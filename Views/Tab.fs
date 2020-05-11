namespace Seff.Views

open Seff.Editor
open System.IO
open System.Windows.Controls
open System.Windows
open System.Windows.Media

/// returns a bigger integer on each access for naming unsaved files
type Counter private () = 
    static let unsavedFile = ref 0
    
    /// returns a bigger integer on each access
    /// used to give each unsaved file a unique number
    static member UnsavedFile = incr unsavedFile ;  !unsavedFile


 /// The tab that holds the tab header logic and the code editor 
type Tab (editor:Editor, fileInfoOp :FileInfo option) = //as this= 
    inherit TabItem()
    
    let mutable isCodeSaved          = true

    let mutable fileInfo = fileInfoOp

    let mutable headerShowsUnsaved   = false

    let textBlock = new TextBlock(VerticalAlignment = VerticalAlignment.Bottom)  
    
    let closeButton = new Button(
                            Content = new Shapes.Path( Data = Geometry.Parse("M0,7 L7,0 M0,0 L7,7"), Stroke = Brushes.Black,  StrokeThickness = 0.8 ) ,            //"M1,8 L8,1 M1,1 L8,8"       
                            Margin =  new Thickness(7., 0.5, 0.5, 3.), //left ,top, right, bottom
                            Padding = new Thickness(2.) )
    let header = 
        let p = new StackPanel(
                        Margin = Thickness(2.5 , 0.5 , 0.5 , 2.5),
                        Orientation= Orientation.Horizontal) //left ,top, right, bottom)
        p.Children.Add textBlock  |> ignore
        p.Children.Add closeButton |> ignore
        p
        
        
    let setHeader() = 
        match fileInfo, isCodeSaved with 
        |Some fi , true -> 
            textBlock.ToolTip       <- "File saved at:\r\n" + fi.FullName
            textBlock.Text          <- fi.Name
            textBlock.Foreground    <- Brushes.Black
            headerShowsUnsaved <- false
        |Some fi , false -> 
            textBlock.ToolTip       <- "File with unsaved changes from :\r\n" + fi.FullName
            textBlock.Text          <- fi.Name + "*"
            textBlock.Foreground    <- Brushes.DarkRed
            headerShowsUnsaved <- true
        |None,_    -> 
            textBlock.ToolTip      <- "This file has not yet been saved to disk."
            textBlock.Text         <- sprintf "* unsaved-%d *" Counter.UnsavedFile  
            textBlock.Foreground   <- Brushes.Gray
       
       
    do
        base.Content <- editor 
        base.Header <- header
        setHeader()
        editor.Checker.Check(ed,fileInfo)

    member this.IsCodeSaved 
        with get() = isCodeSaved 
        and set(isSaved) = 
            if  not isSaved && not headerShowsUnsaved then 
                isCodeSaved <- false
                setHeader()
            elif isSaved && headerShowsUnsaved  then 
                isCodeSaved <- true
                setHeader()
    
    member this.CloseButton = closeButton // public so click event can be attached later in Tabs.fs AddTab
       
    member this.FormatedFileName = 
        match this.FileInfo with 
        |Some fi  -> sprintf "%s\r\nat\r\n%s" fi.Name fi.DirectoryName
        |None     -> textBlock.Text
    
    member val IsCurrent = false with get,set
    
    member val FileInfo:FileInfo option = fileInfo with get,set

    member val Editor = editor 
    
    member val AvaEdit = editor.AvaEdit
    