namespace Seff

open System
open System.Windows
open System.Windows.Controls
open System.Linq
open Seff.Util.String
open Seff.Util.General
open Seff.FsService
open Seff.EditorUtil
open ICSharpCode.AvalonEdit

 
module TabEvents =
    
    (* TODO 
        
        Fsi.IsReady.Add      (fun _ -> UI.log.Background <- Appearance.logBackgroundFsiReady) 
        Fsi.Started.Add      (fun _ -> UI.log.Background <- Appearance.logBackgroundFsiEvaluating) // happens at end of eval in sync mode

        *)


    let setUpForTab (tab:Tab) =         
        let tArea = tab.Editor.TextArea
        let tView = tab.Editor.TextArea.TextView

        //----------------------------------
        //--FS Checker and Code completion--
        //----------------------------------
        
        tab.CompletionWindowClosed <- (fun () -> textChanged( TextChange.CompletionWinClosed , tab)) //trigger error check if windo closed without insertion

        //tab.Editor.Document.TextChanged.Add (fun e -> ())

        tab.Editor.Document.Changed.Add(fun e -> //TODO or TextChanged ??
            //Log.Print "*Document.Changed Event: deleted %d '%s', inserted %d '%s' completion Window:%A" e.RemovalLength e.RemovedText.Text e.InsertionLength e.InsertedText.Text tab.CompletionWin
            tab.IsCodeSaved <- false
            match tab.CompletionWin with
            | Some w ->  // just keep on tying in completion window, no type checking !
                if w.CompletionList.ListBox.HasItems then 
                    ()
                    //let currentText = getField(typeof<CodeCompletion.CompletionList>,w.CompletionList,"currentText") :?> string //this property schould be public !
                    //TODO close Window if w.CompletionList.SelectedItem.Text = currentText
                    //TODO ther is a bug in current text when deliting chars
                    //Log.Print "currentText: '%s'" currentText
                    //Log.Print "w.CompletionList.CompletionData.Count:%d" w.CompletionList.ListBox.VisibleItemCount
                else 
                    w.Close() 
            
            | None -> //no completion window open , do type check..
                match e.InsertedText.Text with 
                |"."  ->                                             textChanged( TextChange.EnteredDot              , tab)//complete
                | txt when txt.Length = 1 ->                    
                    if tab.CompletionWindowJustClosed then           textChanged( TextChange.CompletionWinClosed     , tab)//check to avoid retrigger of window on single char completions
                    else
                        let c = txt.[0]
                        if Char.IsLetter(c) || c='_' || c='`' then   textChanged( TextChange.EnteredOneIdentifierChar        , tab)//complete
                        else                                         textChanged( TextChange.EnteredOneNonIdentifierChar     , tab)//check
               
                | _  ->                                              textChanged( TextChange.OtherChange             , tab)//several charcters(paste) ,delete or completion window          
                
                tab.CompletionWindowJustClosed<-false
                )

        //this is not needed  for insertion, insertion with Tab or Enter. is built in !!
        tArea.TextEntering.Add (fun ev ->  //http://avalonedit.net/documentation/html/47c58b63-f30c-4290-a2f2-881d21227446.htm          
            match tab.CompletionWin with 
            | Some w ->                
                match ev.Text with 
                |" " -> w.Close()
                |"." -> w.CompletionList.RequestInsertion(ev) // insert on dot too? // not nededed: textChanged( TextChange.EnteredDot , tab)
                | _  -> () // other triggers https://github.com/icsharpcode/AvalonEdit/blob/28b887f78c821c7fede1d4fc461bde64f5f21bd1/ICSharpCode.AvalonEdit/CodeCompletion/CompletionList.cs#L171
            |None -> ()
            )
   

        //Tooltips for types
        tView.MouseHover.Add        Tooltips.TextEditorMouseHover
        tView.MouseHoverStopped.Add Tooltips.TextEditorMouseHoverStopped

        //-----------------------------
        //--------Error UI-------------
        //-----------------------------          
            
        tView.BackgroundRenderers.Add(tab.ErrorMarker)
        tView.LineTransformers.Add(   tab.ErrorMarker)
        tView.Services.AddService(typeof<ErrorMarker> , tab.ErrorMarker) // what for?
        tView.MouseHover.Add (fun e ->
            let pos = tView.GetPositionFloor(e.GetPosition(tView) + tView.ScrollOffset)
            if pos.HasValue then
                let logicalPosition = pos.Value.Location
                let offset = tab.Editor.Document.GetOffset(logicalPosition)
                let markersAtOffset = tab.ErrorMarker.GetMarkersAtOffset(offset)
                let markerWithMsg = markersAtOffset.FirstOrDefault(fun marker -> marker.Msg <> null)//LINQ ??
                if notNull markerWithMsg && notNull tab.ErrorToolTip then
                    let tb = new TextBlock()
                    tb.Text <- markerWithMsg.Msg        //TODO move styling out of event handler ?
                    tb.FontSize <- Appearance.fontSize
                    tb.FontFamily <- Appearance.font
                    tb.TextWrapping <- TextWrapping.Wrap
                    tb.Foreground <- Media.SolidColorBrush(Media.Colors.DarkRed)                    
                    // TODO use popup instead of tooltip so it can be pinned?
                    tab.ErrorToolTip.Content <- tb
                    
                    let pos = tab.Editor.Document.GetLocation(markerWithMsg.StartOffset) 
                    let tvpos= TextViewPosition(pos.Line,pos.Column) 
                    let pt = tab.Editor.TextArea.TextView.GetVisualPosition(tvpos, Rendering.VisualYPosition.LineTop) //https://www.dazhuanlan.com/2019/07/03/wpf-position-a-tooltip-avalonedit%E6%B8%B8%E6%A0%87%E5%A4%84%E6%98%BE%E7%A4%BA/                    Posi
                    let ptInclScroll = pt - tab.Editor.TextArea.TextView.ScrollOffset
                    tab.ErrorToolTip.PlacementTarget <- tab.Editor.TextArea
                    tab.ErrorToolTip.PlacementRectangle <- new Rect(ptInclScroll.X, ptInclScroll.Y, 0., 0.)                    
                    tab.ErrorToolTip.Placement <- Primitives.PlacementMode.Top //https://docs.microsoft.com/en-us/dotnet/framework/wpf/controls/popup-placement-behavior      
                    tab.ErrorToolTip.VerticalOffset <- -6.0                   
                    
                    tab.ErrorToolTip.IsOpen <- true
                    //e.Handled <- true
                    //ErrorToolTipService.SetInitialShowDelay(tab.ErrorToolTip,50) // do in main window create instead
               
               )

        tView.MouseHoverStopped.Add ( fun e ->  if notNull tab.ErrorToolTip then  tab.ErrorToolTip.IsOpen <- false ) //; e.Handled <- true) )
        tView.VisualLinesChanged.Add( fun e ->  if notNull tab.ErrorToolTip then  tab.ErrorToolTip.IsOpen <- false )


        //------------------------------
        //--------Backspacing-----------
        //------------------------------  
        //remove 4 charactes (Options.IndentationSize) on pressing backspace key insted of one 
        tab.Editor.PreviewKeyDown.Add ( fun e -> // http://community.sharpdevelop.net/forums/t/10746.aspx
            if e.Key = Input.Key.Back then 
                let line:string = currentLine tab
                let car = tArea.Caret.Column
                let prevC = line.Substring(0 ,car-1)
                //Log.Print "--Substring length %d: '%s'" prevC.Length prevC
                if prevC.Length > 0 then 
                    if isJustSpaceCharsOrEmpty prevC  then
                        let dist = prevC.Length % tab.Editor.Options.IndentationSize
                        let clearCount = if dist = 0 then tab.Editor.Options.IndentationSize else dist
                        //Log.Print "--Clear length: %d " clearCount
                        tab.Editor.Document.Remove(tab.Editor.CaretOffset - clearCount, clearCount)
                        e.Handled <- true
            )
