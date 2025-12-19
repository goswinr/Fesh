namespace Fesh.Editor

open System
open System.Windows.Threading
open System.Windows.Input

open AvalonEditB
open AvalonEditB.Document

open Fesh.Model
open Fesh.Util
open Fesh.Util.Str
open Fesh


module DocChangeUtil =

    /// one deleted or one inserted character
    let isASingleCharChange (a:DocumentChangeEventArgs) =
        match a.InsertionLength, a.RemovalLength with
        | 1, 0 -> true
        | 0, 1 -> true
        | _    -> false


    let getShift (doc:TextDocument, a:DocumentChangeEventArgs) : Shift =
        let off = a.Offset
        let ins = a.InsertionLength
        let rem = a.RemovalLength
        // count line returns added and deleted:
        let addLns = if ins > 1 then countCharI '\n' a.InsertedText else 0 // a line return is minimum 2 characters
        let remLns = if rem > 1 then countCharI '\n' a.RemovedText  else 0 // a line return is minimum 2 characters
        // printfn $"getShift: off={off} ins={ins} rem={rem} addLns={addLns} remLns={remLns}"
        // printfn $" insText='{a.InsertedText}' remText='{a.RemovedText}'"
        { fromOff = off
          fromLine= doc.GetLocation(off).Line
          amountOff = ins - rem
          amountLines = addLns-remLns}

    /// returns the Line of code that contains the given offset.
    /// from start of line till given offset
    let getLine(code:string, off) =
        let rec loop (i) =
            if i = -1 then 0
            else
                match code[i] with
                | '\n' -> i-1
                | _ -> loop (i-1)
        let st = loop off
        code.Substring(st,off-st)

    let getPosInCode(avaEdit:TextEditor) : PositionInCode =
        let doc = avaEdit.Document
        let car = avaEdit.TextArea.Caret
        let caretOffset = car.Offset
        let ln = doc.GetLineByOffset(caretOffset)
        let caretOffsetInThisLine = caretOffset - ln.Offset
        {
        lineToCaret = doc.GetText(ln.Offset, caretOffsetInThisLine)// this line will include the character that trigger auto completion(dot or first letter)
        lineIdx = car.Line
        column = caretOffsetInThisLine // equal to amount of characters in lineToCaret
        offset = caretOffset
        }


module Redrawing =

    type DrawingServices = {
        folds       : Foldings
        compls      : Completions
        brackets    : BracketHighlighter
        errors      : ErrorHighlighter
        semantic    : SemanticHighlighter
        selection   : SelectionHighlighter
        evalTracker : EvaluationTracker // not tracked by tryDraw !! TODO, runs in sync
        }

    type EventCombiner(services:DrawingServices, state:InteractionState) =
        let ed = state.Editor
        // let mutable scan = ScanState.None

        let priority = DispatcherPriority.Render

        let mutable idBrackets   = 0L
        let mutable idSemantics  = 0L
        let mutable idErrors     = 0L
        let mutable idSels       = 0L
        let mutable idFolds      = 0L

        let tryDraw(id) =
            //printfn $"id={id}, idSemantics={idSemantics}, idBrackets={idBrackets}, idErrors={idErrors}, idSels={idSels}, idFolds={idFolds}"
            if state.IsLatest id && idSemantics=id && idBrackets=id && idErrors=id && idSels=id && idFolds=id then
                    async{
                        do! Async.SwitchToContext Fittings.SyncWpf.context
                        //eprintfn $"Redrawing full: id={id}, idSemantics={idSemantics}, idBrackets={idBrackets}, idErrors={idErrors}, idSels={idSels}, idFolds={idFolds}"
                        // first render then fold do avoid  this ?
                        //System.ArgumentException: Cannot dispose visual line because it is in construction!
                        //at AvalonEditB.Rendering.TextView.DisposeVisualLine(VisualLine visualLine)
                        //at AvalonEditB.Rendering.TextView.ClearVisualLines()
                        //at AvalonEditB.Rendering.TextView.Redraw(DispatcherPriority redrawPriority)
                        //at Fesh.Editor.Redrawing.cloQQ?—153.Invoke(Unit _arg2) in D:\Git\Fesh\Src\Editor\DocChanged.fs:line 99
                        ed.TextArea.TextView.Redraw(priority)
                        // do! Async.Sleep 50 // to try to avoid: InvalidOperationException: Line 117 was skipped by a VisualLineElementGenerator, but it is not collapsed. at AvalonEditB.Rendering.TextView.BuildVisualLine(..)
                        // if state.IsLatest id then
                        services.folds.RedrawFoldings()
                    }
                    |> Async.Start
                // else
                //     IFeshLog.log.PrintfnAppErrorMsg $"Can't redraw, check id that is wrong: id={id}"
                //     IFeshLog.log.PrintfnAppErrorMsg $"idSemantics={idSemantics}, idBrackets={idBrackets}, idErrors={idErrors}, idSels={idSels}, idFolds={idFolds}"


        let doneBrackets(id)   = idBrackets   <- id ;  tryDraw(id)
        let doneSemantics(id)  = idSemantics  <- id ;  tryDraw(id)
        let doneErrors(id)     = idErrors     <- id ;  tryDraw(id)
        let doneFolds(id)      = idFolds      <- id ;  tryDraw(id)
        let doneSels(id)  =
            if idSels <> id then    // Only tryDraw if the id changed, to avoid another full redraw on found selection event, from changing selection,
                idSels <- id        // that might be triggered again and again without a doc change.
                tryDraw(id)         // The found selection does a its own range redraw anyway too.

        do
            services.brackets.FoundBrackets.Add  doneBrackets
            services.semantic.FoundSemantics.Add doneSemantics // includes Bad Indentation and Unused declarations
            services.selection.FoundSels.Add     doneSels
            services.errors.FoundErrors.Add      doneErrors
            services.folds.FoundFolds.Add        doneFolds

