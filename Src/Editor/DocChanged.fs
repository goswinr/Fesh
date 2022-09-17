namespace Seff.Editor


open System
open System.Text
open System.Windows
open System.Windows.Media
open System.Windows.Input

open FSharp.Compiler.Tokenization // for keywords

open AvalonEditB
open AvalonEditB.Utils
open AvalonEditB.Document
open AvalonLog

open Seff
open Seff.Model
open Seff.Config
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
            |"(" -> compls.RequestInsertion(ev) // insert on open Bracket too?
            | _  -> () // other triggers https://github.com/icsharpcode/AvalonEdit/blob/28b887f78c821c7fede1d4fc461bde64f5f21bd1/AvalonEditB/CodeCompletion/CompletionList.cs#L171

            //else compls.JustClosed<-false


    module Internal =     
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
        
        /// like lastIndex but  test if its the first char or preceeded by a space 
        let lastIdxAtStartOrWithSpace inString find txt = 
            let i = 
                if inString then NotInQuotes.lastIndexOfFromInside  find txt 
                else             NotInQuotes.lastIndexOfFromOutside find txt
            if i= -1 then -1
            elif i = 0 || txt.[i-1] = ' ' then i // test if its the first char or preceeded by a space 
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
                let colonIdx = lastIdx inStr  ":" ln   
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
                if lastIdx inStr " in " ln > forIdx then ShowAll else isDU (forIdx+3) ln
        
        let isBarDeclaration inStr (ln:string) = // also covers the  'as' binding        
            let barIdx = lastIdxAtStartOrWithSpace inStr "|" ln // test if its the first char or preceded by a space 
            if barIdx = -1 then  
                ShowAll
            else 
                if   containsFrom barIdx "->"      ln then ShowAll                
                elif containsFrom barIdx " when "  ln then ShowAll
                elif containsFrom barIdx ":?"      ln then  (if containsFrom barIdx  " as " ln then DontShow   else ShowAll)
                else isDU (barIdx+1) ln                 
           
        //-------------------------------------------------------------------------------------------
        //-------------------------------------------------------------------------------------------
        
        let show (pos:PositionInCode, compls:Completions, ed:IEditor, forDUonly) = 
            let ln = pos.lineToCaret
            let setback     = lastNonFSharpNameCharPosition ln // to maybe replace some previous characters too
            let query       = ln.Substring(ln.Length - setback)
            let isKeyword   = keywords.Contains query
            //log.PrintfnDebugMsg "pos:%A setback='%d'" pos setback

            let charBeforeQueryDU = 
                let i = pos.column - setback - 1
                if i >= 0 && i < ln.Length then
                    if ln.[i] = '.' then Dot else NotDot
                else
                    NotDot

            if charBeforeQueryDU = NotDot && isKeyword then
                //log.PrintfnDebugMsg "*2.1-textChanged highlighting with: query='%s', charBefore='%A', isKey=%b, setback='%d', line='%s' " query charBeforeQueryDU isKeyword setback line
                CheckCode
            else
                //ISeffLog.log.PrintfnDebugMsg "*2.2-Completion window opening with: query='%s', charBefore='%A', isKey=%b, setback='%d', onlyDU:%b" query charBeforeQueryDU isKeyword setback forDUonly
                let last = ln.[ln.Length-1]
                Completions.TryShow(ed, compls, pos, last , setback, query, charBeforeQueryDU, forDUonly)
                DoNothing


        let maybeShowComletionWindow (compls:Completions,ed:IEditor) :DoNext =            
            let pos = currentLineBeforeCaret(ed.AvaEdit) 
            let ln = pos.lineToCaret // this line will include the character that trigger auto completion(dot or first letter)
            let len = ln.Length
            if len=0 then // line is empty
                DoNothing
            else
                let last = ln.[len-1]
                if isCaretInComment ln then 
                    if last <> '/' then CheckCode // to make sure comment was not just typed (then still check)
                    else DoNothing 
                else
                    let inStr = not <| NotInQuotes.isLastCharOutsideQuotes ln
                    match isLetDeclaration inStr ln with 
                    |DontShow -> CheckCode // keep on writing the current new varaiable name for a binding , dont open any completion windows
                    |ShowOnlyDU -> show(pos,compls,ed,true)
                    |ShowAll -> 
                        match isFunDeclaration inStr ln with 
                        |DontShow -> CheckCode 
                        |ShowOnlyDU -> show(pos,compls,ed,true)
                        |ShowAll ->
                            match isForDeclaration inStr ln with 
                            |DontShow -> CheckCode 
                            |ShowOnlyDU -> show(pos,compls,ed,true)
                            |ShowAll ->
                                match isBarDeclaration inStr ln with 
                                |DontShow -> CheckCode 
                                |ShowOnlyDU -> show(pos,compls,ed,true)
                                |ShowAll ->    show(pos,compls,ed,false) // most comon case

    open Internal

    let docChanged (e:DocumentChangeEventArgs,ed:IEditor, compls:Completions) : DoNext = 
        //log.PrintfnDebugMsg "*Document.Changed Event: deleted %d '%s', inserted %d '%s', completion hasItems: %b, isOpen: %b , Just closed: %b" e.RemovalLength e.RemovedText.Text e.InsertionLength e.InsertedText.Text ed.Completions.HasItems ed.Completions.IsOpen compls.JustClosed
                        
        if compls.IsOpen then   // just keep on tying in completion window, no type checking !
            if compls.HasItems then 
                //let currentText = getField(typeof<CodeCompletion.CompletionList>,w.CompletionList,"currentText") :?> string //this property should be public in avaloneditB !                
                //log.PrintfnDebugMsg "currentText: '%s'" currentText
                //log.PrintfnDebugMsg "w.CompletionList.CompletionData.Count:%d" w.CompletionList.ListBox.VisibleItemCount
                DoNothing
            else
                compls.Close()
                DoNothing // do nothing because if the doc changed a separate event will be triggered for that

        else //no completion window open , do type check..  
            match e.InsertedText.Text with
            |"."  ->  maybeShowComletionWindow(compls,ed) // EnteredDot  
            | txt when txt.Length = 1 ->
                if compls.JustClosed then   // check to avoid re-trigger of window on single char completions
                    compls.JustClosed<-false
                    CheckCode // CompletionWinClosed 
                else
                    let c = txt.[0]
                    if Char.IsLetter(c) 
                        || c='_' // for __SOURCE_DIRECTORY__
                        || c='`' 
                        || c='#'  then    // for #if directives
                            maybeShowComletionWindow(compls,ed) // EnteredOneIdentifierChar  
                    else 
                        CheckCode // EnteredOneNonIdentifierChar

            | _  -> CheckCode //OtherChange: several characters(paste) , delete or an insert from the completion window

           


