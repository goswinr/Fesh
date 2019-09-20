namespace Seff

open System.Windows.Input
open System.Threading
open System.Collections.Generic
open FSharp.Compiler.SourceCodeServices
open ICSharpCode.AvalonEdit.CodeCompletion
open ICSharpCode.AvalonEdit.Document
open Seff.Util
open Seff.StringUtil
open Seff.CompletionUI
open Seff.FsChecker

module EditorUtil=

    let currentLine(tab: FsxTab)=
        let doc = tab.Editor.Document
        doc.GetText(doc.GetLineByOffset(tab.Editor.CaretOffset))
    
    let currentLineBeforeCaret(tab: FsxTab)=
        let doc = tab.Editor.Document
        let car = tab.Editor.TextArea.Caret
        let caretOffset = car.Offset
        let ln = doc.GetLineByOffset(caretOffset)
        let caretOffsetInThisLine = caretOffset - ln.Offset
        
        { lineToCaret = doc.GetText(ln.Offset, caretOffsetInThisLine) 
          row =    car.Line  
          column = caretOffsetInThisLine // equal to amount of characters in lineToCaret
          offset = caretOffset }


module FsService = 

    type TextChange =  EnteredDot | EnteredOneLetter | EnteredOneNonLetter | CompletionWinClosed | TabChanged | OtherChange //| EnteredQuote
    type CharBeforeQuery = Dot | NotDot
    
    let keywordsComletionLines = [| for kw,desc in Keywords.KeywordsWithDescription  do yield CompletionLineKeyWord(kw,desc) :> ICompletionData|]
    let Keywords = Keywords.KeywordsWithDescription |> List.map fst |> HashSet

    // to be able to cancel all running FSC checker threads when text changed (there should only be one)
    let CancellationSources = new System.Collections.Concurrent.ConcurrentStack<CancellationTokenSource>()
    
    let showChecking (tab:FsxTab) = 
        async {
            let checkeri = tab.FsCheckerRunning // to check if after 200 ms still the same checker is running
            do! Async.Sleep 200
            if checkeri = tab.FsCheckerRunning then tab.Editor.Background <- Appearance.editorBackgroundChecking
            } |> Async.StartImmediate   


    let highlightErrors(tab:FsxTab) = 
        tab.FsCheckerRunning <- Rand.Next()                    
        showChecking tab 
        let findErrors = 
            async{  
                let! ok, parseRes, checkRes =  FsChecker.check (tab, 0 )
                if ok && Tab.isCurr tab then
                    tab.FsCheckerResult <- Some checkRes // cache for type info
                    tab.TextMarkerService.Clear()
                    match checkRes.Errors with 
                    | [||] -> 
                        tab.Editor.Background <- Appearance.editorBackgroundOk    
                    | es   -> 
                        tab.Editor.Background <- Appearance.editorBackgroundErr
                        for e in es |> Seq.truncate 4 do // TODO Only highligth the first 3 Errors, Otherwise UI becomes unresponsive at 100 erroes ( eg when pasting text)
                            let startOffset = tab.Editor.Document.GetOffset(new TextLocation(e.StartLineAlternate, e.StartColumn + 1 ))
                            let endOffset   = tab.Editor.Document.GetOffset(new TextLocation(e.EndLineAlternate,   e.EndColumn   + 1 ))
                            let length = endOffset-startOffset
                            tab.TextMarkerService.Create(startOffset, length, e.Message+", Error:"+ (string e.ErrorNumber))
                            Packages.checkForMissingPackage tab e startOffset length

                tab.FsCheckerRunning <- 0
                } 
        let cancelScr = new CancellationTokenSource()
        CancellationSources.Push cancelScr 
        Async.StartImmediate(findErrors, cancelScr.Token)

    let prepareAndShowComplWin(tab:FsxTab, pos:FsChecker.PositionInCode , changetype, setback, query, charBefore, onlyDU) = 
        if changetype = EnteredOneLetter  &&  Keywords.Contains query  then //this never happens since typing complete word happens in when window is open not closed
            highlightErrors(tab)
            () // do not complete, if keyword was typed full, just continue typing, completion will triger anyway on additional chars
        else
            let prevCursor = tab.Editor.Cursor
            tab.Editor.Cursor <- Cursors.Wait

            let aComp = 
                async{
                    let! ok, parseRes, checkRes =  FsChecker.check (tab, pos.offset)
                    if ok && Tab.isCurr tab then                        
                        //Log.printf "*2-prepareAndShowComplWin geting completions"
                        let ifDotSetback = if charBefore = Dot then setback else 0
                        let! decls = FsChecker.complete (parseRes , checkRes , pos , ifDotSetback)
                                                          
                        let completionLines = ResizeArray<ICompletionData>()                                
                        if not onlyDU && charBefore = NotDot then // add keywords to list
                            completionLines.AddRange keywordsComletionLines 
                        for it in decls.Items do 
                            match it.Glyph with 
                            |FSharpGlyph.Union|FSharpGlyph.Module | FSharpGlyph.EnumMember -> completionLines.Add (new CompletionLine(it)) 
                            | _ -> if not onlyDU then                                         completionLines.Add (new CompletionLine(it))
                                
                        tab.Editor.Cursor <- prevCursor
                        if Tab.isCurr tab then
                            //Log.printf "*prepareAndShowComplWin for '%s' with offset %d" pos.lineToCaret setback
                            if completionLines.Count > 0 then showCompletionWindow(tab, completionLines, setback,query)
                            else highlightErrors(tab)
                                      
                } 
            let cancelScr = new CancellationTokenSource()
            CancellationSources.Push cancelScr 
            Async.StartImmediate(aComp, cancelScr.Token)   
    
    let textChanged (change:TextChange ,tab:FsxTab) =
        //Log.printf "*1-textChanged because of %A" change 
        match tab.CompletionWin with
        | Some w ->  
            if w.CompletionList.ListBox.HasItems then 
                //TODO check text is full mtch and close completion window ?
                () // just keep on tying in completion window, no type checking !
            else 
                w.Close() 

        | None -> //no completion window open , do type check..

            // first cancel all previous checker threads 
            let mutable toCancel:CancellationTokenSource = null
            while CancellationSources.TryPop(&toCancel) do toCancel.Cancel() 
            
            match change with 
            | CompletionWinClosed | TabChanged | OtherChange | EnteredOneNonLetter -> highlightErrors(tab) //TODO maybe do less call to rerreor highlighter wen typing in string or comment ?
            | EnteredOneLetter | EnteredDot -> 

                let pos = EditorUtil.currentLineBeforeCaret tab // this line will include the charcater that trigger auto completion(dot or first letter)
                let line = pos.lineToCaret
                
                // possible cases where autocompletion is not desired:
                //let isNotInString           = (countChar '"' line ) - (countSubString "\\\"" line) |> isEven && not <| line.Contains "print" // "\\\"" to ignore escaped quotes of form \" ; check if formating string
                let isNotAlreadyInComment   = countSubString "//"  line = 0  ||  lastCharIs '/' line   // to make sure comment was not just typed(then still check)
                let isNotLetDecl            = let lk = (countSubString "let " line) + (countSubString "let(" line) in lk <= (countSubString "=" line) || lk <= (countSubString ":" line)
                let isNotFunDecl            = let fk = (countSubString "fun " line) + (countSubString "fun(" line) in fk <= (countSubString "->" line)|| fk <= (countSubString ":" line)
                let doCompletionInPattern, onlyDU   =  
                    match stringAfterLast " |" (" "+line) with // add starting step to not fail at start of line with "|"
                    |None    -> true,false 
                    |Some "" -> Log.print " this schould never happen since we get here only with letters, but not typing '|'" ; false,false // most comen case: '|" was just typed, next pattern declaration starts after next car
                    |Some s  -> 
                        let doCompl = 
                            s.Contains "->"             || // name binding already happend 
                            isOperator s.[0]            || // not in pattern matching 
                            s.[0]=']'                   || // not in pattern matching 
                            (s.Contains " :?" && not <| s.Contains " as ")  // auto complete desired  after '| :?" type check but not after 'as' 
                        if not doCompl && startsWithUppercaseAfterWhitespace s then // do autocomplete on DU types when starting with uppercase Letter
                           if s.Contains "(" || s.Contains " " then   false,false //no completion binding a new name inside a DU
                           else                                       true ,true //upper case only, show DU and Enum in completion list, if all others are false
                        else
                           doCompl,false //not upper case, other 3 decide if anything is shown

                //Log.printf "isNotInString:%b; isNotAlreadyInComment:%b; isNotFunDeclaration:%b; isNotLetDeclaration:%b; doCompletionInPattern:%b, onlyDU:%b" isNotInString isNotAlreadyInComment isNotFunDecl isNotLetDecl doCompletionInPattern onlyDU
            
                if (*isNotInString &&*) isNotAlreadyInComment && isNotFunDecl && isNotLetDecl && doCompletionInPattern then
                    let setback     = lastNonFSharpNameCharPosition line                
                    let query       = line.Substring(line.Length - setback)
                    let isKeyword   = Keywords.Contains query
                    //Log.printf "pos:%A setback='%d'" pos setback
                
                    let charBeforeQuery = 
                        let i = pos.column - setback - 1
                        if i >= 0 && i < line.Length then 
                            if line.[i] = '.' then Dot else NotDot
                        else
                            NotDot

                    //Log.printf "*2-textChanged now FsChecking with: query='%s', charBefore='%A', isKey=%b, setback='%d', line='%s' " query charBeforeQuery isKeyword setback line

                    if charBeforeQuery = NotDot && isKeyword then
                        highlightErrors(tab)
                    else 
                        prepareAndShowComplWin(tab, pos, change, setback, query, charBeforeQuery, onlyDU)
                else
                    //highlightErrors(tab)
                    //Log.printf "*2-textChanged didn't trigger of checker not needed? \r\n"
                    ()
    