module DocChangeMark =
    open Redrawing

    /// milliseconds to wait before starting the first check after a change
    /// only for Single char changes
    let mainWait = 50

    let updateAllTransformersConcurrently(iEd:IEditor, code:string, drawServ:DrawingServices, state:InteractionState, id, lineIdx) =
            // first: Foldings, ColorBrackets when full text available async.
            async{
                state.CodeLines.UpdateLines(code, id)
                if state.IsLatest id then
                    drawServ.selection.UpdateTransformers(id)
                    drawServ.brackets.UpdateAllBrackets(id)
                    drawServ.folds.CheckFolds(id)
                    drawServ.evalTracker.SetLastChangeAt(lineIdx)
            } |> Async.Start

            // second: Errors and Semantic Highlighting and BadIndentation on FCS check result .
            async{
                match Checker.CheckCode(iEd, state, code, id, false, lineIdx) with // code checking does not need to wait for CodeLines.Update
                |None -> ()
                |Some res ->
                    if state.IsLatest id then
                        drawServ.errors.UpdateErrs(res.errors, id)
                        try
                            drawServ.semantic.UpdateSemHiLiTransformers( res.checkRes, id)
                        with e ->
                            AutoFixErrors.check e.Message // check for add a reference to assembly
                            IFeshLog.log.PrintfnAppErrorMsg "Exception in drawServ.semantic.UpdateSemHiLiTransformers: \r\n%s "e.Message

            } |> Async.Start

    //should be called from any thread pool thread
    let updateAllTransformersSync(iEd:IEditor, doc:TextDocument, drawServ:DrawingServices, state:InteractionState, id , lineIdx) =
        match SelectionHighlighting.makeEditorSnapShot(doc,state,id) with
        | None      -> ()
        | Some code -> updateAllTransformersConcurrently (iEd, code, drawServ, state, id, lineIdx)


    // Must be called from UI thread only
    let updateAllTransformersAsync (iEd:IEditor, drawServ:DrawingServices, state:InteractionState, id, lineIdx) =
        let doc = iEd.AvaEdit.Document // get Doc in Sync
        async {
            if mainWait <> 0 then do! Async.Sleep mainWait
            match SelectionHighlighting.makeEditorSnapShot(doc,state,id) with
            | None      -> ()
            | Some code -> updateAllTransformersConcurrently (iEd, code, drawServ, state, id, lineIdx)
        }
        |> Async.Start

