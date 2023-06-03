namespace Seff.Views

open System
open System.Windows.Controls
open System.Windows
open System.Windows.Media

open Seff.Editor
open Seff.Model

open AvalonLog.Brush


module TabStyle = 
    let savedHeader   =  Brushes.Black  |> freeze
    let changedHeader =  Brushes.Red    |> darker 90   |> freeze
    let deletedHeader =  Brushes.Red    |> darker 20   |> freeze
    let unsavedHeader =  Brushes.Gray   |> brighter 40 |> freeze
    // button in header
    let redButton     =  ofRGB 232 17 35 // same red color as default for the main window    
    let grayButton    =  ofRGB 150 150 150 // for gray cross inside red button
    let transpButton  =  ofARGB 0 255 255 255 // fully transparent


 /// The tab that holds the tab header logic and the code editor
type Tab (editor:Editor) = //, config:Seff.Config.Config, allFileInfos:seq<IO.FileInfo>) = 
    inherit TabItem()

    // thes two are used to avoid redrawing header on very keystroke:
    let mutable isCodeSaved        = true
    let mutable headerShowsSaved   = true

    let textBlock = new TextBlock(VerticalAlignment = VerticalAlignment.Center) //, Padding = Thickness(2.) ) , FontFamily = StyleState.fontEditor)

    let closeButton = 
        let b =  new Button()
        //let cross = new Shapes.Path( Data = Geometry.Parse("M0,7 L7,0 M0,0 L7,7"),   StrokeThickness = 0.8 )  //"M1,8 L8,1 M1,1 L8,8"
        let cross = new Shapes.Path( Data = Geometry.Parse("M0,10 L10,0 M0,0 L10,10"))
        b.Content <- cross
        //b.Margin <-  new Thickness(7., 0.5, 0.5, 3.) //left ,top, right, bottom
        b.Margin <-  new Thickness(7., 1. , 1. , 1.) //left ,top, right, bottom
        b.Padding <- new Thickness(3.)
        b.BorderThickness <- new Thickness(1.)
        b.BorderBrush <- TabStyle.transpButton
        b.Background <- TabStyle.transpButton
        cross.Stroke <- TabStyle.grayButton
        cross.StrokeThickness <- 1.0
        b.MouseEnter.Add (fun a -> cross.StrokeThickness <- 1.0   ; cross.Stroke <- TabStyle.redButton ; b.BorderBrush <- TabStyle.grayButton)
        b.MouseLeave.Add (fun a -> cross.StrokeThickness <- 1.0   ; cross.Stroke <- TabStyle.grayButton; b.BorderBrush <- TabStyle.transpButton)
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
                textBlock.ToolTip         <- "File saved at:\r\n" + fi.FullName
                textBlock.Text            <- fi.Name
                textBlock.TextDecorations <- null
                textBlock.Foreground      <- TabStyle.savedHeader
                headerShowsSaved          <- true
            |SetTo fi , false ->          
                textBlock.ToolTip         <- "File with unsaved changes from :\r\n" + fi.FullName
                textBlock.Text            <- fi.Name + "*"
                textBlock.TextDecorations <- null
                textBlock.Foreground      <- TabStyle.changedHeader
                headerShowsSaved          <- false
            |NotSet dummyName,true ->     
                textBlock.ToolTip         <- "This file just shows the default code for every new file."
                textBlock.Text            <- dummyName
                textBlock.TextDecorations <- null
                textBlock.Foreground      <- TabStyle.unsavedHeader
                headerShowsSaved          <- true
            |NotSet dummyName,false ->     
                textBlock.ToolTip         <- "This file has not yet been saved to disk."
                textBlock.Text            <- dummyName
                textBlock.TextDecorations <- null
                //if not ( textBlock.Text.EndsWith "*") then textBlock.Text <- textBlock.Text + "*"
                textBlock.Foreground      <- TabStyle.changedHeader
                headerShowsSaved          <- false
            |Deleted dfi, _ -> 
                textBlock.ToolTip         <- "This file has been deleted (or renamed) from:\r\n" + dfi.FullName
                textBlock.Text            <- dfi.Name
                textBlock.TextDecorations <- TextDecorations.Strikethrough
                textBlock.Foreground      <- TabStyle.deletedHeader
                headerShowsSaved          <- false
            )

    /// this gets called on every character typed.
    // can be called async too.
    let setCodeSavedStatus(isSaved)= 
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
        // TODO wrap tabitem in border element and the style the border instead ??
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

