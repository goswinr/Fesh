namespace Fesh.Views

open System
open System.Windows.Controls
open System.Windows
open System.Windows.Media

open Fesh
open Fesh.Editor
open Fesh.Model

open AvalonLog.Brush


module TabStyle =
    // the colors are in Theme.tabs
    let transpButton  =  ofARGB 0 255 255 255 // fully transparent


 /// The tab that holds the tab header logic and the code editor
type Tab (editor:Editor) = //, config:Fesh.Config.Config, allFileInfos:seq<IO.FileInfo>) =
    inherit TabItem()

    // these two are used to avoid redrawing header on very keystroke:
    let mutable isCodeSaved        = true
    let mutable headerShowsSaved   = true
    let mutable headerShowsDeleted = false

    /// this can be set to false so that the dialog about saving only pops up once.
    /// In a hosted context like Rhino the dialog would pop on closing fesh window and on closing the Rhino window
    let mutable savingWanted = true


    let textBlock = new TextBlock(VerticalAlignment = VerticalAlignment.Center) //, Padding = Thickness(2.) ) , FontFamily = StyleState.fontEditor)

    //let cross = new Shapes.Path( Data = Geometry.Parse("M0,7 L7,0 M0,0 L7,7"),   StrokeThickness = 0.8 )  //"M1,8 L8,1 M1,1 L8,8"
    let cross = new Shapes.Path( Data = Geometry.Parse("M0,10 L10,0 M0,0 L10,10"))

    let closeButton =
        let b =  new Button()
        b.Content <- cross
        //b.Margin <-  new Thickness(7., 0.5, 0.5, 3.) //left ,top, right, bottom
        b.Margin <-  new Thickness(7., 1. , 1. , 1.) //left ,top, right, bottom
        b.Padding <- new Thickness(3.)
        b.BorderThickness <- new Thickness(1.)
        b.BorderBrush <- TabStyle.transpButton
        b.Background <- TabStyle.transpButton
        cross.Stroke <- Theme.tabs.closeButton
        cross.StrokeThickness <- 1.0
        b.MouseEnter.Add (fun _ -> cross.StrokeThickness <- 1.0   ; cross.Stroke <- Theme.tabs.closeButtonHover ; b.BorderBrush <- Theme.tabs.closeButton)
        b.MouseLeave.Add (fun _ -> cross.StrokeThickness <- 1.0   ; cross.Stroke <- Theme.tabs.closeButton; b.BorderBrush <- TabStyle.transpButton)
        b

    let header =
        let p = new StackPanel(
                        Margin = Thickness(4. , 2. , 2. , 2.),//left ,top, right, bottom)
                        Orientation = Orientation.Horizontal,
                        VerticalAlignment = VerticalAlignment.Center)
        p.Children.Add textBlock  |> ignore
        p.Children.Add closeButton |> ignore
        p

    /// tread safe (for file watcher)
    let setHeader() =
        editor.AvaEdit.Dispatcher.Invoke(fun () ->
            match editor.FilePath, isCodeSaved with
            |SetTo fi , true ->
                headerShowsDeleted        <- false
                textBlock.ToolTip         <- "File saved at:\r\n" + fi.FullName
                textBlock.Text            <- fi.Name
                textBlock.TextDecorations <- null
                textBlock.Foreground      <- Theme.tabs.saved
                headerShowsSaved          <- true
            |SetTo fi , false ->
                headerShowsDeleted        <- false
                textBlock.ToolTip         <- "File with unsaved changes from :\r\n" + fi.FullName
                textBlock.Text            <- fi.Name + "*"
                textBlock.TextDecorations <- null
                textBlock.Foreground      <- Theme.tabs.changed
                headerShowsSaved          <- false
            |NotSet dummyName,true ->
                headerShowsDeleted        <- false
                textBlock.ToolTip         <- "This file just shows the default code for every new file."
                textBlock.Text            <- dummyName
                textBlock.TextDecorations <- null
                textBlock.Foreground      <- Theme.tabs.unsaved
                headerShowsSaved          <- true
            |NotSet dummyName,false ->
                headerShowsDeleted        <- false
                textBlock.ToolTip         <- "This file has not yet been saved to disk."
                textBlock.Text            <- dummyName
                textBlock.TextDecorations <- null
                //if not ( textBlock.Text.EndsWith "*") then textBlock.Text <- textBlock.Text + "*"
                textBlock.Foreground      <- Theme.tabs.changed
                headerShowsSaved          <- false
            |Deleted dfi, _ ->
                headerShowsDeleted        <- true
                textBlock.ToolTip         <- "This file has been deleted (or renamed) from:\r\n" + dfi.FullName
                textBlock.Text            <- dfi.Name
                textBlock.TextDecorations <- TextDecorations.Strikethrough
                textBlock.Foreground      <- Theme.tabs.deleted
                headerShowsSaved          <- false
            )

    /// this gets called on every character typed.
    // can be called async too.
    let setCodeSavedStatus(isSaved)=
        savingWanted <-true //to always ask gain after a doc change
        isCodeSaved <- isSaved
        let filePathStateChanged =
            match editor.FilePath with
            |Deleted _ -> not headerShowsDeleted
            |SetTo _ |NotSet _ -> headerShowsDeleted
        if filePathStateChanged then
            setHeader()
        elif not isSaved && headerShowsSaved then // to only update header if actually required
            setHeader()
        elif isSaved && not headerShowsSaved  then // to only update header if actually required
            setHeader()

    let fileTracker =
        new FileChangeTracker (editor, setCodeSavedStatus)

    do
        base.Content <- editor.AvaEdit
        base.Header <- header
        // implicit styles only apply to the exact type, this is a subclass of TabItem, so use the dark TabItem style explicitly:
        base.SetResourceReference(FrameworkElement.StyleProperty, typeof<TabItem>)
        // TODO wrap tabItem in border element and the style the border instead ??
        //base.Padding <- Thickness(2.)   // don't messes it all up
        //base.Margin <- Thickness(2.)   // don't messes it all up
        //base.BorderThickness <- Thickness(4.)       // don't messes it all up
        //base.BorderBrush <- Brushes.Blue            // don't messes it all up
        //base.Margin <- Thickness(3., 0. , 0. , 0.)  //left ,top, right, bottom) // don't messes it all up
        setHeader()
        editor.AvaEdit.TextChanged.Add(fun _ -> setCodeSavedStatus(false))

    member _.FileTracker = fileTracker

    member _.IsCodeSaved
        with get()       = isCodeSaved
        and set(isSaved) = setCodeSavedStatus(isSaved)

    /// this can be set to false so that the dialog about saving only pops up once.
    /// In a hosted context like Rhino the dialog would pop on closing fesh window and on closing the Rhino window
    member _.SavingWanted
        with get() = savingWanted
        and set(v) = savingWanted<-v

    member _.UpdateTabHeader() = setHeader()

    /// Applies the colors of the current theme to the header and the editor
    member _.ApplyTheme() =
        cross.Stroke <- Theme.tabs.closeButton
        setHeader()
        editor.ApplyTheme()

    member _.CloseButton = closeButton // public so click event can be attached later in Tabs.fs AddTab

    /// used in compiler error messages
    member this.FormattedFileName =
        match editor.FilePath with
        |SetTo fi          -> sprintf "%s" fi.FullName //sprintf "%s\r\nat\r\n%s" fi.Name fi.DirectoryName
        |Deleted fi        -> sprintf "(deleted): %s" fi.FullName //sprintf "%s\r\nat\r\n%s" fi.Name fi.DirectoryName
        |NotSet dummyName  -> dummyName

    member val Editor = editor

    member val AvaEdit = editor.AvaEdit