type ShowAutocomplete = DoNothing | JustMark| ShowOnlyDU | ShowAll | DeclModifiers | ShowDirectives


[<RequireQualifiedAccess>]
module MaybeShow =

        let isCaretInComment ln =
            NotInQuotes.contains "//" ln

        //--------------------------------------------------------------------------------------------
        //-----------check the four ways to bind a name: let, for, fun, match with | ---------------
        //-------------------------------------------------------------------------------------------

        let private lastIdx inString find txt =
            if inString then NotInQuotes.lastIndexOfFromInside  find txt
            else             NotInQuotes.lastIndexOfFromOutside find txt

        /// like lastIndex but test if its the first char or preceded by a space
        let private lastIdxAtStartOrWithSpace inString find txt =
            let i =
                if inString then NotInQuotes.lastIndexOfFromInside  find txt
                else             NotInQuotes.lastIndexOfFromOutside find txt
            if   i = -1 then -1
            elif i = 0 || txt.[i-1] = ' ' then i // test if its the first char or preceded by a space
            else -1

        let private containsFrom idx (find:string) (txt:String) =
            match txt.IndexOf(find,idx,StringComparison.Ordinal) with
            | -1 -> false
            | _  -> true

        /// Is a discriminated union that wants autocomplete
        let private isDUorKeyword checkForKeyW fromIdx (ln:string) =
            let fi = indexOfFirstNonWhiteAfter fromIdx ln
            if fi < fromIdx then
                ShowAll // fromIdx-1 returned, non white letter was not found
            else
                let first = ln.[fi]

                if 'A' <= first && first <= 'Z' then // starts with a capital letter , TODO or use Char.isUpper for full Unicode spectrum ?
                    match ln.IndexOf(' ',fi) with // and has no space
                    | -1 ->
                        match ln.IndexOf('(',fi) with // and has no open bracket
                        | -1 -> ShowOnlyDU
                        | _ -> JustMark // writing a lowercase name binding as part of the uppercase DU's value
                    | _ -> JustMark

                elif 'a' <= first && first <= 'z' then    // a lower case identifier or a keyword
                    if checkForKeyW && ln.Length - fi > 2 then
                        match ln.Substring(fi,3) with
                        | "pri" ->  DeclModifiers // private
                        | "mut" ->  DeclModifiers // mutable
                        | "int" ->  DeclModifiers // internal
                        | "inl" ->  DeclModifiers // inline
                        | _     ->  JustMark
                    else
                        JustMark // writing a lowercase name binding
                else
                    ShowAll // writing not a DU but an operator like |> or |]

        let private isKeyword fromIdx ln =
            let fi = indexOfFirstNonWhiteAfter fromIdx ln
            if fi < fromIdx then
                false // fromIdx-1 returned, non white letter was not found
            else
                let first = ln.[fi]
                if 'A' <= first && first <= 'Z' then // starts with a capital letter , TODO or use Char.isUpper for full Unicode spectrum ?
                    false
                elif 'a' <= first && first <= 'z' then    // a lower case identifier or a keyword
                    if ln.Length - fi > 2 then
                        match ln.Substring(fi,3) with
                        | "pri" ->  true // private
                        | "mut" ->  true // mutable
                        | "int" ->  true // internal
                        | "inl" ->  true // inline
                        | _     ->  false
                    else
                        false // writing a lowercase name binding
                else
                    false // writing not a DU but an operator like |> or |]

        let isHashDirective inStr (ln:string) =
            let hashIdx = lastIdxAtStartOrWithSpace inStr "#" ln // test if its the first char or preceded by a space
            if hashIdx = -1 then ShowAll
            else ShowDirectives

        let isLetDeclaration inStr (ln:string)  =
            //test if we are after a 'let' but before a '=' or ':'
            let letIdx = lastIdxAtStartOrWithSpace inStr "let " ln // test if its the first char or preceded by a space
            if letIdx = -1 then ShowAll
            else
                let eqIdx    = lastIdx inStr "=" ln
                let colonIdx = lastIdx inStr ":" ln
                // a : or = is after the let, so show all completions
                if max eqIdx colonIdx < letIdx then isDUorKeyword true (letIdx+3) ln  else ShowAll

        let isFunDeclaration inStr (ln:string) =
            //test if we are after a 'fun' but before a '->' or ':'
            let funIdx = max (lastIdx inStr " fun " ln) (lastIdx inStr "(fun " ln)
            if funIdx = -1 then ShowAll
            else
                let eqIdx    = lastIdx inStr  "->" ln
                let colonIdx = lastIdx inStr  ":" ln
                if (max eqIdx colonIdx) < funIdx then isDUorKeyword false (funIdx+4) ln else ShowAll

        let isForDeclaration inStr (ln:string) =
            let forIdx = lastIdxAtStartOrWithSpace inStr "for " ln // test if its the first char or preceded by a space
            if forIdx = -1 then  ShowAll
            else
                if   lastIdx inStr " in "     ln > forIdx then ShowAll
                elif lastIdx inStr " to "     ln > forIdx then ShowAll
                elif lastIdx inStr " downto " ln > forIdx then ShowAll
                else isDUorKeyword false (forIdx+3) ln

        let isBarDeclaration inStr (ln:string) = // also covers the 'as' binding
            let barIdx = lastIdxAtStartOrWithSpace inStr "|" ln // test if its the first char or preceded by a space
            if barIdx = -1 then
                ShowAll
            else
                if   containsFrom barIdx "->"      ln then ShowAll
                elif containsFrom barIdx "."       ln then ShowAll  // a DU member with full Qualification
                elif containsFrom barIdx " when "  ln then ShowAll
                elif containsFrom barIdx " of "    ln then ShowAll
                elif containsFrom barIdx ":?"      ln then  (if containsFrom barIdx  " as " ln then JustMark   else ShowAll)
                else isDUorKeyword false (barIdx+1) ln

        let isThisMemberDeclaration inStr (ln:string) = // to not autocomplete on 'member this' (before the dot) but on keywords
            let memIdx = lastIdxAtStartOrWithSpace inStr "member" ln // test if its the first char or preceded by a space
            if memIdx = -1 then
                ShowAll
            else
                if   containsFrom memIdx "."  ln then ShowAll
                elif isKeyword memIdx ln then DeclModifiers
                else JustMark


        let bind (f:bool->string->ShowAutocomplete) inStr ln (prev:ShowAutocomplete) =
            match prev with
            |ShowAll      -> f inStr ln
            |ShowOnlyDU   -> ShowOnlyDU
            |JustMark     -> JustMark
            |DoNothing    -> DoNothing
            |DeclModifiers -> DeclModifiers
            |ShowDirectives -> ShowDirectives

        let completionWindow ( pos:PositionInCode) : ShowAutocomplete =
            let ln = pos.lineToCaret // this line will include the character that trigger auto completion(dot or first letter)
            let len = ln.Length
            //IFeshLog.log.PrintfnDebugMsg "*2.1 maybeShowCompletionWindow for lineToCaret: \r\n    '%s'" ln
            if len=0 then // line is empty,
                ShowAll // ctrl+space was triggered at start of line empty line, // not JustMark, an empty line after deleting is caught in singleCharChange() already
            else
                let last = ln.[len-1]
                if isCaretInComment ln then
                    if last = '/' then
                        JustMark // to make sure comment was not just typed (then still check)
                    else
                        // IFeshLog.log.PrintfnDebugMsg " DoNothing because isCaretInComment: %s" ln
                        // DoNothing, we are typing somewhere in a comment
                        // TODO! because of this changing a text in a comment is not immediately picked up by selection highlighter (if present). only after another change in the code.
                        DoNothing
                else
                    let inStr = not <| NotInQuotes.isLastCharOutsideQuotes ln
                    isLetDeclaration                inStr ln // TODO rewrite with a beautiful monad
                    |> bind isFunDeclaration        inStr ln
                    |> bind isForDeclaration        inStr ln
                    |> bind isBarDeclaration        inStr ln
                    |> bind isThisMemberDeclaration inStr ln
                    |> bind isHashDirective         inStr ln
            //|> (fun x -> printfn $"*2.2 maybeShowCompletionWindow for lineToCaret: \r\n    '{ln}'\r\n    is {x}"; x)

        /// checks if it is a letter or a digit preceded by a letter
        let inline isAlpha c =
            //(c >= 'a' && c <= 'z')|| (c >= 'A' && c <= 'Z')
            Char.IsLetter c

        let inline isNum c =
            c >= '0'  && c <= '9'

        /// checks if the second or third last character  is a letter
        let inline isAlphaBefore (pos:PositionInCode) =
            pos.column > 1 &&  isAlpha pos.lineToCaret[pos.column-1]
            ||
            pos.column > 2 &&  isNum pos.lineToCaret[pos.column-1] && isAlpha pos.lineToCaret[pos.column-2]


        /// even if ther is a digit before the dot it might be part of a name
        /// so this checks if before the dot is a number or a name
        let isNotInNumber (pos:PositionInCode) =
            let rec loop i =
                if i = -1 then false // start of line reached no letter found yet
                else
                    let c = pos.lineToCaret.[i]
                    if c = '_' then loop (i-1) // can be vailid numbers, so loop on
                    elif Char.IsLetter c then true
                    elif Char.IsDigit c then loop (i-1)
                    else false
            loop (pos.column-2)



        let inline getCtrlDown() = Keyboard.IsKeyDown Key.LeftCtrl || Keyboard.IsKeyDown Key.RightCtrl // can't be async
        let inline getSpaceDown() = Keyboard.IsKeyDown Key.Space // can't be async

        let inline lastCharTriggersCompletion (lastChar, pos) =
            match lastChar with
            | c when isAlpha c -> true // a ASCII letter
            | c when isNum c && isAlphaBefore pos  -> true // an number preceded by a letter
            | '.' when isNotInNumber pos -> true // dot completion
            | '_'  // for __SOURCE_DIRECTORY__ or in names
            //| '`'  // for complex F# names in `` `` // not needed
            | '#'  -> true // for #if directives
            | _    -> false


