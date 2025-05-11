namespace Fesh.Views

open System
open Avalonia.Controls
open Avalonia
open Avalonia.Media

open Fesh.Editor
open Fesh.Model

open AvaloniaLog.ImmBrush
open Avalonia.Layout


module TabStyle =
    let savedHeader   =  Brushes.Black
    let changedHeader =  Brushes.Red    |> darker 90
    let deletedHeader =  Brushes.Red    |> darker 20
    let unsavedHeader =  Brushes.Gray   |> brighter 40
    // button in header
    let redButton     =  ofRGB 232 17 35 // same red color as default for the main window
    let grayButton    =  ofRGB 150 150 150 // for gray cross inside red button
    let transpButton  =  ofARGB 0 255 255 255 // fully transparent


 /// The tab that holds the tab header logic and the code editor
 [<AllowNullLiteral>]
type Tab (editor:Editor)  =
    inherit TabItem()



    // these two are used to avoid redrawing header on very keystroke:
    let mutable isCodeSaved        = true
    let mutable headerShowsSaved   = true

    /// this can be set to false so that the dialog about saving only pops up once.
    /// In a hosted context like Rhino the dialog would pop on closing fesh window and on closing the Rhino window
    let mutable savingWanted = true


    let textBlock =
        let t = new TextBlock()
        t.VerticalAlignment <- VerticalAlignment.Center
        t.FontSize <- 12.0
        t

    let closeButton =
        let b =  new Button()
        //let cross = new Shapes.Path( Data = Geometry.Parse("M0,7 L7,0 M0,0 L7,7"),   StrokeThickness = 0.8 )  //"M1,8 L8,1 M1,1 L8,8"
        let cross = new Shapes.Path( Data = Geometry.Parse "M0,10 L10,0 M0,0 L10,10")
        b.Content <- cross
        //b.Margin <-  new Thickness(7., 0.5, 0.5, 3.) //left ,top, right, bottom
        b.Margin <-  new Thickness(7., 1. , 1. , 1.) //left ,top, right, bottom
        b.Padding <- new Thickness(3.)
        b.BorderThickness <- new Thickness(1.)
        b.BorderBrush <- TabStyle.transpButton
        b.Background <- TabStyle.transpButton
        cross.Stroke <- TabStyle.grayButton
        cross.StrokeThickness <- 1.0
        b.PointerEntered.Add (fun _ -> cross.StrokeThickness <- 1.0   ; cross.Stroke <- TabStyle.redButton ; b.BorderBrush <- TabStyle.grayButton)
        b.PointerExited.Add (fun _ -> cross.StrokeThickness <- 1.0   ; cross.Stroke <- TabStyle.grayButton; b.BorderBrush <- TabStyle.transpButton)
        b

    let header =
        let p = new StackPanel()
        p.Margin <- Thickness(4. , 2. , 2. , 2.) //left ,top, right, bottom)
        p.Orientation <- Orientation.Horizontal
        p.VerticalAlignment <- VerticalAlignment.Center
        p.Children.Add textBlock  |> ignore
        p.Children.Add closeButton |> ignore

        let border = new Border()
        border.BorderThickness <- Thickness(1. , 1. , 1. , 0.) //left ,top, right, bottom)
        border.CornerRadius <- CornerRadius(6., 6., 0., 0.) //left ,top, right, bottom)
        border.BorderBrush <- Brushes.Gray
        border.Padding <- Thickness 0.
        border.Margin <- Thickness 0. //left ,top, right, bottom)

        border.Child <- p
        border

    /// tread safe (for file watcher)
    let setHeader() =
        Fittings.SyncContext.doSync (fun () ->
            match editor.FilePath, isCodeSaved with
            |SetTo fi , true ->
                // textBlock.SetValue(ToolTipProperty, new ToolTip( "File saved at:\r\n" + fi.FullName))
                textBlock.Text            <- fi.Name
                textBlock.TextDecorations <- null
                textBlock.Foreground      <- TabStyle.savedHeader
                headerShowsSaved          <- true
            |SetTo fi , false ->
                // textBlock.ToolTip         <- "File with unsaved changes from :\r\n" + fi.FullName
                textBlock.Text            <- fi.Name + "*"
                textBlock.TextDecorations <- null
                textBlock.Foreground      <- TabStyle.changedHeader
                headerShowsSaved          <- false
            |NotSet dummyName,true ->
                // textBlock.ToolTip         <- "This file just shows the default code for every new file."
                textBlock.Text            <- dummyName
                textBlock.TextDecorations <- null
                textBlock.Foreground      <- TabStyle.unsavedHeader
                headerShowsSaved          <- true
            |NotSet dummyName,false ->
                // textBlock.ToolTip         <- "This file has not yet been saved to disk."
                textBlock.Text            <- dummyName
                textBlock.TextDecorations <- null
                //if not ( textBlock.Text.EndsWith "*") then textBlock.Text <- textBlock.Text + "*"
                textBlock.Foreground      <- TabStyle.changedHeader
                headerShowsSaved          <- false
            |Deleted dfi, _ ->
                // textBlock.ToolTip         <- "This file has been deleted (or renamed) from:\r\n" + dfi.FullName
                textBlock.Text            <- dfi.Name
                textBlock.TextDecorations <- TextDecorations.Strikethrough
                textBlock.Foreground      <- TabStyle.deletedHeader
                headerShowsSaved          <- false
            )

    /// this gets called on every character typed.
    // can be called async too.
    let setCodeSavedStatus isSaved=
        savingWanted <-true //to always ask gain after a doc change
        isCodeSaved <- isSaved
        if not isSaved && headerShowsSaved then // to only update header if actually required
            setHeader()
        elif isSaved && not headerShowsSaved  then // to only update header if actually required
            setHeader()

    let fileTracker =
        new FileChangeTracker (editor, setCodeSavedStatus)

    do
        base.Content <- editor.AvaEdit
        base.Header <- header



        // TODO wrap tabItem in border element and the style the border instead ??
        //ti.Padding <- Thickness(2.)   // don't messes it all up
        //ti.Margin <- Thickness(2.)   // don't messes it all up
        //ti.BorderThickness <- Thickness(4.)       // don't messes it all up
        //ti.BorderBrush <- Brushes.Blue            // don't messes it all up
        //ti.Margin <- Thickness(3., 0. , 0. , 0.)  //left ,top, right, bottom) // don't messes it all up
        setHeader()
        editor.AvaEdit.TextChanged.Add(fun _ -> setCodeSavedStatus false)

    // if this class just would inherits a Tab Item:
    // The control TextBlock (Text = Body) already has a visual parent ContentPresenter
    // (Name = PART_SelectedContentHost, Host = TabControl) while trying to add it
    // as a child of ContentPresenter (Name = PART_ContentPresenter, Host = TestTabs.Tab).
    override _.StyleKeyOverride = typeof<TabItem>

    member _.FileTracker = fileTracker

    member _.IsCodeSaved
        with get()       = isCodeSaved
        and set isSaved  = setCodeSavedStatus(isSaved)

    /// this can be set to false so that the dialog about saving only pops up once.
    /// In a hosted context like Rhino the dialog would pop on closing fesh window and on closing the Rhino window
    member _.SavingWanted
        with get() = savingWanted
        and set(v) = savingWanted <- v

    member _.UpdateTabHeader() = setHeader()

    member _.CloseButton = closeButton // public so click event can be attached later in Tabs.fs AddTab

    /// used in compiler error messages
    member this.FormattedFileName =
        match editor.FilePath with
        |SetTo fi          -> sprintf "%s" fi.FullName //sprintf "%s\r\nat\r\n%s" fi.Name fi.DirectoryName
        |Deleted fi        -> sprintf "(deleted): %s" fi.FullName //sprintf "%s\r\nat\r\n%s" fi.Name fi.DirectoryName
        |NotSet dummyName  -> dummyName

    member val Editor = editor

    member val AvaEdit = editor.AvaEdit

