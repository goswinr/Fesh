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
    //    // fails to serialize TextBlockSelectable
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
                w.ShowDialog(parent).Wait()
                w.Width <- width
                w.Height<- height
            else
                w.SizeToContent <- SizeToContent.WidthAndHeight
                w.Show()
                if w.Height > parent.Height * 0.8  then w.Height  <-  parent.Height * 0.8
                if w.Width  > parent.Width  * 0.8  then w.Width   <-  parent.Width  * 0.8
                w.Height  <-  max 10 <| parent.Height + 2. // to avoid border artifacts
                w.Width   <-  max 30 <| parent.Width  + 2. // to avoid border artifacts
            lastLocation <- Some w.Position

    let create(grid:TabsAndLog, statusBar:FeshStatusBar) =
        let parent = grid.FeshWindow.Window
        let ed = grid.Tabs.Current.Editor
        if statusBar.CheckerStatus.TextBlock.IsPointerOver|| ed.DrawingServices.errors.ToolTip.IsOpen then
            // match statusBar.CheckerStatus.ToolTip with
            //     | :? ToolTip as tt ->
            //         match tt.Content with
            //         | :? StackPanel as sp -> showWindow ("Fesh PopOut | Compiler Error Info ", sp.Width, sp.Height, (fun () -> statusBar.CheckerStatus.GetErrorPanelCached ed) , parent)
            //         | _ -> () // if ToolTip is just a string  don't pop out
            //     | :? StackPanel as sp -> showWindow ("Fesh PopOut | Compiler Error Info ", sp.Width, sp.Height, (fun () -> statusBar.CheckerStatus.GetErrorPanelCached ed), parent)
            //     | _ -> () // if ToolTip is just a string don't pop out
            ()

        if ed.TypeInfoTip.IsOpen then
            let ti = ed.TypeInfoTip
            let newSV = TypeInfo.getPanelCached ()
            newSV.HorizontalScrollBarVisibility <- ScrollBarVisibility.Disabled // to have word wrap
            showWindow ("Fesh PopOut | Type Info ", ti.Width, ti.Height ,(fun () -> Some newSV), parent)

        if ed.Completions.IsOpen  then  //&& Completions.HasStackPanelTypeInfo then
            let newSV = TypeInfo.getPanelCached ()
            newSV.HorizontalScrollBarVisibility <- ScrollBarVisibility.Disabled // to have word wrap
            let wi,hi =
                match ed.Completions.ComplWin with
                | None -> 0.,0.
                | Some w ->
                    match w.ToolTipContent with
                    | :? ScrollViewer as sv -> sv.Width, sv.Height
                    | _ -> 0.,0.

            showWindow ("Fesh PopOut | Autocomplete Type Info ", wi, hi,(fun () -> Some newSV ), parent)



