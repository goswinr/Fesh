namespace Seff.Editor

open ICSharpCode
open System.Windows.Controls
open ICSharpCode.AvalonEdit
open System.Windows.Media
open Seff.Config
open FSharp.Compiler.SourceCodeServices
open System.Windows



 /// The tab that holds the tab header and the code editor 
type Editor (code:string, config:Config) = //as this= 
    
    let ed = new AvalonEdit.TextEditor()    
    
    let checker = Checker.Create(config.Log)

    let errorHighligter = new ErrorHighligter(ed)

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
        SyntaxHighlighting.setFSharp(ed,config,false)

        checker.OnChecked.Add(errorHighligter.Draw)
        


    ///additional constructor using default code 
    new (config:Config) =  Editor( config.DefaultCode.Get() , config)

    member val AvaEdit = ed 
    
    member val IsCurrent = false with get,set // this is managed in Tabs.selctionChanged event handler 
    
    member val Checker = checker

    member val ErrorHighligter = new ErrorHighligter(ed)






    member val FoldingManager = Folding.FoldingManager.Install(ed.TextArea)  
    
    member val Foldings:Option<ResizeArray<int*int>> =  None with get,set
    
    member val CompletionWin : CodeCompletion.CompletionWindow option = None with get,set   
        


    member val TypeInfoToolTip = new ToolTip(IsOpen=false) with get,set
   
    
    member val CompletionWindowJustClosed = false with get,set // for one letter completions to not trigger another completion
    
    member val CompletionWindowClosed = fun ()->() with get,set //will be set with all the other eventhandlers setup, but ref is needed before
    
    /// to access the Config from editor
    member this.Config = config

    /// to access the Log view fom editor
    member this.Log = config.Log
  
    
    // additional text change event:
    //let completionInserted = new Event<string>() // event needed because Text change event is not raised after completion insert    
    //[<CLIEvent>]
    //member this.CompletionInserted = completionInserted.Publish
    //member this.TriggerCompletionInserted x = completionInserted.Trigger x // to raise it after completion inserted ?

