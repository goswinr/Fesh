namespace Seff.Editor

open Seff.Views
open Seff.Model
open System
open System.Windows
open System.Windows.Controls
open System.Linq
open Seff.Util.String
open Seff.Util.General
open ICSharpCode.AvalonEdit
open Seff.Editor.TextChanged
 
module EditorEvents =
    
    let private currentLine(ed:Editor)=
        let doc = ed.AvaEdit.Document
        doc.GetText(doc.GetLineByOffset(ed.AvaEdit.CaretOffset))


    let setUpForTab (tab:Tab) =         
        let avaEdit = tab.Editor.AvaEdit
        
        //----------------------------------
        //--FS Checker and Code completion--
        //----------------------------------
        
        //tab.CompletionWindowClosed <- (fun () -> textChanged( TextChange.CompletionWinClosed , tab)) //trigger error check if windo closed without insertion

        //ed.AvaEdit.Document.TextChanged.Add (fun e -> ()) //does not give document changed event args

        avaEdit.Document.Changed.Add(fun e -> 
            //log.PrintDebugMsg "*Document.Changed Event: deleted %d '%s', inserted %d '%s' completion Window:%A" e.RemovalLength e.RemovedText.Text e.InsertionLength e.InsertedText.Text tab.CompletionWin
            tab.IsCodeSaved <- false

            if tab.Editor.ComletionWin.IsVisible then   // just keep on tying in completion window, no type checking !
                if tab.Editor.ComletionWin.HasItems then 
                    ()
                    //let currentText = getField(typeof<CodeCompletion.CompletionList>,w.CompletionList,"currentText") :?> string //this property schould be public !
                    //TODO close Window if w.CompletionList.SelectedItem.Text = currentText
                    //TODO ther is a bug in current text when deliting chars
                    //log.PrintDebugMsg "currentText: '%s'" currentText
                    //log.PrintDebugMsg "w.CompletionList.CompletionData.Count:%d" w.CompletionList.ListBox.VisibleItemCount
                else 
                    tab.Editor.ComletionWin.Close() 
            
            else //no completion window open , do type check..
                match e.InsertedText.Text with 
                |"."  ->                                             textChanged (TextChange.EnteredDot              , tab.Editor)//complete
                | txt when txt.Length = 1 ->                                     
                    if tab.Editor.ComletionWin.JustClosed then       textChanged (TextChange.CompletionWinClosed     , tab.Editor)//check to avoid retrigger of window on single char completions
                    else                                                         
                        let c = txt.[0]                                          
                        if Char.IsLetter(c) || c='_' || c='`' then   textChanged (TextChange.EnteredOneIdentifierChar        , tab.Editor)//complete
                        else                                         textChanged (TextChange.EnteredOneNonIdentifierChar     , tab.Editor)//check
                                                                                 
                | _  ->                                              textChanged (TextChange.OtherChange                    , tab.Editor)//several charcters(paste) ,delete or completion window          
                
                tab.Editor.ComletionWin.JustClosed<-false
                )

        //this is not needed  for insertion, insertion with Tab or Enter. is built in !!
        avaEdit.TextArea.TextEntering.Add (fun ev ->  //http://avalonedit.net/documentation/html/47c58b63-f30c-4290-a2f2-881d21227446.htm          
            if tab.Editor.ComletionWin.IsVisible then 
                match ev.Text with 
                |" " -> tab.Editor.ComletionWin.Close()
                |"." -> tab.Editor.ComletionWin.Window.CompletionList.RequestInsertion(ev) // insert on dot too? // not nededed: textChanged( TextChange.EnteredDot , tab)
                | _  -> () // other triggers https://github.com/icsharpcode/AvalonEdit/blob/28b887f78c821c7fede1d4fc461bde64f5f21bd1/ICSharpCode.AvalonEdit/CodeCompletion/CompletionList.cs#L171            
            )
   

        //------------------------------
        //--------Backspacing-----------
        //------------------------------  
        //remove 4 charactes (Options.IndentationSize) on pressing backspace key insted of one 
        avaEdit.PreviewKeyDown.Add ( fun e -> // http://community.sharpdevelop.net/forums/t/10746.aspx
            if e.Key = Input.Key.Back then 
                let line:string = currentLine tab.Editor
                let car = avaEdit.TextArea.Caret.Column
                let prevC = line.Substring(0 ,car-1)
                //log.PrintDebugMsg "--Substring length %d: '%s'" prevC.Length prevC
                if prevC.Length > 0 then 
                    if isJustSpaceCharsOrEmpty prevC  then
                        let dist = prevC.Length % avaEdit.Options.IndentationSize
                        let clearCount = if dist = 0 then avaEdit.Options.IndentationSize else dist
                        //log.PrintDebugMsg "--Clear length: %d " clearCount
                        avaEdit.Document.Remove(avaEdit.CaretOffset - clearCount, clearCount)
                        e.Handled <- true
            )