module DocChangeCompletion =
    open DocChangeUtil
    open Redrawing
    open FSharp.Compiler.EditorServices
    open FSharp.Compiler.Tokenization

    let keywords = [| for kw, _desc in FSharpKeywords.KeywordsWithDescription  do kw |]

    //pre check (still async) if the posX.query is contained in any name in the completion list
    // let containsQuery (name:string, decls:DeclarationListInfo)=
    //     decls.Items  |> Array.exists (fun d -> d.NameInList.IndexOf( name, StringComparison.OrdinalIgnoreCase) >= 0)
    //     ||
    //     keywords |> Array.exists (fun kw -> kw.IndexOf( name, StringComparison.OrdinalIgnoreCase) >= 0)



    let handelShow(pos:PositionInCode, doc:TextDocument, iEd:IEditor, drawServ:DrawingServices, state:InteractionState, chId:int64) =

        let inline check() = Checker.CheckCode(iEd, state, doc.CreateSnapshot().Text , chId, true, pos.lineIdx )

        let inline getDecls(posX:PositionInCodeEx) : DeclarationListInfo option =
            let res =
                // re-check every time?
                // Timer.InstanceRedraw.tic()
                // let r = check() // can take up to 500ms on file with 1k lines
                // let t = Timer.InstanceRedraw.tocEx
                // IFeshLog.log.PrintfnDebugMsg $"check time for compl: {t}"
                // r

                // Or reuse old result? TODO: refresh the completion window when the new result is available?
                // if posX.lineToCaret.EndsWith "." then // not even that seems to be necessary
                //     check()
                // else
                    match iEd.FileCheckState with
                    | Done r     ->
                        if r.changeId <> chId then async{check()|> ignore} |> Async.Start // the change id is not the same as the current one, so the check is outdated, start a new check in the background
                        Some r // but return the old result
                    | WaitForCompl r | WaitForErr r -> Some r
                    | NotChecked -> check()

            match res with // false for not aborting on a new check id
            | None -> None
            | Some res -> Checker.GetDeclarations(posX,res)
            // if containsQuery(posX.query, decls) then


        async{
            let show = MaybeShow.completionWindow(pos)

            match show with
            |DoNothing  ->
                // DoNothing means the cursor is typing in a comment:
                // BUT ! Don't just do nothing!!
                // Even when typing in a comment, the checker should run because it makes a new change ID and that new change ID would
                // abort any still  running checker and not trigger a new one. unless we do it here.
                do! Async.Sleep (300)
                DocChangeMark.updateAllTransformersSync(iEd, doc, drawServ, state, chId, pos.lineIdx )

            |JustMark ->
                DocChangeMark.updateAllTransformersSync(iEd, doc, drawServ, state, chId, pos.lineIdx  )

            | ShowAll | ShowOnlyDU | DeclModifiers | ShowDirectives ->
                // WaitForCompletions: from now on any typed character will NOT increment the doc change
                // AND never trigger a checking or or new completion window
                // the characters go to the doc and will be taken from there as a prefilter for the completion list
                state.DocChangedConsequence <- WaitForCompletions // incrementing  IDs is disabled now

                let posX = PositionInCodeEx.get(pos)

                let showListOpt : option<RestrictedShowList> =
                    match show with
                    | ShowAll        -> getDecls(posX)|> Option.map JustAllFrom //|> Option.orElse  ( if posX.dotBefore then None else Some JustDeclModifiers) // if nod declarations where found, and there was no dot before, try show keywords
                    | ShowOnlyDU     -> getDecls(posX)|> Option.map JustDuFrom
                    | DeclModifiers  -> Some JustDeclModifiers
                    | ShowDirectives -> Some JustDirectives
                    | DoNothing      -> None // never reached
                    | JustMark       -> None // never reached

                match showListOpt with
                | Some showList ->
                    // Switch to Sync and try showing completion window:
                    let checkAndMark = fun () -> DocChangeMark.updateAllTransformersAsync (iEd, drawServ, state, state.Increment(), pos.lineIdx ) // will be called if window closes without an insertion

                    do! Async.SwitchToContext Fittings.SyncWpf.context
                    match drawServ.compls.TryShow( posX, showList, checkAndMark) with
                    |NoShow ->
                        state.DocChangedConsequence <- React
                        DocChangeMark.updateAllTransformersAsync(iEd, drawServ, state, state.Increment(), pos.lineIdx )// incrementing because it was disabled from state.DocChangedConsequence <- WaitForCompletions
                    |DidShow ->
                        // completion window is showing now,
                        // no need to do anything,
                        // DocChangedConsequence will be updated to 'React' when completion window closes
                        // and checkAndMark will be called.
                        ()
                | None ->
                    state.DocChangedConsequence <- React
                    DocChangeMark.updateAllTransformersSync (iEd, doc, drawServ, state, state.Increment(), pos.lineIdx )

        } |> Async.Start



    /// for single character edits
    let singleCharChange (iEd:IEditor, drawServ:DrawingServices, state:InteractionState, chId:int64, lineIdx )  =
        let pos = getPosInCode(iEd.AvaEdit)
        let tx = pos.lineToCaret
        if tx.Length = 0 then // empty line after deleting
            DocChangeMark.updateAllTransformersAsync (iEd, drawServ, state, chId, lineIdx )
        else
            let lastChar = tx[tx.Length-1]
            if lastChar <> '.' && state.JustCompleted then
                // if it is not a dot avoid re-trigger of completion window on single character completions, just do check and mark
                state.JustCompleted <- false // reset it
                DocChangeMark.updateAllTransformersAsync (iEd, drawServ, state, chId, lineIdx )
            else
                let doc = iEd.AvaEdit.Document // get in sync
                async{
                    if DocChangeMark.mainWait <> 0 then
                        do! Async.Sleep DocChangeMark.mainWait  // to not trigger completion if typing is fast

                    if state.IsLatest chId then
                        if not <| MaybeShow.lastCharTriggersCompletion (lastChar, pos) then
                            // The typed character should not trigger completion.
                            // DocChangedConsequence is still  'React', no need to reset.
                            DocChangeMark.updateAllTransformersSync(iEd, doc, drawServ, state, chId, lineIdx )
                        else
                            handelShow(pos, doc,iEd, drawServ, state, chId)
                } |> Async.Start



