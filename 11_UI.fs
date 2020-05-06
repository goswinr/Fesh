namespace Seff

open System
open System.Windows
open System.Windows.Controls
open System.Windows.Controls.Primitives // status bar
open System.Windows.Media
open ICSharpCode
open Seff.Util.WPF
open FSharp.Compiler.SourceCodeServices
open System.Windows.Automation.Peers


module Appearance=    
    
    
    let logBackgroundFsiEvaluating   =  Brushes.White    |> darker 30 
    let logBackgroundFsiReady    =      Brushes.White
    let editorBackgroundOk       =      Brushes.White
    let editorBackgroundErr      =      Brushes.Red      |> brighter 240 // very light pink
    let editorBackgroundChecking =      Brushes.White    |> darker 45    // light grey

    let defaultFontSize = 14.0
    let defaultFont = FontFamily("Consolas")
    let mutable fontSize = defaultFontSize // current size 


    let setForLog (l:AvalonEdit.TextEditor)=
        AvalonEdit.Search.SearchPanel.Install(l) |> ignore
        l.FontFamily       <- defaultFont
        l.FontSize         <- Config.Settings.getFloat "FontSize" defaultFontSize
        l.IsReadOnly       <- true
        l.ShowLineNumbers  <- true
        l.WordWrap         <- true
        l.Options.EnableHyperlinks <- true 
        l.Background <- logBackgroundFsiReady
        l.TextArea.TextView.LinkTextForegroundBrush <- l.Foreground //Hyperlinks color
        l.TextArea.SelectionCornerRadius <- 0.0 
        l.TextArea.SelectionBorder <- null        
        l.VerticalScrollBarVisibility <- ScrollBarVisibility.Auto        
        if Config.Settings.getBool "logHasLineWrap" true then 
            l.WordWrap         <- true // or 
            l.HorizontalScrollBarVisibility <- ScrollBarVisibility.Disabled   
        else
            l.WordWrap         <- false 
            l.HorizontalScrollBarVisibility <- ScrollBarVisibility.Auto  
        l

    let setForEditor (e:AvalonEdit.TextEditor)=
        e.Background <- editorBackgroundOk
        e.FontFamily <- defaultFont
        e.FontSize <- Config.Settings.getFloat "FontSize" defaultFontSize
        e.ShowLineNumbers <- true
        e.VerticalScrollBarVisibility <- ScrollBarVisibility.Auto
        e.HorizontalScrollBarVisibility <- ScrollBarVisibility.Auto
        e.Options.HighlightCurrentLine <- true // http://stackoverflow.com/questions/5072761/avalonedit-highlight-current-line-even-when-not-focused
        e.Options.EnableHyperlinks <- true
        e.TextArea.TextView.LinkTextForegroundBrush <- Brushes.DarkGreen
        e.Options.EnableTextDragDrop <- true //TODO add implementation
        e.Options.ShowSpaces <- false //true
        e.Options.ShowTabs <- true
        e.Options.ConvertTabsToSpaces <- true
        e.Options.IndentationSize <- 4
        e.Options.HideCursorWhileTyping <- false
        //customise selection style:
        e.TextArea.SelectionCornerRadius <- 0.0 
        e.TextArea.SelectionBorder <- null
        
        e
    
    let setForHorSplitter (g:GridSplitter) = 
        g.Height <- 3.0
        g.HorizontalAlignment <- Windows.HorizontalAlignment.Stretch
        g.VerticalAlignment <- Windows.VerticalAlignment.Center
        g.ToolTip <- "Drag to resize code editor and log window"
        g
    
    let setForVertSplitter (g:GridSplitter) = 
        g.Width  <- 3.0        
        g.VerticalAlignment <- Windows.VerticalAlignment.Stretch
        g.HorizontalAlignment <- Windows.HorizontalAlignment.Center //needed only on vertical split
        g.ToolTip <- "Drag to resize code editor and log window"
        g


