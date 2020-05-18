namespace Seff.Views

open Seff.Editor
open System.IO
open System.Windows.Controls
open System.Windows
open System.Windows.Media
open Seff

/// returns a bigger integer on each access for naming unsaved files
type Counter private () = 
    static let unsavedFile = ref 0
    
    /// returns a bigger integer on each access
    /// used to give each unsaved file a unique number
    static member UnsavedFile = incr unsavedFile ;  !unsavedFile


 /// The tab that holds the tab header logic and the code editor 
type Tab (editor:Editor) =  
    inherit TabItem()
    
    let mutable isCodeSaved        = true

    let mutable headerShowsSaved   = true

    let textBlock = new TextBlock(VerticalAlignment = VerticalAlignment.Bottom) //, FontFamily = Style.fontEditor)  
    
    let closeButton = new Button(
                            Content = new Shapes.Path( Data = Geometry.Parse("M0,7 L7,0 M0,0 L7,7"), Stroke = Brushes.Black,  StrokeThickness = 0.8 ) ,            //"M1,8 L8,1 M1,1 L8,8"       
                            Margin =  new Thickness(7., 0.5, 0.5, 3.), //left ,top, right, bottom
                            Padding = new Thickness(2.) )
    let header = 
        let p = new StackPanel(
                        Margin = Thickness(4. , 2. , 2. , 2.),//left ,top, right, bottom)
                        Orientation= Orientation.Horizontal) 
        p.Children.Add textBlock  |> ignore
        p.Children.Add closeButton |> ignore
        p
        
        
    let setHeader() = 
        match editor.FileInfo, isCodeSaved with 
        |Some fi , true -> 
            textBlock.ToolTip       <- "File saved at:\r\n" + fi.FullName
            textBlock.Text          <- fi.Name
            textBlock.Foreground    <- Brushes.Black
            headerShowsSaved        <- true
        |Some fi , false -> 
            textBlock.ToolTip       <- "File with unsaved changes from :\r\n" + fi.FullName
            textBlock.Text          <- fi.Name + "*"
            textBlock.Foreground    <- Brushes.DarkRed
            headerShowsSaved        <- false
        |None,_    -> 
            textBlock.ToolTip      <- "This file has not yet been saved to disk."
            textBlock.Text         <- sprintf "*unsaved-%d*" Counter.UnsavedFile  
            textBlock.Foreground   <- Brushes.Gray
     
    let upadteIsCodeCaved(isSaved)=
        if  not isSaved && headerShowsSaved then 
            isCodeSaved <- false
            setHeader()
        elif isSaved && not headerShowsSaved  then 
            isCodeSaved <- true
            setHeader()
       
    do
        base.Content <- editor.AvaEdit
        base.Header <- header
        //base.Margin <- Thickness(3., 0. , 0. , 0.) //left ,top, right, bottom) // don't messes it all up 
        setHeader()        
        editor.AvaEdit.TextChanged.Add(fun _ ->upadteIsCodeCaved(false)) 

    member this.IsCodeSaved 
        with get()       = isCodeSaved 
        and set(isSaved) = upadteIsCodeCaved(isSaved)
          
    /// this gets and set FileInfo on the Editor
    member this.FileInfo
        with get() = editor.FileInfo
        and set(fi) =
            editor.FileInfo <- fi
            setHeader()

    member this.CloseButton = closeButton // public so click event can be attached later in Tabs.fs AddTab
       
    member this.FormatedFileName = 
        match this.FileInfo with 
        |Some fi  -> sprintf "%s" fi.FullName //sprintf "%s\r\nat\r\n%s" fi.Name fi.DirectoryName
        |None     -> textBlock.Text
    
    /// this gets and sets IsCurrent on the Editor
    member this.IsCurrent  
        with get() = editor.IsCurrent
        and set(c) = editor.IsCurrent <- c

    member val Editor = editor 
    
    member val AvaEdit = editor.AvaEdit
    