module DocChangeEvents =
    open DocChangeUtil

    let ctrlSpace(iEd:IEditor, drawServ:Redrawing.DrawingServices, state:InteractionState, eventArgs:KeyEventArgs) =
        match state.DocChangedConsequence with
        | WaitForCompletions -> ()
        | React ->
            if eventArgs.Key = Key.Space && MaybeShow.getCtrlDown() then
                eventArgs.Handled <- true // to not actually insert a space
                let pos = getPosInCode(iEd.AvaEdit)
                let doc = iEd.AvaEdit.Document // get in sync
                DocChangeCompletion.handelShow (pos, doc, iEd, drawServ, state, state.DocChangedId.Value)


    let maybeAdjustCursor(iEd:IEditor, eventArgs:DocumentChangeEventArgs) =
        let t = eventArgs.InsertedText
        if t.TextLength = 12 && t.Text  = "#r \"nuget: \""
        || t.TextLength = 5 &&  t.Text  = "#r \"\""         then
            let c = iEd.AvaEdit.TextArea.Caret
            c.Offset <- c.Offset - 1



    // gets called before the document actually changes
    let changing(state:InteractionState, a:DocumentChangeEventArgs) =
        // (1) increment change counter
        match state.DocChangedConsequence with
        | WaitForCompletions ->
            //IFeshLog.log.PrintfnAppErrorMsg $"wait for '{a.InsertedText.Text}'"
            ()
        | React ->
            //IFeshLog.log.PrintfnDebugMsg $"react for '{a.InsertedText.Text}'"
            state.Increment() |> ignore // incrementing this handler before the change actually happens, but only increment when a reaction is required.

        // (2) Adjust Shifts
        let shift = getShift(state.Editor.Document, a)
        state.FastColorizer.AdjustShifts shift
        state.ErrSegments.AdjustOneShift shift

    let changed (iEd:IEditor, drawServ:Redrawing.DrawingServices, state:InteractionState, eventArgs:DocumentChangeEventArgs) : unit  =
        match state.DocChangedConsequence with
        | WaitForCompletions ->
            // no type checking ! just keep on tying,
            // the typed characters wil become a prefilter for the  in completion window
            ()
        | React ->
            let id = state.DocChangedId.Value // the increment was done before this event in Doc.Changing (not Doc.Changed)
            let lineIdx = iEd.AvaEdit.Document.GetLineByOffset( eventArgs.Offset).LineNumber
            if isASingleCharChange eventArgs then
                DocChangeCompletion.singleCharChange     (iEd, drawServ, state, id, lineIdx)// maybe shows completion window
            else
                maybeAdjustCursor(iEd, eventArgs) // maybe shows completion window
                DocChangeMark.updateAllTransformersAsync (iEd, drawServ, state, id, lineIdx)


    (*
    // used with a auto hotkey script that simulates 28 key presses starting with ß ending with £
    let logPerformance (t:string)=
        if   t = "ß" then Timer.InstanceRedraw.tic()
        elif t = "£" then  eprintfn $"{Timer.InstanceRedraw.tocEx}"
    *)



    (* https://github.com/icsharpcode/AvalonEdit/blob/master/ICSharpCode.AvalonEdit.Sample/document.html

    type ChangeReason = Semantic | Selection | BadIndent | MatchingBrackets | CurrentBracketPair | CheckerError

        Change Events:
            Here is the order in which events are raised during a document update:
            BeginUpdate()

            UpdateStarted event is raised
            Insert() / Remove() / Replace()

            Changing event is raised
            The document is changed
            TextAnchor.Deleted events are raised if anchors were in the deleted text portion
            Changed event is raised
            EndUpdate()

            TextChanged event is raised
            TextLengthChanged event is raised
            LineCountChanged event is raised
            UpdateFinished event is raised
        If the insert/remove/replace methods are called without a call to BeginUpdate(), they will call BeginUpdate() and EndUpdate() to ensure no change happens outside of UpdateStarted/UpdateFinished.

        There can be multiple document changes between the BeginUpdate() and EndUpdate() calls. In this case, the events associated with EndUpdate will be raised only once after the whole document update is done.

        The UndoStack listens to the UpdateStarted and UpdateFinished events to group all changes into a single undo step.
        *)