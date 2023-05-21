namespace Seff.Editor

open System

open AvalonEditB
open AvalonEditB.Document

open Seff.Model
open Seff.Util
open Seff.Util.Str
open Seff


type EditorServices = {
    folds       : Foldings
    brackets    : BracketHighlighter
    errors      : ErrorHighlighter
    semantic    : SemanticHighlighter
    compls      : Completions  
    selection   : SelectionHighlighter
    evalTracker : EvaluationTracker
    }

module DocChangeUtil = 
   
    
    /// returns the total character count change -1 or +1 depending if its a insert or remove
    let isSingleCharChange (a:DocumentChangeEventArgs) =
        match a.InsertionLength, a.RemovalLength with
        | 1, 0 -> ValueSome  1
        | 1, 1 -> ValueSome  0
        | 0, 1 -> ValueSome -1
        | _    -> ValueNone
    
    /// returns the cLine of code that contains the given offset.
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
    
    let getPosInCode(caretOff, line, code:string): PositionInCode  =        
        let lineToCaret = getLine(code, caretOff)
        { 
        lineToCaret = lineToCaret// this line will include the character that trigger auto completion(dot or first letter)
        row =    line
        column = lineToCaret.Length // equal to amount of characters in lineToCaret
        offset = caretOff 
        }

    let getPosInCode2(avaEdit:TextEditor) : PositionInCode =         
        let doc = avaEdit.Document
        let car = avaEdit.TextArea.Caret
        let caretOffset = car.Offset
        let ln = doc.GetLineByOffset(caretOffset)
        let caretOffsetInThisLine = caretOffset - ln.Offset
        { 
        lineToCaret = doc.GetText(ln.Offset, caretOffsetInThisLine)// this line will include the character that trigger auto completion(dot or first letter)
        row =    car.Line
        column = caretOffsetInThisLine // equal to amount of characters in lineToCaret
        offset = caretOffset 
        }

    /// checks if it is a letter or a digit preceded by a letter 
    let inline isInFsharpIdentifier (c , p:PositionInCode) = 
        if Char.IsLetter c then 
            true
        elif Char.IsDigit c then // TODO allow several digits after letter too ?
            let tx = p.lineToCaret
            let len = tx.Length
            len > 1 && Char.IsLetter(tx[len-1] )
        else
            false

    let isCaretInComment ln =  
        NotInQuotes.contains "//" ln
    

module Redrawing = 

    [<Flags;RequireQualifiedAccess>]
    type Scan1State =
        | None      = 0b0000000
        | BadIndent = 0b0000001
        | Brackets  = 0b0000010
        | All       = 0b0000011


    type FirstEventCombiner(serv:EditorServices, state:InteractionState) = 
        let ed = state.Editor
        let fast = state.FastColorizer
        let mutable scan = Scan1State.None       
    
        let tryDraw() =  
            if scan = Scan1State.All then                 
                scan <- Scan1State.None 
                //fast.ResetShift()
                //printfn $"*Redraw BadInd+Brackets, {serv.brackets.TransformerLineCount} lines"
                ed.Dispatcher.Invoke (fun() -> ed.TextArea.TextView.Redraw()) //TODO only redraw parts of the view, or lower priority ?    

        let doneBadIndents() = scan <- scan ||| Scan1State.BadIndent;  tryDraw()
        let doneBrackets()   = scan <- scan ||| Scan1State.Brackets ;  tryDraw()             

        do
            serv.folds.FoundBadIndents.Add doneBadIndents
            serv.brackets.FoundBrackets.Add doneBrackets

    [<Flags;RequireQualifiedAccess>]
    type Scan2State =
        | None      = 0b0000000
        | Semantics = 0b0000001
        | Errors    = 0b0000010
        | All       = 0b0000011


    type SecondEventCombiner(serv:EditorServices, state:InteractionState) = 
        let ed = state.Editor
        let fast = state.FastColorizer
        let mutable scan = Scan2State.None        
    
        let tryDraw(msg:string) = 
            //ISeffLog.printnColor 99 99 99 $"{msg} done"
            if scan = Scan2State.All then                 
                scan <- Scan2State.None
                fast.ResetShift()
                //printfn $"*Redraw Err+Semantic, {serv.semantic.TransformerLineCount} lines*"
                ed.Dispatcher.Invoke (fun() -> ed.TextArea.TextView.Redraw()) //TODO only redraw parts of the view, or lower priority ?    
    
        let doneSemantics()  = scan <- scan ||| Scan2State.Semantics;  tryDraw("Sem")
        let doneErrors()     = scan <- scan ||| Scan2State.Errors   ;  tryDraw("Errs")
        
        do
            serv.semantic.FoundSemantics.Add doneSemantics
            serv.errors.FoundErrors.Add doneErrors



