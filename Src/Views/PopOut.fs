namespace Seff.Views

open System
open System.Windows
open System.Windows.Controls

open Seff
open Seff.Util.General
open Seff.Model
open Seff.Editor

module PopOut = 

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

    let showWindow(title, getUi:unit->#UIElement, parent:Window,last:option<Window>) = 
        let w = Window(Title= title)
        let scr = ScrollViewer(Content = getUi() )
        scr.VerticalScrollBarVisibility   <- ScrollBarVisibility.Auto
        scr.HorizontalScrollBarVisibility <- ScrollBarVisibility.Auto
        scr.Padding <- Thickness(7.0)
        w.Content <- scr
        w.SizeToContent <- SizeToContent.WidthAndHeight
        if w.ActualHeight > parent.ActualHeight * 0.8 then  w.Height  <-  parent.ActualHeight * 0.8
        if w.ActualWidth  > parent.ActualWidth  * 0.8  then w.Width   <-  parent.ActualWidth  * 0.8
        w.Owner <- parent
        match last with
        |None ->
            w.WindowStartupLocation <- WindowStartupLocation.CenterOwner
        |Some lw ->
            // cascade windows
            w.WindowStartupLocation <- WindowStartupLocation.Manual
            w.Left <- lw.Left + 50.
            w.Top <- lw.Top + 50.

        if notNull icon then w.Icon <- icon
        w.Show()
        w

    let create(grid:TabsAndLog, statusBar:SeffStatusBar) = 
        let parent = grid.Window.Window
        let mutable last :option<Window> = None
        let ed = grid.Tabs.Current.Editor
        if statusBar.CheckerStatus.IsMouseOver || ed.ErrorHighlighter.ToolTip.IsOpen then
            match statusBar.CheckerStatus.ToolTip with
                | :? StackPanel  ->
                    last <- Some <| showWindow ("Seff PopOut| Compiler Error Info ", statusBar.CheckerStatus.GetErrorPanelCached,parent,last )
                | _ -> () // if ToolTip is just a string dont pop out

        if ed.TypeInfoTip.IsOpen then
            last <- Some <|showWindow ("Seff PopOut| Type Info ", TypeInfo.getPanelCached, parent,last )

        if ed.Completions.IsOpen  && ed.Completions.HasStackPanelTypeInfo then
            last <- Some <|showWindow ("Seff PopOut| Autocomplete Type Info ", TypeInfo.getPanelCached, parent,last )



