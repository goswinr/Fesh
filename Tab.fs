namespace Seff

open System
open System.IO
open ICSharpCode
open System.Windows.Controls
open System.IO
open System.Windows
open System.Windows.Controls
open ICSharpCode.AvalonEdit
open System.Windows.Media
open Seff.Util.WPF
open FSharp.Compiler.SourceCodeServices

/// returns a bigger integer on each access for naming unsaved files
type Counter private () = 
    static let unsavedFile = ref 1
    
    /// returns a bigger integer on each access
    /// used to give each unsaved file a unique number
    static member UnsavedFile = incr unsavedFile ;  !unsavedFile


 /// The tab that holds one editor of a code file, Log window is not part of tab, it exists only once
type Tab (code:string, fileInfoOp :FileInfo option) as this= 
    inherit TabItem()
    
    let ed = new AvalonEdit.TextEditor() //|> Appearance.setForEditor
    
    let mutable isCodeSaved          = true
    let mutable fileInfo :FileInfo option = None
    let mutable headerShowsUnsaved   = false

    let txBl = new TextBlock(VerticalAlignment = VerticalAlignment.Bottom)  
    
    let closeButton = new Button(
                            Content = new Shapes.Path( Data = Geometry.Parse("M0,8 L8,0 M0,0 L8,8"), Stroke = Brushes.Black,  StrokeThickness = 0.8 ) ,            //"M1,8 L8,1 M1,1 L8,8"       
                            Margin =  new Thickness(7., 0.5, 0.5, 3.), //left ,top, right, bottom
                            Padding = new Thickness(2.) )

    let setHeader() = 
        match fileInfo, isCodeSaved with 
        |Some fi , true -> 
            txBl.ToolTip       <- "File saved at:\r\n" + fi.FullName
            txBl.Text          <- fi.Name
            txBl.Foreground    <- Brushes.Black
            headerShowsUnsaved <- false
        |Some fi , false -> 
            txBl.ToolTip       <- "File with unsaved changes from :\r\n" + fi.FullName
            txBl.Text          <- fi.Name + "*"
            txBl.Foreground    <- Brushes.DarkRed
            headerShowsUnsaved <- true
        |None,_    -> 
            txBl.ToolTip      <- "This file has not yet been saved to disk."
            txBl.Text         <- sprintf "* unsaved-%d *" Counter.UnsavedFile  
            txBl.Foreground   <- Brushes.Gray
        let p = makePanelHor [txBl :> UIElement; closeButton :> UIElement ]
        p.Margin <- new Thickness(2.5 , 0.5 , 0.5 , 2.5) //left ,top, right, bottom
        this.Header <- p
      
    do
        //Editor.Document.Changed.Add(fun e //trigger text changed, TODO!! listener already added ? or added later
        fileInfo <- fileInfoOp
        ed.Text <- code 
        ed.ShowLineNumbers <- true
        ed.VerticalScrollBarVisibility <- ScrollBarVisibility.Auto
        ed.HorizontalScrollBarVisibility <- ScrollBarVisibility.Auto
        ed.Options.HighlightCurrentLine <- true // http://stackoverflow.com/questions/5072761/avalonedit-highlight-current-line-even-when-not-focused
        ed.Options.EnableHyperlinks <- true
        ed.TextArea.TextView.LinkTextForegroundBrush <- Brushes.DarkGreen
        ed.Options.EnableTextDragDrop <- true //TODO add implementation
        ed.Options.ShowSpaces <- false //true
        ed.Options.ShowTabs <- true
        ed.Options.ConvertTabsToSpaces <- true
        ed.Options.IndentationSize <- 4
        ed.Options.HideCursorWhileTyping <- false
        ed.TextArea.SelectionCornerRadius <- 0.0 
        ed.TextArea.SelectionBorder <- null
        ed.FontFamily <- Appearance.font
        ed.FontSize <- Config.Settings.getFloat "FontSize" Appearance.fontSize  
        this.Content <- ed         
        setHeader() 
        Search.SearchPanel.Install(ed) |> ignore
        SyntaxHighlighting.setFSharp(ed,false)
    
    ///additional constructor using default code 
    new () =  Tab(Config.DefaultCode.Get(),None)

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
        |None     -> txBl.Text
    
    member val IsCurrent = false with get,set
    
    member val FileInfo:FileInfo option = fileInfo with get,set

    member val Editor = ed 

    member val FoldingManager = Folding.FoldingManager.Install(ed.TextArea)  
    
    member val Foldings:Option<ResizeArray<int*int>> =  None with get,set
    
    member val CompletionWin : CodeCompletion.CompletionWindow option = None with get,set   
    
    member val FsCheckerResult: FSharpCheckFileResults option = None with get,set
    
    member val FsCheckerId = 0 with get,set // each check will get a unique id, used for UI background only?    
    
    member val ErrorToolTip =    new ToolTip(IsOpen=false) with get,set 

    member val TypeInfoToolTip = new ToolTip(IsOpen=false) with get,set
    
    member val ErrorMarker = new ErrorMarker(ed) with get
    
    member val CompletionWindowJustClosed = false with get,set // for one letter completions to not trigger another completion
    member val CompletionWindowClosed = fun ()->() with get,set //will be set with all the other eventhandlers setup, but ref is needed before
    
    
    // additional text change event:
    //let completionInserted = new Event<string>() // event needed because Text change event is not raised after completion insert    
    //[<CLIEvent>]
    //member this.CompletionInserted = completionInserted.Publish
    //member this.TriggerCompletionInserted x = completionInserted.Trigger x // to raise it after completion inserted ?

