namespace Seff.Editor

open System

open AvalonEditB
open AvalonEditB.Document

open Seff.Model
open Seff.Util
open Seff.Util.Str
open Seff
open System.Windows.Threading


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
        let rec insCount from i = match a.InsertedText.IndexOf('\n', from, ins-from)  with | -1 -> i | found  -> insCount (i+1) found
        let rec remCount from i = match a.RemovedText.IndexOf ('\n', from, rem-from)  with | -1 -> i | found  -> remCount (i+1) found
        let addLns = if ins > 1 then insCount 0 0 else 0 // a line return is minimum 2 characters
        let remLns = if rem > 1 then remCount 0 0 else 0 // a line return is minimum 2 characters 
        { fromOff = off 
          fromLine= doc.GetLocation(off).Line
          amountOff = ins - rem
          amountLines = addLns-remLns} 
    
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

    type DrawingServices = {
        folds       : Foldings
        compls      : Completions
        brackets    : BracketHighlighter
        errors      : ErrorHighlighter
        semantic    : SemanticHighlighter
        selection   : SelectionHighlighter
        evalTracker : option<EvaluationTracker>
        }


    [<Flags;RequireQualifiedAccess>]
    type ScanState =
        | None      = 0b0000001
        | Brackets  = 0b0000010
        | Semantics = 0b0000100
        | Errors    = 0b0001000
        | Selects   = 0b0010000
        | All       = 0b0011111

    type EventCombiner(services:DrawingServices, state:InteractionState) = 
        let ed = state.Editor
        let mutable scan = ScanState.None 

        let priority = DispatcherPriority.Input //.Render
       
        let tryDraw(id) =             
            if scan = ScanState.All && state.IsLatest id then                 
                scan <- ScanState.None  // this reset also happens on Editor.SetUp.Document.Changing.Add(fun _ -> ....)          
                //eprintfn "full Transformers TextView.Redraw()"
                ed.Dispatcher.Invoke (fun() -> ed.TextArea.TextView.Redraw(priority)) //TODO review priority. Render is default ?    
        
  
        let doneBrackets(id)   = scan <- scan ||| ScanState.Brackets ;  tryDraw(id)             
        let doneSemantics(id)  = scan <- scan ||| ScanState.Semantics;  tryDraw(id)
        let doneErrors(id)     = scan <- scan ||| ScanState.Errors   ;  tryDraw(id)
        let doneSels(id)       = scan <- scan ||| ScanState.Selects  ;  tryDraw(id)

        do  
            services.brackets.FoundBrackets.Add  doneBrackets 
            services.semantic.FoundSemantics.Add doneSemantics
            services.errors.FoundErrors.Add      doneErrors
            services.selection.FoundSels.Add     doneSels
        
        member _.Reset() = // will be called in Editor.SetUp.Document.Changing.Add(fun _ -> ....)
            scan <- ScanState.None 

module DocChangeMark =     
    open Redrawing
    
    /// milliseconds to wait before starting the first check after a change
    /// only for Single char changes
    let mainWait = 50 

    let updateAllTransformersConcurrently(iEd:IEditor, code:string, drawServ:DrawingServices, state:InteractionState, id) = 
            // first: Foldings, ColorBrackets when full text available async.
            async{
                state.CodeLines.UpdateLines(code, id)
                if state.IsLatest id then   
                    drawServ.selection.DocChangedResetTransformers(id)             
                    drawServ.brackets.UpdateAllBrackets(id)
                    drawServ.folds.UpdateFolds(id)
             } |> Async.Start 
        
            // second: Errors and Semantic Highlighting and BadIndentation on FCS check result .  
            async{ 
                match Checker.CheckCode(iEd, state, code, id, true) with // code checking does not need to wait for CodeLines.Update
                |None -> ()
                |Some res ->
                    if state.IsLatest id then 
                        drawServ.errors.UpdateErrs(res.errors, id)
                        drawServ.semantic.UpdateSemHiLiTransformers( res.checkRes, id)
            
            } |> Async.Start   
    
    //To be called from any thread
    let updateAllTransformersSync(iEd:IEditor, doc:TextDocument, drawServ:DrawingServices, state:InteractionState, id ) =        
        // NOTE just checking only Partial Code till caret with (doc.CreateSnapshot(0, tillOffset).Text) 
        // would make the GetDeclarationsList method miss some declarations !!
        let code = doc.CreateSnapshot().Text // the only threadsafe way to access the code string
        if state.IsLatest id then 
            updateAllTransformersConcurrently (iEd, code, drawServ, state, id)
      
    // To be called from UI thread
    let updateAllTransformersAsync (iEd:IEditor, drawServ:DrawingServices, state:InteractionState, id ) =
        let doc = iEd.AvaEdit.Document // get Doc in Sync
        async { 
            if mainWait <> 0 then do! Async.Sleep mainWait
            if state.IsLatest id then 
                // NOTE just checking only Partial Code till caret with (doc.CreateSnapshot(0, tillOffset).Text) 
                // would make the GetDeclarationsList method miss some declarations !!
                let code = doc.CreateSnapshot().Text // the only threadsafe way to access the code string
                if state.IsLatest id then 
                    updateAllTransformersConcurrently (iEd, code, drawServ, state, id)
        } |> Async.Start
    
               