module DocChangeMark = 
    open DocChangeUtil
    open CodeLineTools

    let markTwoSteps(iEd:IEditor, code, serv:EditorServices, state:InteractionState, id) = 
            /// first: Foldings, ColorBrackets and BadIndentation when full text available async.
            async{
                if state.CodeLines.Update(code,id) then 
                    state.TransformersAllBrackets.ClearAllLines()// do as late as possible , offset shifting should do its work   
                    serv.folds.UpdateFoldsAndBadIndents(id)
                    if state.IsLatest id then 
                        serv.brackets.UpdateAllBrackets( id)
             } |> Async.Start 
        
            /// second: Errors and Semantic Highlighting on check result .  
            async{ 
                match Checker.CheckCode(iEd, state, code, id, true) with // code checking does not need to wait for CodeLines.Update
                |None -> ()
                |Some res ->
                    if state.IsLatest id then 
                        serv.errors.UpdateErrs(res.errors, id)
                        if state.IsLatest id then 
                            serv.semantic.UpdateSemHiLiTransformers( res.checkRes, id)
            
            } |> Async.Start   
    
    //To be called from any thread
    let markFoldCheckHighlight(iEd:IEditor, doc:TextDocument, serv:EditorServices, state:InteractionState, id ) =        
        // NOTE just checking only Partial Code till caret with (doc.CreateSnapshot(0, tillOffset).Text) 
        // would make the GetDeclarationsList method miss some declarations !!
        let code = doc.CreateSnapshot().Text // the only threadsafe way to access the code string
        if state.DocChangedId.Value = id then 
            markTwoSteps (iEd, code, serv, state, id)
      
    // To be called from UI thread
    let markFoldCheckHighlightAsync (iEd:IEditor, serv:EditorServices, state:InteractionState, id ) =
        let doc = iEd.AvaEdit.Document // get Doc in Sync
        async { 
            do! Async.Sleep 100
            // NOTE just checking only Partial Code till caret with (doc.CreateSnapshot(0, tillOffset).Text) 
            // would make the GetDeclarationsList method miss some declarations !!
            let code = doc.CreateSnapshot().Text // the only threadsafe way to access the code string
            if state.DocChangedId.Value = id then 
                markTwoSteps (iEd, code, serv, state, id)
        } |> Async.Start
    
