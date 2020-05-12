namespace Seff.Editor

open ICSharpCode
open System.Windows.Controls
open ICSharpCode.AvalonEdit
open System.Windows.Media
open Seff.Config
open FSharp.Compiler.SourceCodeServices
open System.Windows
open System.IO



 /// The tab that holds the tab header and the code editor 
type Editor (code:string, config:Config, fileInfo:FileInfo Option) = //as this= 
    
    let avaEdit =           new AvalonEdit.TextEditor()    
    
    let checker =           Checker.Create(config)

    let errorHighligter =   new ErrorHighligter(avaEdit)

    let typeInfo =          new TypeInfo(avaEdit,checker)

    let complWin =          new CompletionWindow(avaEdit,config,checker)


    do
        //Editor.Document.Changed.Add(fun e //trigger text changed, TODO!! listener already added ? or added later        
        avaEdit.Text <- code
        avaEdit.ShowLineNumbers <- true
        avaEdit.VerticalScrollBarVisibility <- ScrollBarVisibility.Auto
        avaEdit.HorizontalScrollBarVisibility <- ScrollBarVisibility.Auto
        avaEdit.Options.HighlightCurrentLine <- true // http://stackoverflow.com/questions/5072761/avalonedit-highlight-current-line-even-when-not-focused
        avaEdit.Options.EnableHyperlinks <- true
        avaEdit.TextArea.TextView.LinkTextForegroundBrush <- Brushes.DarkGreen
        avaEdit.Options.EnableTextDragDrop <- true //TODO add implementation
        avaEdit.Options.ShowSpaces <- false //true
        avaEdit.Options.ShowTabs <- true
        avaEdit.Options.ConvertTabsToSpaces <- true
        avaEdit.Options.IndentationSize <- 4
        avaEdit.Options.HideCursorWhileTyping <- false
        avaEdit.TextArea.SelectionCornerRadius <- 0.0 
        avaEdit.TextArea.SelectionBorder <- null
        avaEdit.FontFamily <- Seff.Appearance.font
        avaEdit.FontSize <- config.Settings.GetFloat "FontSize" Seff.Appearance.fontSize 
        Search.SearchPanel.Install(avaEdit) |> ignore
        SyntaxHighlighting.setFSharp(avaEdit,config,false)

        checker.OnChecked.Add(errorHighligter.Draw)
        checker.CkeckAndHighlight(avaEdit,fileInfo)

        complWin.OnShowing.Add(fun _ -> errorHighligter.ToolTip.IsOpen <- false)
        complWin.OnShowing.Add(fun _ -> typeInfo.ToolTip.IsOpen        <- false)
        


    ///additional constructor using default code 
    new (config:Config) =  Editor( config.DefaultCode.Get() , config, None)

    member this.AvaEdit = avaEdit 
    
    /// The Tab class containing this editor takes care of updating this 
    member val FileInfo = fileInfo with get, set

    member val IsCurrent = false with get,set // TODO really needed ? this is managed in Tabs.selectionChanged event handler 
    
    member this.Checker = checker

    member this.ErrorHighligter = errorHighligter
    
    member this.ComletionWin = complWin

    //member val CompletionWindowJustClosed = false with get,set // for one letter completions to not trigger another completion
    
    //member val CompletionWindowClosed = fun ()->() with get,set //will be set with all the other eventhandlers setup, but ref is needed before
    
    /// to access the Config from editor
    member this.Config = config

    /// to access the Log view fom editor
    member this.Log = config.Log
  
    
    // additional text change event:
    //let completionInserted = new Event<string>() // event needed because Text change event is not raised after completion insert    
    //[<CLIEvent>]
    //member this.CompletionInserted = completionInserted.Publish
    //member this.TriggerCompletionInserted x = completionInserted.Trigger x // to raise it after completion inserted ?

