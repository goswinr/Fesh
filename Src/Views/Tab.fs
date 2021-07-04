﻿namespace Seff.Views

open System
open System.IO
open System.Windows.Controls
open System.Windows
open System.Windows.Media

open Seff
open Seff.Editor
open Seff.Model
open Seff.Util
open Seff.Util.Media


/// returns a bigger integer on each access for naming unsaved files
type Counter private () = 
    static let unsavedFile = ref 0
    
    /// returns a bigger integer on each access
    /// used to give each unsaved file a unique number
    static member UnsavedFile = incr unsavedFile ;  !unsavedFile

module TabStyle =
    let savedHeader   =  Brushes.Black  |> freeze
    let changedHeader =  Brushes.Red    |> darker 90   |> freeze
    let unsavedHeader =  Brushes.Gray   |> brighter 40 |> freeze


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
                        Orientation = Orientation.Horizontal) 
        p.Children.Add textBlock  |> ignore
        p.Children.Add closeButton |> ignore 
        p
        //let bor = new Border()
        //bor.Background <- Brushes.Blue
        //bor.Child <- p
        //bor 
        
    let setHeader() = 
        match editor.FilePath, isCodeSaved with 
        |SetTo fi , true -> 
            textBlock.ToolTip       <- "File saved at:\r\n" + fi.FullName
            textBlock.Text          <- fi.Name
            textBlock.Foreground    <- TabStyle.savedHeader
            headerShowsSaved        <- true
        |SetTo fi , false -> 
            textBlock.ToolTip       <- "File with unsaved changes from :\r\n" + fi.FullName
            textBlock.Text          <- fi.Name + "*"
            textBlock.Foreground    <- TabStyle.changedHeader
            headerShowsSaved        <- false
        |NotSet,true    -> 
            textBlock.ToolTip      <- "This file just shows the default code for every new file."
            textBlock.Text         <- sprintf "*unsaved-%d*" Counter.UnsavedFile  
            textBlock.Foreground   <- TabStyle.unsavedHeader
            headerShowsSaved       <- true
        |NotSet,false    -> 
            textBlock.ToolTip      <- "This file has not yet been saved to disk."
            if not ( textBlock.Text.EndsWith "*") then textBlock.Text <- textBlock.Text + "*"
            textBlock.Foreground   <- TabStyle.changedHeader
            headerShowsSaved       <- false

     
    let upadteIsCodeSaved(isSaved)=
        isCodeSaved <- isSaved
        if not isSaved && headerShowsSaved then
            setHeader()
        elif isSaved && not headerShowsSaved  then
            setHeader()
    
    let watcher = new FileWatcher(editor,upadteIsCodeSaved)        
       
    do
        base.Content <- editor.AvaEdit
        base.Header <- header
        // TODO wrap tabitem in border elemet and the style the border insetad ??
        //base.Padding <- Thickness(2.)   // don't messes it all up 
        //base.Margin <- Thickness(2.)   // don't messes it all up 
        //base.BorderThickness <- Thickness(4.)       // don't messes it all up 
        //base.BorderBrush <- Brushes.Blue            // don't messes it all up 
        //base.Margin <- Thickness(3., 0. , 0. , 0.)  //left ,top, right, bottom) // don't messes it all up 
        setHeader()        
        editor.AvaEdit.TextChanged.Add(fun _ -> upadteIsCodeSaved(false)) 
       

    member this.FileWatcher = watcher


    member this.IsCodeSaved 
        with get()       = isCodeSaved 
        and set(isSaved) = upadteIsCodeSaved(isSaved)
          
    /// this gets and set FileInfo on the Editor
    member this.FilePath
        with get() = editor.FilePath
        and set(fp) =
            editor.FilePath <- fp
            setHeader()
            // update file watcher:
            match editor.FilePath with
            |NotSet -> ()
            |SetTo fi ->
                watcher.Path <- fi.DirectoryName
                watcher.Filter <- fi.Name
                watcher.EnableRaisingEvents <- true
            

    member this.CloseButton = closeButton // public so click event can be attached later in Tabs.fs AddTab
       
    member this.FormatedFileName = 
        match this.FilePath with 
        |SetTo fi   -> sprintf "%s" fi.FullName //sprintf "%s\r\nat\r\n%s" fi.Name fi.DirectoryName
        |NotSet     -> textBlock.Text
    
    /// this gets and sets IsCurrent on the Editor
    member this.IsCurrent  
        with get() = editor.IsCurrent
        and set(c) = editor.IsCurrent <- c

    member val Editor = editor 
    
    member val AvaEdit = editor.AvaEdit
    