module DocChangeCompletion = 
    open DocChangeUtil

    type ShowAutocomplete = DoNothing| JustMark| ShowOnlyDU | ShowAll  

    [<RequireQualifiedAccess>]
    module MaybeShow = 


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
            | _ -> true 
    
        /// is a discriminated union that wants autocomplete
        let private isDU fromIdx ln =
            //printfn $"indexOfFirstNonWhiteAfter fromIdx {fromIdx} of '{ln}'"
            let fi = indexOfFirstNonWhiteAfter fromIdx ln
            if fi < fromIdx then 
                ShowAll // fromIdx-1 returned, non white letter was not found
            else
                //printfn $"getting {fi} of '{ln}'"
                let first = ln.[fi]
                if 'A' <= first && first <= 'Z' then // starts with a capital letter , TODO or use Char.isUpper for full Unicode spectrum ?
                    match ln.IndexOf(' ',fi) with // and has no space 
                    | -1 -> 
                        match ln.IndexOf('(',fi) with // and has no open bracket
                        | -1 -> ShowOnlyDU 
                        | _ -> JustMark // writing a lowercase name binding as part of the uppercase DU's value
                    | _ -> JustMark
                elif 'a' <= first && first <= 'z' then
                    JustMark // writing a lowercase name binding             
                else
                    ShowAll // writing not a DU but an operator like |> or |]


        let isLetDeclaration inStr (ln:string)  = 
            //test if we are after a 'let' but before a '=' or ':'  
            let letIdx = lastIdxAtStartOrWithSpace inStr "let " ln // test if its the first char or preceded by a space 
            if letIdx = -1 then ShowAll
            else
                let eqIdx    = lastIdx inStr "=" ln                       
                let colonIdx = lastIdx inStr ":" ln   
                if (max eqIdx colonIdx) < letIdx then isDU (letIdx+3) ln else ShowAll

        let isFunDeclaration inStr (ln:string) = 
            //test if we are after a 'fun' but before a '->' or ':'  
            let funIdx = max (lastIdx inStr " fun " ln) (lastIdx inStr "(fun " ln) 
            if funIdx = -1 then ShowAll
            else
                let eqIdx    = lastIdx inStr  "->" ln                       
                let colonIdx = lastIdx inStr  ":" ln   
                if (max eqIdx colonIdx) < funIdx then isDU (funIdx+4) ln else ShowAll
        
        let isForDeclaration inStr (ln:string) =         
            let forIdx = lastIdxAtStartOrWithSpace inStr "for " ln // test if its the first char or preceded by a space 
            if forIdx = -1 then  ShowAll
            else 
                if   lastIdx inStr " in "     ln > forIdx then ShowAll 
                elif lastIdx inStr " to "     ln > forIdx then ShowAll 
                elif lastIdx inStr " downto " ln > forIdx then ShowAll 
                else isDU (forIdx+3) ln
        
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
                else isDU (barIdx+1) ln         
                
        let isThisMemberDeclaration inStr (ln:string) = // to not autocomplete on 'member this' (before the dot)
            let barIdx = lastIdxAtStartOrWithSpace inStr "member" ln // test if its the first char or preceded by a space 
            if barIdx = -1 then  
                ShowAll
            else 
                if   containsFrom barIdx "."  ln then ShowAll                
                else JustMark 

    
        let bind (f:bool->string->ShowAutocomplete) inStr ln (prev:ShowAutocomplete) = 
            match prev with
            |ShowAll    -> f inStr ln
            |ShowOnlyDU -> ShowOnlyDU
            |JustMark   -> JustMark
            |DoNothing  -> DoNothing
    
        let completionWindow ( pos:PositionInCode) : ShowAutocomplete = 
            let ln = pos.lineToCaret // this line will include the character that trigger auto completion(dot or first letter)
            let len = ln.Length
            //ISeffLog.log.PrintfnDebugMsg "*2.1 maybeShowCompletionWindow for lineToCaret: \r\n    '%s'" ln
            if len=0 then // line is empty, still check because deleting might have removed errors.
                JustMark
            else
                let last = ln.[len-1]
                if isCaretInComment ln then 
                    if last = '/' then 
                        JustMark // to make sure comment was not just typed (then still check)
                    else 
                        // ISeffLog.log.PrintfnDebugMsg " DoNothing because isCaretInComment: %s" ln
                        // DoNothing, we are typing somewhere in a comment
                        DoNothing 
                else
                    let inStr = not <| NotInQuotes.isLastCharOutsideQuotes ln                     
                    isLetDeclaration                inStr ln // TODO rewrite with a beautiful monad
                    |> bind isFunDeclaration        inStr ln
                    |> bind isForDeclaration        inStr ln
                    |> bind isBarDeclaration        inStr ln
                    |> bind isThisMemberDeclaration inStr ln
     
    let inline showOnLastChar (lastChar, pos) = 
        lastChar = '_' || // for __SOURCE_DIRECTORY__
        lastChar = '`' || // for complex F# names in `` ``
        lastChar = '#' || // for #if directives
        lastChar = '.' || // for dot completion
        isInFsharpIdentifier(lastChar,pos) 



    /// for single character edits
    let singleCharChange (iEd:IEditor, serv:EditorServices, state:InteractionState, id:int64)  =        
        let pos = getPosInCode2(iEd.AvaEdit)
        //ISeffLog.log.PrintfnDebugMsg $"singleCharChange:\r\n{pos}"
        let tx = pos.lineToCaret        
        if tx.Length = 0 then // empty line after deleting
            DocChangeMark.markFoldCheckHighlightAsync (iEd, serv, state, id)    
        else
            let lastChar = tx[tx.Length-1]
            if lastChar <> '.' && state.JustCompleted then 
                // if it is not a dot avoid re-trigger of completion window on single character completions, just check
                state.JustCompleted <- false // reset it
                DocChangeMark.markFoldCheckHighlightAsync (iEd, serv, state, id)
            
            else 
                let doc = iEd.AvaEdit.Document // get in sync
                async{                
                    do! Async.Sleep 100                    
                    if showOnLastChar (lastChar,pos) then
                        let show = MaybeShow.completionWindow(pos)
                        //ISeffLog.log.PrintfnDebugMsg $"MaybeShow.completionWindow for {lastChar} is {show}"
                        match show with 
                        |DoNothing  -> ()

                        |JustMark   -> DocChangeMark.markFoldCheckHighlight(iEd, doc, serv, state, id)
                        
                        |ShowAll    
                        |ShowOnlyDU -> 
                            state.DocChangedConsequence <- WaitForCompletions
                            let mutable fullCode = ""
                            let declsPosx  = 
                                Monads.maybe{
                                    let! _          = state.IsLatestOpt id
                                    let code        = doc.CreateSnapshot().Text // the only threadsafe way to access the code string
                                    fullCode <- code
                                    let! _          = state.IsLatestOpt id
                                    let! res        = Checker.CheckCode(iEd, state, code, id, false)
                                    let! decls, pos = Checker.GetCompletions(pos,res) 
                                    let! _          = state.IsLatestOpt id
                                    return decls, pos
                                }
                        
                            match declsPosx with         
                            |Some (decls, posx) ->
                                // Switsch to Sync and try showing completion window:
                                do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context                            
                                let onlyDU = show = ShowOnlyDU
                                let checkAndMark() = DocChangeMark.markFoldCheckHighlightAsync (iEd, serv, state, state.DocChangedId.Value) // will be called if window closes without an insertion
                                match serv.compls.TryShow(decls, posx, onlyDU, checkAndMark ) with 
                                |DidShow ->                                     
                                    () // no need to do anything, DocChangedConsequence will be updated to 'React' when completion window closes
                                |NoShow -> 
                                    state.DocChangedConsequence <- React
                                    do! Async.SwitchToThreadPool()
                                    DocChangeMark.markTwoSteps(iEd, fullCode, serv, state, id)
                            |None -> 
                                state.DocChangedConsequence <- React
                                do! Async.SwitchToThreadPool()
                                if fullCode="" then 
                                    fullCode <- doc.CreateSnapshot().Text
                                if state.DocChangedId.Value = id then 
                                    DocChangeMark.markTwoSteps (iEd, fullCode, serv, state, id)                            
                    else   
                        // the typed charater should not trigger completion.                 
                        // DocChangedConsequence is still  'React', no need to reset.
                        DocChangeMark.markFoldCheckHighlight(iEd, doc, serv, state, id)
            
                } |> Async.Start
        

