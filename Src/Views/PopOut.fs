namespace Seff.Views

open System
open System.Windows
open System.Windows.Controls

open Seff
open Seff.Util.General
open Seff.Model
open Seff.Editor

module PopOut = 
    let mutable private lastLocation : option<float*float> = 
        // TODO remember these postions in config
        None

    let icon = 
        try
            Windows.Media.Imaging.BitmapFrame.Create(Uri("pack://application:,,,/Seff;component/Media/logoGray.ico"))
        with ex ->
            ISeffLog.log.PrintfnAppErrorMsg  "Failed to load Media/logoGray.ico from Application.ResourceStream : %A" ex
            null

    let internal copyUi(ui:UIElement) = 
        // fails to serialize TextBlockSelectable
        // https://stackoverflow.com/questions/32541/how-can-you-clone-a-wpf-object
        ui  |> Markup.XamlWriter.Save   |> fun s -> new IO.StringReader(s)  |> Xml.XmlReader.Create |> Markup.XamlReader.Load   :?> UIElement

    let showWindow(title, getUi:unit->option<#UIElement>, parent:Window) =         
        match getUi() with 
        |None -> ()
        |Some (content:#UIElement) ->        
            let w = Window(Title= title)
            let scr = ScrollViewer(Content = content)
            scr.VerticalScrollBarVisibility   <- ScrollBarVisibility.Auto
            scr.HorizontalScrollBarVisibility <- ScrollBarVisibility.Auto
            scr.Padding <- Thickness(7.0)
            w.Content <- scr
            w.SizeToContent <- SizeToContent.WidthAndHeight              
            w.Owner <- parent
            match lastLocation with
            |None ->
                w.WindowStartupLocation <- WindowStartupLocation.CenterOwner            
            |Some (left,top) ->            
                w.WindowStartupLocation <- WindowStartupLocation.Manual
                // cascade windows:
                w.Left <- left + 50.
                w.Top  <- top + 50.

            if notNull icon then w.Icon <- icon
            w.LocationChanged.Add(fun _ -> lastLocation <- Some(w.Left,w.Top))        
            w.Show()
            if w.ActualHeight > parent.ActualHeight * 0.8  then w.Height  <-  parent.ActualHeight * 0.8
            if w.ActualWidth  > parent.ActualWidth  * 0.8  then w.Width   <-  parent.ActualWidth  * 0.8 
            w.Height  <-  parent.ActualHeight + 2. // to avoid border artifacts
            w.Width   <-  parent.ActualWidth  + 2. // to avoid border artifacts
            lastLocation <- Some(w.Left,w.Top)        

    let create(grid:TabsAndLog, statusBar:SeffStatusBar) = 
        let parent = grid.Window.Window        
        let ed = grid.Tabs.Current.Editor
        if statusBar.CheckerStatus.IsMouseOver || ed.ErrorHighlighter.ToolTip.IsOpen then
            match statusBar.CheckerStatus.ToolTip with
                | :? ToolTip as tt -> 
                    match tt.Content with 
                    | :? StackPanel  -> showWindow ("Seff PopOut| Compiler Error Info ", (fun () -> statusBar.CheckerStatus.GetErrorPanelCached ed) , parent)
                    | _ -> () // if ToolTip is just a string ?? don't pop out                
                | :? StackPanel  -> showWindow ("Seff PopOut| Compiler Error Info ", (fun () -> statusBar.CheckerStatus.GetErrorPanelCached ed), parent)
                | _ -> () // if ToolTip is just a string?? don't pop out

        if ed.TypeInfoTip.IsOpen then
            showWindow ("Seff PopOut| Type Info ", (fun () -> Some<|  TypeInfo.getPanelCached ()), parent)

        if ed.Completions.IsOpen  && ed.Completions.HasStackPanelTypeInfo then
            showWindow ("Seff PopOut| Autocomplete Type Info ", (fun () -> Some<|  TypeInfo.getPanelCached ()), parent)



