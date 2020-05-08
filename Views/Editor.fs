namespace Seff.Views

open ICSharpCode
open System.Windows.Controls
open ICSharpCode.AvalonEdit
open System.Windows.Media
open Seff.Config
open FSharp.Compiler.SourceCodeServices



 /// The tab that holds the tab header and the code editor 
type SeffEditor (code:string, config:Config) = //as this= 
    
    let ed = new AvalonEdit.TextEditor()    
      
    do
        //Editor.Document.Changed.Add(fun e //trigger text changed, TODO!! listener already added ? or added later        
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
        ed.FontFamily <- Seff.Appearance.font
        ed.FontSize <- config.Settings.GetFloat "FontSize" Seff.Appearance.fontSize 
        Search.SearchPanel.Install(ed) |> ignore
        SyntaxHighlighting.setFSharp(ed,false)
    
    ///additional constructor using default code 
    new (config:Config) =  SeffEditor( config.DefaultCode.Get() , config)

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
    
    member this.Config = config
  
    
    // additional text change event:
    //let completionInserted = new Event<string>() // event needed because Text change event is not raised after completion insert    
    //[<CLIEvent>]
    //member this.CompletionInserted = completionInserted.Publish
    //member this.TriggerCompletionInserted x = completionInserted.Trigger x // to raise it after completion inserted ?