module DocChangeEvents = 
    open DocChangeUtil   

    let changing (fastColor:FastColorizer) (a:DocumentChangeEventArgs) =             
        match DocChangeUtil.isSingleCharChange a with 
        |ValueSome s -> 
            fastColor.AdjustShift {from=a.Offset; amount=s}
        |ValueNone   -> 
            //a multi character change, just wait for type checker.., 
            //because it might contain a line rturen and then just doing a shift would not work anymore
            fastColor.ResetShift() 
    

    let changed (iEd:IEditor) (serv:EditorServices) (state:InteractionState) (eventArgs:DocumentChangeEventArgs) : unit  =       
        serv.errors.InvalidateErrorTransformers()
        match state.DocChangedConsequence with 
        | WaitForCompletions -> 
            // Do not increment DoChangeID counter, this would cancel the showing of the completion window.
            // no type checking ! just keep on tying, 
            // the typed characters wil become a prefilter for the  in completion window
            ()
        | React -> 
            let id = state.Increment() // only increment when a reaction is required
            state.Caret <- state.Editor.CaretOffset
            match isSingleCharChange eventArgs with 
            |ValueSome _ -> DocChangeCompletion.singleCharChange (iEd, serv, state, id)
            |ValueNone   -> DocChangeMark.markFoldCheckHighlightAsync (iEd, serv, state, id)            

            

        