namespace Seff

open System
open System.Windows
open System.Windows.Controls
open System.Linq
open Seff.StringUtil
open Seff.Util
open Seff.UtilWPF
open Seff.FsService
open Seff.EditorUtil
open Seff.Fsi
open ICSharpCode.AvalonEdit

 
module EventHandlers =
    
    let setUpForWindow(win:Window) =  
        WindowLayout.init(win)

        UI.tabControl.SelectionChanged.Add( fun _-> 
            let ob = UI.tabControl.SelectedItem 
            if isNull ob then Tab.current <- None //  happens when closing the last open tab
            else
                let tab = ob :?> FsxTab
                Tab.current <- Some tab
                textChanged (TextChange.TabChanged , tab)  // not needed?
                Config.saveOpenFilesAndCurrentTab(tab.FileInfo , Tab.allTabs |> Seq.map(fun t -> t.FileInfo))
            )

        UI.splitterHor.DragCompleted.Add      (fun _ -> WindowLayout.splitChangeHor UI.editorRowHeight    UI.logRowHeight) 
        UI.splitterVert.DragCompleted.Add     (fun _ -> WindowLayout.splitChangeVert UI.editorColumnWidth UI.logColumnWidth) 

        //--------------------------------------
        // -  window location and size ---
        //--------------------------------------
        
        win.LocationChanged.Add(fun e -> // occures for every pixel moved
            if win.WindowState = WindowState.Normal &&  not WindowLayout.isMinOrMax then 
                if win.Top > -500. && win.Left > -500. then // to not save on minimizing on minimized: Top=-32000 Left=-32000 
                    Config.setFloatDelayed "WindowTop"  win.Top  89 // get float in statchange maximised neddes top access this before 350 ms pass
                    Config.setFloatDelayed "WindowLeft" win.Left 95
                    Config.save Log.dlog
                    //Log.dlog (sprintf "%s Location Changed: Top=%.0f Left=%.0f State=%A" Time.nowStrMilli win.Top win.Left win.WindowState) 
            )

        win.StateChanged.Add (fun e ->
            match win.WindowState with 
            | WindowState.Normal -> // because when Window is hosted in other App the restore from maximised does not remember the previous position automatically                
                win.Top <-     Config.getFloat "WindowTop"    0.0
                win.Left <-    Config.getFloat "WindowLeft"   0.0 
                win.Height <-  Config.getFloat "WindowHeight" 800.0
                win.Width <-   Config.getFloat "WindowWidth"  800.0
                Config.setBool  "WindowIsMax" false
                WindowLayout.isMinOrMax <- false
                Config.save Log.dlog
                //Log.dlog (sprintf "%s State changed=%A Top=%.0f Left=%.0f Width=%.0f Height=%.0f" Time.nowStrMilli win.WindowState win.Top win.Left win.ActualWidth win.ActualHeight )

            | WindowState.Maximized ->
                // the state change event comes after the location change event but before size changed.
                // restore to previous values before Location change event (unfortunatly as normal state instead of maximised) that saves the maximised 
                // position with a delay , see Conig.setDelayed(v) ).
                WindowLayout.isMinOrMax <- true
                async{
                    do! Async.Sleep 800 //wait for UI.editorRowHeight.ActualHeight update
                    if UI.editorRowHeight.ActualHeight > 1. then // do not save on Maximise Log     
                        Config.setFloatDelayed "WindowTop"    (Config.getFloat "WindowTop"     11.0) 200 
                        Config.setFloatDelayed "WindowLeft"   (Config.getFloat "WindowLeft"    11.0) 210 
                        Config.setFloatDelayed "WindowHeight" (Config.getFloat "WindowHeight" 699.0) 220 // just to be save restore those too
                        Config.setFloatDelayed "WindowWidth"  (Config.getFloat "WindowWidth"  699.0) 230 // just to be save restore those too
                        Config.setBool  "WindowIsMax" true                    
                        Config.save Log.dlog
                        //Log.dlog (sprintf "%s State changed=%A Top=%.0f Left=%.0f Width=%.0f Height=%.0f" Time.nowStrMilli win.WindowState win.Top win.Left win.ActualWidth win.ActualHeight )
                        }
                        |> Async.StartImmediate

            |WindowState.Minimized -> 
                WindowLayout.isMinOrMax <- true
            |x -> 
                Log.dlog (sprintf "*** unknown WindowState State change=%A" x) 
                WindowLayout.isMinOrMax <- true
            )

        win.SizeChanged.Add (fun e ->
            if win.WindowState = WindowState.Normal &&  not WindowLayout.isMinOrMax then 
                Config.setFloatDelayed "WindowHeight" win.Height 89
                Config.setFloatDelayed "WindowWidth"  win.Width  95
                Config.save Log.dlog
                //Log.dlog (sprintf "%s Size Changed: Width=%.0f Height=%.0f State=%A" Time.nowStrMilli win.Width win.Height win.WindowState)
            )


    let setUpForTab (tab:FsxTab) =         
        let tArea = tab.Editor.TextArea
        let tView = tab.Editor.TextArea.TextView

        //----------------------------------
        //--FS Checker and Code completion--
        //----------------------------------
        
        tab.CompletionWindowClosed <- (fun () -> textChanged( TextChange.CompletionWinClosed , tab)) //trigger error check

        //tab.Editor.Document.TextChanged.Add (fun e -> ())

        tab.Editor.Document.Changed.Add(fun e -> 
            //Log.printf "*Document.Changed Event: deleted %d, inserted %d." e.RemovalLength e.InsertionLength
            ModifyUI.markTabUnSaved(tab)
            match tab.CompletionWin with
            | Some w ->  // just keep on tying in completion window, no type checking !
                if w.CompletionList.ListBox.HasItems then 
                    ()
                    //let currentText = getField(typeof<CodeCompletion.CompletionList>,w.CompletionList,"currentText") :?> string //this property schould be public !
                    //TODO close Window if w.CompletionList.SelectedItem.Text = currentText
                    //TODO ther is a bug in current text when deliting chars
                    //Log.printf "currentText: '%s'" currentText
                    //Log.printf "w.CompletionList.CompletionData.Count:%d" w.CompletionList.ListBox.VisibleItemCount
                else 
                    w.Close() 
            
            | None -> //no completion window open , do type check..
                match e.InsertedText.Text with 
                |"."  ->                                textChanged( TextChange.EnteredDot          , tab)//complete
                | txt when txt.Length = 1 ->
                    if Char.IsLetter(txt.[0])  then     textChanged( TextChange.EnteredOneLetter    , tab)//complete
                    else                                textChanged( TextChange.EnteredOneNonLetter , tab)//check
                | _  ->                                 textChanged( TextChange.OtherChange         , tab)//check errors            
                )

        //this is not needed  for insertion, insertion with Tab or Enter. is built in !!
        tArea.TextEntering.Add (fun ev ->  //http://avalonedit.net/documentation/html/47c58b63-f30c-4290-a2f2-881d21227446.htm           
            let mutable stack = ""
            match tab.CompletionWin with 
            | Some w ->                
                match ev.Text with 
                |" " -> w.Close()
                |"." -> w.CompletionList.RequestInsertion(ev) // insert on dot too? // not nededed: textChanged( TextChange.EnteredDot , tab)
                | _  -> ()
            |None -> ()
            )
   

        //Tooltips for types
        tView.MouseHover.Add        Tooltips.TextEditorMouseHover
        tView.MouseHoverStopped.Add Tooltips.TextEditorMouseHoverStopped

        //-----------------------------
        //--------Error UI-------------
        //-----------------------------          
            
        tView.BackgroundRenderers.Add(tab.TextMarkerService)
        tView.LineTransformers.Add(   tab.TextMarkerService)
        tView.Services.AddService(typeof<ErrorUI.TextMarkerService> , tab.TextMarkerService) // what for?
        tView.MouseHover.Add (fun e ->
            let pos = tView.GetPositionFloor(e.GetPosition(tView) + tView.ScrollOffset)
            if pos.HasValue then
                let logicalPosition = pos.Value.Location
                let offset = tab.Editor.Document.GetOffset(logicalPosition)
                let markersAtOffset = tab.TextMarkerService.GetMarkersAtOffset(offset)
                let markerWithMsg = markersAtOffset.FirstOrDefault(fun marker -> marker.Msg <> null)//LINq ??
                if notNull markerWithMsg && notNull tab.ErrorToolTip then
                    let tb = new TextBlock()
                    tb.Text <- markerWithMsg.Msg        //TODO move styling out of event 
                    tb.FontSize <- Appearance.fontSize
                    tb.FontFamily <- Appearance.defaultFont
                    tb.TextWrapping <- TextWrapping.Wrap
                    tb.Foreground <- Media.SolidColorBrush(Media.Colors.DarkRed)                    
                    tab.ErrorToolTip.Content <- tb
                    
                    let pos = tab.Editor.Document.GetLocation(markerWithMsg.StartOffset)                    
                    let pt = tab.Editor.TextArea.TextView.GetVisualPosition(TextViewPosition(pos), Rendering.VisualYPosition.LineTop) //https://www.dazhuanlan.com/2019/07/03/wpf-position-a-tooltip-avalonedit%E6%B8%B8%E6%A0%87%E5%A4%84%E6%98%BE%E7%A4%BA/                    Posi
                    tab.ErrorToolTip.PlacementTarget <- tab.Editor.TextArea
                    tab.ErrorToolTip.PlacementRectangle <- new Rect(pt.X, pt.Y, 0., 0.)                    
                    tab.ErrorToolTip.Placement <- Primitives.PlacementMode.Top //https://docs.microsoft.com/en-us/dotnet/framework/wpf/controls/popup-placement-behavior      
                    tab.ErrorToolTip.VerticalOffset <- -6.0                   
                    
                    tab.ErrorToolTip.IsOpen <- true
                    //e.Handled <- true
                    //ErrorToolTipService.SetInitialShowDelay(tab.ErrorToolTip,50) // TODO does not work
                    //ErrorToolTipService.SetInitialShowDelay(this,50)// TODO does not work                    
                    //ErrorToolTipService.SetInitialShowDelay(tab.ErrorToolTip.Parent,50)// is null
                    )
        tView.MouseHoverStopped.Add ( fun e ->  if notNull tab.ErrorToolTip then (tab.ErrorToolTip.IsOpen <- false ; e.Handled <- true) )
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
                //Log.printf "--Substring length %d: '%s'" prevC.Length prevC
                if prevC.Length > 0 then 
                    if isJustSpaceChars prevC  then
                        let dist = prevC.Length % tab.Editor.Options.IndentationSize
                        let clearCount = if dist = 0 then tab.Editor.Options.IndentationSize else dist
                        //Log.printf "--Clear length: %d " clearCount
                        tab.Editor.Document.Remove(tab.Editor.CaretOffset - clearCount, clearCount)
                        e.Handled <- true
            )
