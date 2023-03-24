namespace Seff.Editor

open System
open System.Windows
open System.Windows.Input

open FSharp.Compiler.Tokenization // for keywords

open AvalonEditB
open AvalonEditB.Document

open Seff.Model
open Seff.Util
open Seff.Util.Str


[<RequireQualifiedAccess>]
module DocChanged =
    
    type DoNext =  CheckCode | DoNothing

    /// for closing and inserting from completion window
    let closeAndMaybeInsertFromCompletionWindow (compls:Completions) (ev:TextCompositionEventArgs) = 
        if compls.IsOpen then
            match ev.Text with              //enter and tab is not needed  here for  insertion,  insertion with Tab or Enter is built into Avalonedit!!
            |" " -> compls.Close()
            |"." -> compls.RequestInsertion(ev) // insert on dot too? //TODO only when more than one char is typed in completion window??
            | _  -> () // other triggers https://github.com/icsharpcode/AvalonEdit/blob/28b887f78c821c7fede1d4fc461bde64f5f21bd1/AvalonEditB/CodeCompletion/CompletionList.cs#L171

          //|"(" -> compls.RequestInsertion(ev) // insert on open Bracket too?
       //else compls.JustClosed<-false


    module InternalDocChange =     
        type ShowAutocomplete = 
             | DontShow     
             | ShowOnlyDU   
             | ShowAll      
    
        let keywords = FSharpKeywords.KeywordsWithDescription |> List.map fst |> Collections.Generic.HashSet // used in analysing text change

        /// this line will include the character that trigger auto completion(dot or first letter)
        let currentLineBeforeCaret(avaEdit:TextEditor) : PositionInCode =         
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

        let lastIdx inString find txt = 
            if inString then NotInQuotes.lastIndexOfFromInside  find txt 
            else             NotInQuotes.lastIndexOfFromOutside find txt


        let inline containsFrom idx (find:string) (txt:String) =
            match txt.IndexOf(find,idx,StringComparison.Ordinal) with 
            | -1 -> false
            | _ -> true 
        
        /// like lastIndex but test if its the first char or preceded by a space 
        let lastIdxAtStartOrWithSpace inString find txt = 
            let i = 
                if inString then NotInQuotes.lastIndexOfFromInside  find txt 
                else             NotInQuotes.lastIndexOfFromOutside find txt
            if   i = -1 then -1
            elif i = 0 || txt.[i-1] = ' ' then i // test if its the first char or preceded by a space 
            else -1

        let isCaretInComment ln =  
             NotInQuotes.contains "//" ln
    
        // is a discriminated union that wants autocomplete
        let inline isDU fromIdx ln =
            let fi = indexOfFirstNonWhiteAfter fromIdx ln
            if fi < fromIdx then ShowAll // fromIdx-1 returned, non white letter was not found
            else
                let first = ln.[fi]
                if 'A' <= first && first <= 'Z' then // starts with a capital letter , TODO or use Char.isUpper for full Unicode spectrum ?
                    match ln.IndexOf(' ',fi) with // and has no space 
                    | -1 -> 
                        match ln.IndexOf('(',fi) with // and has no open bracket
                        | -1 -> ShowOnlyDU 
                        | _ -> DontShow // writing a lowercase name binding as part of the uppercase DU's value
                    | _ -> DontShow
                elif 'a' <= first && first <= 'z' then
                    DontShow// writing a lowercase name binding             
                else
                    ShowAll // writing nut a DU but an operator like |> or |]
    
        //--------------------------------------------------------------------------------------------
        //-----------check the four ways to bind a name: let, for, fun, match with | ---------------
        //-------------------------------------------------------------------------------------------

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
                elif containsFrom barIdx ":?"      ln then  (if containsFrom barIdx  " as " ln then DontShow   else ShowAll)
                else isDU (barIdx+1) ln         
                
        let isThisMemberDeclaration inStr (ln:string) = // to not autocomplete on 'member this' (before the dot)
            let barIdx = lastIdxAtStartOrWithSpace inStr "member" ln // test if its the first char or preceded by a space 
            if barIdx = -1 then  
                ShowAll
            else 
                if   containsFrom barIdx "."  ln then ShowAll                
                else DontShow 

     
        let show (pos:PositionInCode, compls:Completions, ed:IEditor, forDUonly, checker:Checker) : unit= 
            let lnToCaret = pos.lineToCaret
            let setback     = lastNonFSharpNameCharPosition lnToCaret // to maybe replace some previous characters too
            let query       = lnToCaret.Substring(lnToCaret.Length - setback)            
            //ISeffLog.log.PrintfnDebugMsg "2.1 show: pos:%A setback='%d'" pos setback

            let dotBefore = 
                let i = pos.column - setback - 1
                if i >= 0 && i < lnToCaret.Length then
                    if lnToCaret.[i] = '.' then 
                        Dot 
                    else 
                        NotDot
                else
                    NotDot

            if dotBefore = NotDot && keywords.Contains query then
                //ISeffLog.log.PrintfnDebugMsg "*2.2a-show: just highlighting with: lnToCaret='%s' \r\n query='%s', dotBefore='%A',  setback='%d', onlyDU:%b' " lnToCaret query dotBefore setback forDUonly
                checker.CheckThenHighlightAndFold(ed)
            else
                //ISeffLog.log.PrintfnDebugMsg "*2.2b-show: try window opening with: lnToCaret=\r\n  '%s'\r\n  query='%s', dotBefore='%A', setback='%d', onlyDU:%b" lnToCaret query dotBefore  setback forDUonly
                let last = lnToCaret.[lnToCaret.Length-1]
                Completions.TryShow(ed, compls, pos, last , setback, dotBefore, forDUonly)                


        let maybeShowCompletionWindow (compls:Completions,ed:IEditor, checker:Checker) : unit =            
            let pos = currentLineBeforeCaret(ed.AvaEdit) 
            let ln = pos.lineToCaret // this line will include the character that trigger auto completion(dot or first letter)
            let len = ln.Length
            //ISeffLog.log.PrintfnDebugMsg "*1.1 maybeShowCompletionWindow for lineToCaret: \r\n    '%s'" ln
            if len=0 then // line is empty
                () // DoNothing
            else
                let last = ln.[len-1]
                if isCaretInComment ln then 
                    if last <> '/' then checker.CheckThenHighlightAndFold(ed) // to make sure comment was not just typed (then still check)
                    else () // DoNothing 
                else
                    let inStr = not <| NotInQuotes.isLastCharOutsideQuotes ln                    
                    match isLetDeclaration inStr ln with 
                    |DontShow -> 
                        //ISeffLog.log.PrintfnDebugMsg "noShow because isLetDeclaration: %s" ln
                        checker.CheckThenHighlightAndFold(ed) // keep on writing the current new variable name for a binding , don't open any completion windows
                    |ShowOnlyDU -> show(pos,compls,ed,true, checker)
                    |ShowAll -> 
                        match isFunDeclaration inStr ln with 
                        |DontShow -> 
                            //ISeffLog.log.PrintfnDebugMsg "noShow because isFunDeclaration: %s" ln
                            checker.CheckThenHighlightAndFold(ed) 
                        |ShowOnlyDU -> show(pos,compls,ed,true, checker)
                        |ShowAll ->
                            match isForDeclaration inStr ln with 
                            |DontShow -> 
                                //ISeffLog.log.PrintfnDebugMsg "noShow because isForDeclaration: %s" ln
                                checker.CheckThenHighlightAndFold(ed) 
                            |ShowOnlyDU -> show(pos,compls,ed,true, checker)
                            |ShowAll ->                                
                                match isBarDeclaration inStr ln with 
                                |DontShow -> 
                                    //ISeffLog.log.PrintfnDebugMsg "noShow because isBarDeclaration: %s" ln
                                    checker.CheckThenHighlightAndFold(ed) 
                                |ShowOnlyDU -> show(pos,compls,ed,true, checker)
                                |ShowAll    ->                                 
                                    match isThisMemberDeclaration inStr ln with 
                                    |DontShow -> 
                                        //ISeffLog.log.PrintfnDebugMsg "noShow because isThisMemberDeclaration: %s" ln
                                        checker.CheckThenHighlightAndFold(ed) 
                                    |ShowOnlyDU -> show(pos,compls,ed,true, checker)
                                    |ShowAll    -> show(pos,compls,ed,false, checker) // this is the most common case

    open InternalDocChange
        

    let docChanged (e:DocumentChangeEventArgs,ed:IEditor, compls:Completions, checker:Checker) : unit = 
        ISeffLog.log.PrintfnDebugMsg "*1.1 Document.Changed Event: deleted: %d '%s', inserted %d '%s', completion hasItems: %b, isOpen: %b , Just closed: %b IsWaitingForTypeChecker %b" e.RemovalLength e.RemovedText.Text e.InsertionLength e.InsertedText.Text compls.HasItems compls.IsOpen compls.JustClosed Completions.IsWaitingForTypeChecker
                        
        if Completions.IsWaitingForTypeChecker then 
            () // just keep on tying in completion window, no type checking !

        elif compls.IsOpen then   
            // just keep on tying in completion window, no type checking !
            if compls.HasItems then 
                //let currentText = getField(typeof<CodeCompletion.CompletionList>,w.CompletionList,"currentText") :?> string // TODO this property should be public in avaloneditB !                
                //log.PrintfnDebugMsg "currentText: '%s'" currentText
                //log.PrintfnDebugMsg "w.CompletionList.CompletionData.Count:%d" w.CompletionList.ListBox.VisibleItemCount
                () // DoNothing
            else
                compls.Close()
                ()  // do nothing because if the doc changed a separate event will be triggered for that

        else // the completion window is NOT open or not about to be opend after type checking:
            
            if e.InsertionLength = 1 && e.RemovalLength = 0 then 
                let txt = e.InsertedText.Text
                let c = txt.[0]
                if c= '.' then // do even if compls.JustClosed
                    maybeShowCompletionWindow(compls,ed, checker) // EnteredDot 
                
                elif compls.JustClosed then   // check to avoid re-trigger of window on single char completions
                    compls.JustClosed <- false                    
                    checker.CheckThenHighlightAndFold(ed) // because CompletionWinClosed 
                
                else
                    if Char.IsLetter(c) 
                        || c='_' // for __SOURCE_DIRECTORY__
                        || c='`' 
                        || c='#'  then    // for #if directives
                            maybeShowCompletionWindow(compls,ed, checker) // because  EnteredOneIdentifierChar  
                    else 
                        checker.CheckThenHighlightAndFold(ed) // because EnteredOneIdentifierChar  
            
            // also show completion on deleting charcters ?
            elif e.InsertionLength = 0 && e.RemovalLength = 1 then 
                maybeShowCompletionWindow(compls,ed, checker) // because singleChar deletion 
                
            else
              checker.CheckThenHighlightAndFold(ed) // because OtherChange: several characters(paste) , delete or an insert from the completion window
             

    (* unused:

    // delay and buffer reaction to doc changes
    open System.Threading
    let private changeId = ref 0L

    /// only react to the last change after 100 ms
    let delayDocChange(e:DocumentChangeEventArgs, ed:IEditor, compls:Completions, checker:Checker) : unit =         
        /// do timing as low level as possible: see Async.Sleep in  https://github.com/dotnet/fsharp/blob/main/src/fsharp/FSharp.Core/async.fs#L1587
        let k = Interlocked.Increment changeId
        let mutable timer :option<Timer> = None
        let action =  TimerCallback(fun _ ->
            if !changeId= k then ed.AvaEdit.Dispatcher.Invoke(fun () ->  docChanged (e,ed, compls, checker))
            if timer.IsSome then timer.Value.Dispose() // dispose inside callback, like in Async.Sleep implementation
            )
        timer <- Some (new Threading.Timer(action, null, dueTime = 100 , period = -1))
    *)
        

