namespace Seff.Editor

open Seff
open Seff.Model
open System.Windows.Input
open System.Threading
open System.Collections.Generic
open FSharp.Compiler.SourceCodeServices
open ICSharpCode.AvalonEdit.Folding
open ICSharpCode.AvalonEdit
open ICSharpCode.AvalonEdit.CodeCompletion
open ICSharpCode.AvalonEdit.Document
open Seff.Util.General
open Seff.Util.String
open Seff.Editor


module TextChanged=
    
    let private currentLineBeforeCaret(ed: Editor)=
        let doc = ed.AvaEdit.Document
        let car = ed.AvaEdit.TextArea.Caret
        let caretOffset = car.Offset
        let ln = doc.GetLineByOffset(caretOffset)
        let caretOffsetInThisLine = caretOffset - ln.Offset
        
        { lineToCaret = doc.GetText(ln.Offset, caretOffsetInThisLine) 
          row =    car.Line  
          column = caretOffsetInThisLine // equal to amount of characters in lineToCaret
          offset = caretOffset }

    let private keywords = Keywords.KeywordsWithDescription |> List.map fst |> HashSet // used in analysing text change

    let textChanged (change:TextChange ,ed:Editor) =
        let log = ed.Config.Log
        log.PrintDebugMsg "*1-textChanged because of %A" change 

        if ed.ComletionWin.IsVisible then 
            if ed.ComletionWin.Window.CompletionList.ListBox.HasItems then 
                //TODO check text is full mtch and close completion window ?
                // just keep on tying in completion window, no type checking !
                ()
            else 
                log.PrintDebugMsg "*1.1-textChanged: closing empty completion window(change: %A)" change 
                ed.ComletionWin.Window.Close() 


            match change with             
            | OtherChange | CompletionWinClosed | TabChanged  | EnteredOneNonIdentifierChar -> //TODO maybe do less call to error highlighter when typing in string or comment ?
                ed.Checker.CkeckAndHighlight(ed.AvaEdit, ed.FileInfo)
                //TODO trigger here UpdateFoldings(tab,None) or use event

            | EnteredOneIdentifierChar | EnteredDot -> 

                let pos = currentLineBeforeCaret ed // this line will include the charcater that trigger auto completion(dot or first letter)
                let line = pos.lineToCaret
                
                //possible cases where autocompletion is not desired:
                //let isNotInString           = (countChar '"' line ) - (countSubString "\\\"" line) |> isEven && not <| line.Contains "print" // "\\\"" to ignore escaped quotes of form \" ; check if formating string
                let isNotAlreadyInComment   = countSubString "//"  line = 0  ||  lastCharIs '/' line   // to make sure comment was not just typed(then still check)
                let isNotLetDecl            = let lk = (countSubString "let " line) + (countSubString "let(" line) in lk <= (countSubString "=" line) || lk <= (countSubString ":" line)
                // TODO add check for "for" declaration
                let isNotFunDecl            = let fk = (countSubString "fun " line) + (countSubString "fun(" line) in fk <= (countSubString "->" line)|| fk <= (countSubString ":" line)
                let doCompletionInPattern, onlyDU   =  
                    match stringAfterLast " |" (" "+line) with // add starting step to not fail at start of line with "|"
                    |None    -> true,false 
                    |Some "" -> log.PrintDebugMsg " this schould never happen since we get here only with letters, but not typing '|'" ; false,false // most comen case: '|" was just typed, next pattern declaration starts after next car
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

                //log.PrintDebugMsg "isNotInString:%b; isNotAlreadyInComment:%b; isNotFunDeclaration:%b; isNotLetDeclaration:%b; doCompletionInPattern:%b, onlyDU:%b" isNotInString isNotAlreadyInComment isNotFunDecl isNotLetDecl doCompletionInPattern onlyDU
            
                if (*isNotInString &&*) isNotAlreadyInComment && isNotFunDecl && isNotLetDecl && doCompletionInPattern then
                    let setback     = lastNonFSharpNameCharPosition line                
                    let query       = line.Substring(line.Length - setback)
                    let isKeyword   = keywords.Contains query
                    //log.PrintDebugMsg "pos:%A setback='%d'" pos setback
                
                                       
                    let charBeforeQueryDU = 
                        let i = pos.column - setback - 1
                        if i >= 0 && i < line.Length then 
                            if line.[i] = '.' then Dot else NotDot
                        else
                            NotDot

                    if charBeforeQueryDU = NotDot && isKeyword then
                        //log.PrintDebugMsg "*2.1-textChanged highlighting with: query='%s', charBefore='%A', isKey=%b, setback='%d', line='%s' " query charBeforeQueryDU isKeyword setback line
                        ed.Checker.CkeckAndHighlight(ed.AvaEdit,ed.FileInfo)

                    else 
                        //log.PrintDebugMsg "*2.2-textChanged Completion window opening with: query='%s', charBefore='%A', isKey=%b, setback='%d', line='%s' change=%A" query charBeforeQueryDU isKeyword setback line change
                       ed.ComletionWin.TryShow(ed.FileInfo, pos, change, setback, query, charBeforeQueryDU, onlyDU)
                else
                    //checkForErrorsAndUpdateFoldings(tab)
                    //log.PrintDebugMsg "*2.3-textChanged didn't trigger of checker not needed? \r\n"
                    ()
    