module DocChangeCompletion = 
    open DocChangeUtil
    open Redrawing

    type ShowAutocomplete = DoNothing | JustMark| ShowOnlyDU | ShowAll | ShowKeyWords 

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
            | _  -> true 
    
        /// Is a discriminated union that wants autocomplete
        let private isDUorKeyword checkForKeyW fromIdx ln =            
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
                        | "pri" ->  ShowKeyWords // private
                        | "mut" ->  ShowKeyWords // mutable
                        | "int" ->  ShowKeyWords // internal
                        | "inl" ->  ShowKeyWords // inline
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
                elif isKeyword memIdx ln then ShowKeyWords
                else JustMark 

    
        let bind (f:bool->string->ShowAutocomplete) inStr ln (prev:ShowAutocomplete) = 
            match prev with
            |ShowAll      -> f inStr ln
            |ShowOnlyDU   -> ShowOnlyDU
            |JustMark     -> JustMark
            |DoNothing    -> DoNothing
            |ShowKeyWords -> ShowKeyWords
    
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
                        // TODO! because of this changing a text in a comment is not immediately picked up by selection highlighter (if present). only after another change in the code.
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

    let getShowRestriction s = 
        match s with 
        |DoNothing   // never happens       
        |JustMark    // never happens     
        |ShowAll     -> JustAll
        |ShowKeyWords-> JustKeyWords
        |ShowOnlyDU  -> JustDU

    /// for single character edits
    let singleCharChange (iEd:IEditor, drawServ:DrawingServices, state:InteractionState, id:int64)  =        
        let pos = getPosInCode2(iEd.AvaEdit)
        //ISeffLog.log.PrintfnDebugMsg $"singleCharChange:\r\n{pos}"
        let tx = pos.lineToCaret        
        if tx.Length = 0 then // empty line after deleting
            DocChangeMark.updateAllTransformersAsync (iEd, drawServ, state, id)    
        else
            let lastChar = tx[tx.Length-1]
            if lastChar <> '.' && state.JustCompleted then 
                // if it is not a dot avoid re-trigger of completion window on single character completions, just check
                state.JustCompleted <- false // reset it
                DocChangeMark.updateAllTransformersAsync (iEd, drawServ, state, id)
            
            else 
                let doc = iEd.AvaEdit.Document // get in sync
                async{                
                    if DocChangeMark.mainWait <> 0 then 
                        do! Async.Sleep DocChangeMark.mainWait                    
                    if state.IsLatest id then 
                        if showOnLastChar (lastChar,pos) then
                            let show = MaybeShow.completionWindow(pos)
                            //ISeffLog.log.PrintfnDebugMsg $"MaybeShow.completionWindow for {lastChar} is {show}"
                            match show with 
                            |DoNothing  -> 
                                do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context  
                                drawServ.selection.UpdateToCurrentSelection() // TODO Or Just Do nothing ??

                            |JustMark -> 
                                DocChangeMark.updateAllTransformersSync(iEd, doc, drawServ, state, id)                        

                            |ShowKeyWords  |ShowAll |ShowOnlyDU -> 
                                state.DocChangedConsequence <- WaitForCompletions
                                let mutable fullCode = ""
                                let declsPosX  = 
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
                            
                                match declsPosX with         
                                |Some (decls, posX) ->
                                    // Switch to Sync and try showing completion window:
                                    do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context                            
                                    let showRestrictions = getShowRestriction show                                    
                                    let checkAndMark() = DocChangeMark.updateAllTransformersAsync (iEd, drawServ, state, state.DocChangedId.Value) // will be called if window closes without an insertion
                                    match drawServ.compls.TryShow(decls, posX, showRestrictions, checkAndMark ) with 
                                    |DidShow ->                                     
                                        () // no need to do anything, DocChangedConsequence will be updated to 'React' when completion window closes
                                    |NoShow -> 
                                        state.DocChangedConsequence <- React
                                        do! Async.SwitchToThreadPool()
                                        DocChangeMark.updateAllTransformersConcurrently(iEd, fullCode, drawServ, state, id)
                                |None -> 
                                    state.DocChangedConsequence <- React
                                    do! Async.SwitchToThreadPool()
                                    if fullCode="" then 
                                        fullCode <- doc.CreateSnapshot().Text
                                    if state.IsLatest id then 
                                        DocChangeMark.updateAllTransformersConcurrently (iEd, fullCode, drawServ, state, id)                            
                        else   
                            // the typed character should not trigger completion.                 
                            // DocChangedConsequence is still  'React', no need to reset.
                            DocChangeMark.updateAllTransformersSync(iEd, doc, drawServ, state, id)
            
                } |> Async.Start

module DocChangeEvents = 
    open DocChangeUtil   

    // gets called before the document actually changes
    let changing  (state:InteractionState, a:DocumentChangeEventArgs) =             
        
        // (1) increment change counter
        match state.DocChangedConsequence with 
        | WaitForCompletions -> ()
        | React -> state.Increment() |> ignore // incrementing this handler before the change actually happens, but  only increment when a reaction is required.
        
        // (2) Adjust Shifts
        let shift = getShift(state.Editor.Document, a)
        state.FastColorizer.AdjustShifts shift
        state.ErrSegments.AdjustOneShift shift

        // (3) ed.EventCombiner.Reset() //is called in Editor.SetUp.Document.Changing.Add(fun _ -> ....) because it is not accessible from here

    let changed (iEd:IEditor) (drawServ:Redrawing.DrawingServices) (state:InteractionState) (eventArgs:DocumentChangeEventArgs) : unit  =  
        match state.DocChangedConsequence with 
        | WaitForCompletions -> 
            // Do not increment DoChangeID counter, this would cancel the showing of the completion window.
            // no type checking ! just keep on tying, 
            // the typed characters wil become a prefilter for the  in completion window
            ()
        | React -> 
            let id = state.DocChangedId.Value // the increment was done before this event in Doc.Changing (not Doc.Changed) 
                       
            if isASingleCharChange eventArgs then 
                DocChangeCompletion.singleCharChange     (iEd, drawServ, state, id)
            else
                DocChangeMark.updateAllTransformersAsync (iEd, drawServ, state, id)            

            
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