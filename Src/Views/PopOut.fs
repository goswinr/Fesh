namespace Fesh.Views

open System
open Avalonia
open Avalonia.Controls

open Fesh
open Fesh.Util.General
open Fesh.Model
open Fesh.Editor
open Avalonia.Controls.Primitives
open Avalonia.Media.Imaging

module PopOut =

    let mutable private lastLocation : option<PixelPoint> =
        // TODO remember these postions in config
        None

    let icon:WindowIcon =
        try
            let gray =  Uri "avares://Fesh/Media/logoGray.ico"
            let bitmap = new Bitmap(Avalonia.Platform.AssetLoader.Open gray)
            new WindowIcon(bitmap)
        with ex ->
            IFeshLog.log.PrintfnAppErrorMsg  "Failed to load Media/logoGray.ico from Application.ResourceStream : %A" ex
            null

    //let internal copyUi(ui:Control) =
    //    // fails to serialize SelectableTextBlock
    //    // https://stackoverflow.com/questions/32541/how-can-you-clone-a-wpf-object
    //    ui  |> Markup.XamlWriter.Save   |> fun s -> new IO.StringReader(s)  |> Xml.XmlReader.Create |> Markup.XamlReader.Load   :?> Control

    let showWindow(title, width, height, getUi:unit->option<#Control>, parent:Window) =
        match getUi() with
        |None -> ()
        |Some (content:#Control) ->
            let scr = ScrollViewer(Content = content)
            scr.VerticalScrollBarVisibility   <- ScrollBarVisibility.Auto
            scr.HorizontalScrollBarVisibility <- ScrollBarVisibility.Disabled // to have word wrap
            scr.Padding <- Thickness(7.0)

            let w = Window(Title= title)
            w.Content <- scr
            match lastLocation with
            |None ->
                w.WindowStartupLocation <- WindowStartupLocation.CenterOwner
            |Some pos ->
                w.WindowStartupLocation <- WindowStartupLocation.Manual
                // to cascade windows:
                w.Position <- PixelPoint(pos.X + 40, pos.Y + 40)


            if notNull icon then w.Icon <- icon
            w.PositionChanged.Add(fun _ -> lastLocation <- Some w.Position)
            if width > 30. then
                w.Width <- width  + 2. // to avoid border artifacts
                w.Height<- height + 2.
                w.Show(parent) // not ShowDialog(parent).Wait(), that would block the UI thread forever
            else
                w.SizeToContent <- SizeToContent.WidthAndHeight
                w.MaxHeight <- max 100. (parent.Bounds.Height * 0.8)
                w.MaxWidth  <- max 100. (parent.Bounds.Width  * 0.8)
                w.Show(parent)
            lastLocation <- Some w.Position

    let create(grid:TabsAndLog, statusBar:FeshStatusBar) =
        let parent = grid.FeshWindow.Window
        let ed = grid.Tabs.Current.Editor
        if statusBar.CheckerStatus.TextBlock.IsPointerOver|| ed.DrawingServices.errors.ToolTip.IsOpen then
            match ToolTip.GetTip statusBar.CheckerStatus.TextBlock with
            | :? StackPanel as sp -> showWindow ("Fesh PopOut | Compiler Error Info ", sp.Bounds.Width, sp.Bounds.Height, (fun () -> statusBar.CheckerStatus.GetErrorPanelCached ed), parent)
            | _ -> () // if ToolTip is just a string don't pop out

        if ed.TypeInfoTip.IsOpen then
            let ti = ed.TypeInfoTip
            let wi, hi =
                match ti.Content with
                | null -> 0.,0.
                | c    -> c.Bounds.Width, c.Bounds.Height
            let newSV = TypeInfo.getPanelCached ()
            newSV.HorizontalScrollBarVisibility <- ScrollBarVisibility.Disabled // to have word wrap
            showWindow ("Fesh PopOut | Type Info ", wi, hi ,(fun () -> Some newSV), parent)

        if ed.Completions.IsOpen  then  //&& Completions.HasStackPanelTypeInfo then
            let newSV = TypeInfo.getPanelCached ()
            newSV.HorizontalScrollBarVisibility <- ScrollBarVisibility.Disabled // to have word wrap
            let wi,hi =
                match ed.Completions.ComplWin with
                | None -> 0.,0.
                | Some w ->
                    match w.ToolTipContent with
                    | :? ScrollViewer as sv -> sv.Bounds.Width, sv.Bounds.Height
                    | _ -> 0.,0.

            showWindow ("Fesh PopOut | Autocomplete Type Info ", wi, hi,(fun () -> Some newSV ), parent)



