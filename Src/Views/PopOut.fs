namespace Seff.Views

open System
open System.Windows
open System.Windows.Controls

open Seff
open Seff.Util.General
open Seff.Model
open Seff.Editor

module PopOut = 
    open System.Windows.Controls
    let mutable private lastLocation : option<float*float> = 
        // TODO remember these postions in config
        None

    let icon = 
        try
            Windows.Media.Imaging.BitmapFrame.Create(Uri("pack://application:,,,/Seff;component/Media/logoGray.ico"))
        with ex ->
            ISeffLog.log.PrintfnAppErrorMsg  "Failed to load Media/logoGray.ico from Application.ResourceStream : %A" ex
            null

    //let internal copyUi(ui:UIElement) = 
    //    // fails to serialize TextBlockSelectable
    //    // https://stackoverflow.com/questions/32541/how-can-you-clone-a-wpf-object
    //    ui  |> Markup.XamlWriter.Save   |> fun s -> new IO.StringReader(s)  |> Xml.XmlReader.Create |> Markup.XamlReader.Load   :?> UIElement

    let showWindow(title, width, height, getUi:unit->option<#UIElement>, parent:Window) =         
        match getUi() with 
        |None -> ()
        |Some (content:#UIElement) -> 
            let scr = ScrollViewer(Content = content)
            scr.VerticalScrollBarVisibility   <- ScrollBarVisibility.Auto
            scr.HorizontalScrollBarVisibility <- ScrollBarVisibility.Disabled // to have word wrap
            scr.Padding <- Thickness(7.0)
            
            let w = Window(Title= title)
            w.Content <- scr                        
            w.Owner <- parent
            match lastLocation with
            |None ->
                w.WindowStartupLocation <- WindowStartupLocation.CenterOwner            
            |Some (left,top) ->            
                w.WindowStartupLocation <- WindowStartupLocation.Manual
                // to cascade windows:
                w.Left <- left + 40.
                w.Top  <- top  + 40.

            if notNull icon then w.Icon <- icon
            w.LocationChanged.Add(fun _ -> lastLocation <- Some(w.Left,w.Top)) 
            if width > 30. then 
                w.Show()
                w.Width <- width
                w.Height<- height 
            else                
                w.SizeToContent <- SizeToContent.WidthAndHeight  
                w.Show()
                if w.ActualHeight > parent.ActualHeight * 0.8  then w.Height  <-  parent.ActualHeight * 0.8
                if w.ActualWidth  > parent.ActualWidth  * 0.8  then w.Width   <-  parent.ActualWidth  * 0.8 
                w.Height  <-  max 10 <| parent.ActualHeight + 2. // to avoid border artifacts
                w.Width   <-  max 30 <| parent.ActualWidth  + 2. // to avoid border artifacts
            lastLocation <- Some(w.Left,w.Top)        

    let create(grid:TabsAndLog, statusBar:SeffStatusBar) = 
        let parent = grid.SeffWindow.Window        
        let ed = grid.Tabs.Current.Editor
        if statusBar.CheckerStatus.IsMouseOver || ed.DrawingServices.errors.ToolTip.IsOpen then
            match statusBar.CheckerStatus.ToolTip with
                | :? ToolTip as tt -> 
                    match tt.Content with 
                    | :? StackPanel as sp -> showWindow ("Seff PopOut| Compiler Error Info ", sp.ActualWidth, sp.ActualHeight, (fun () -> statusBar.CheckerStatus.GetErrorPanelCached ed) , parent)
                    | _ -> () // if ToolTip is just a string  don't pop out                
                | :? StackPanel as sp -> showWindow ("Seff PopOut| Compiler Error Info ", sp.ActualWidth, sp.ActualHeight, (fun () -> statusBar.CheckerStatus.GetErrorPanelCached ed), parent)
                | _ -> () // if ToolTip is just a string don't pop out

        if ed.TypeInfoTip.IsOpen then
            let ti = ed.TypeInfoTip
            let newSV = TypeInfo.getPanelCached ()
            newSV.HorizontalScrollBarVisibility <- ScrollBarVisibility.Disabled // to have word wrap  
            showWindow ("Seff PopOut | Type Info ", ti.ActualWidth, ti.ActualHeight ,(fun () -> Some newSV), parent)

        if ed.Completions.IsOpen  then  //&& Completions.HasStackPanelTypeInfo then
            let newSV = TypeInfo.getPanelCached ()
            newSV.HorizontalScrollBarVisibility <- ScrollBarVisibility.Disabled // to have word wrap           
            let wi,hi = 
                match ed.Completions.ComplWin with 
                | None -> 0.,0.
                | Some w -> 
                    match w.ToolTipContent with 
                    | :? ScrollViewer as sv -> sv.ActualWidth, sv.ActualHeight
                    | _ -> 0.,0.
                
            showWindow ("Seff PopOut | Autocomplete Type Info ", wi, hi,(fun () -> Some newSV ), parent)



