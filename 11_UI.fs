namespace Seff

open System
open System.Windows
open System.Windows.Controls
open System.Windows.Controls.Primitives // status bar
open System.Windows.Media
open ICSharpCode
open Seff.UtilWPF
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

    
    let en_US = Globalization.CultureInfo.CreateSpecificCulture("en-US")
    do
        Globalization.CultureInfo.DefaultThreadCurrentCulture   <- en_US
        Globalization.CultureInfo.DefaultThreadCurrentUICulture <- en_US

module StatusBar =
    let asyncDesc = 
        let bi = StatusBarItem(Content="*unknown*")
        bi.ToolTip <- "Click to switch between synchronous and asynchronous evaluation in FSI,\r\nsynchronous is needed for UI interaction,\r\nasynchronous allows easy cancellation and keeps the editor window alive"
        //bi.MouseDown.Add(fun _ -> toggleSync()) //done in fsi module
        bi

    let compilerErrors=
        let tb = TextBox(Text="checking for Errors...")
        tb.FontWeight <- FontWeights.Bold
        tb

    let setErrors(es:FSharpErrorInfo[])= 
        if es.Length = 0 then 
            compilerErrors.Text <- "No Errors"
            compilerErrors.Background <- Brushes.Green |> brighter 90            
        else 
            compilerErrors.Text <- sprintf "%d Errors" es.Length
            compilerErrors.Background <- Brushes.Red   |> brighter 90  
            compilerErrors.ToolTip <- makePanelVert [ for e in es do TextBlock(Text=sprintf "• Line %d: %A: %s" e.StartLineAlternate e.Severity e.Message)]

    let bar = 
        let b = new StatusBar()
        b.Items.Add compilerErrors      |> ignore 
        b.Items.Add (Separator())       |> ignore 
        b.Items.Add (StatusBarItem())   |> ignore // fill remaining space
        b

    let addSwitchFforSyncchonisationMode()=
        bar.Items.Insert(0,StatusBarItem(Content="FSI evaluation mode: "))
        bar.Items.Insert(1,asyncDesc)
        bar.Items.Insert(2,Separator())          
    

module UI =     
    let editorRowHeight     = RowDefinition   (Height = makeGridLength (Config.Settings.getFloat "EditorHeight"  400.0))//, MinHeight = minRowHeight)
    let logRowHeight        = RowDefinition   (Height = makeGridLength (Config.Settings.getFloat "LogHeight"     400.0))//, MinHeight = minRowHeight)
    let editorColumnWidth   = ColumnDefinition(Width  = makeGridLength (Config.Settings.getFloat "EditorWidth"   400.0))
    let logColumnWidth      = ColumnDefinition(Width  = makeGridLength (Config.Settings.getFloat "LogWidth"      400.0))

    let menu            = new Menu()
    let tabControl      = new TabControl()
    let splitterHor     = new GridSplitter()             |> Appearance.setForHorSplitter
    let splitterVert    = new GridSplitter()             |> Appearance.setForVertSplitter
    let log             = Log.Editor             |> Appearance.setForLog
    
    
    let gridHor() = 
        Config.Settings.setBool "isVertSplit" false
        makeGridHorizontalEx [         
            menu         :> UIElement, RowDefinition(Height = GridLength.Auto)
            tabControl   :> UIElement, editorRowHeight 
            splitterHor  :> UIElement, RowDefinition(Height = GridLength.Auto) 
            log          :> UIElement, logRowHeight         
            StatusBar.bar:> UIElement, RowDefinition(Height = GridLength.Auto)                
            // TODO add https://github.com/SwensenSoftware/fseye
            ]
    
    let gridVert() = 
        //if structure changes update ModifyWindowLayout.toggleSplit() too
        Config.Settings.setBool "isVertSplit" true
        let EditorAndLog =  makeGridVerticalEx [         
                tabControl    :> UIElement, editorColumnWidth 
                splitterVert  :> UIElement, ColumnDefinition(Width = GridLength.Auto) 
                log           :> UIElement, logColumnWidth 
                ]
        makeGridHorizontalEx [         
            menu         :> UIElement, RowDefinition(Height = GridLength.Auto)
            EditorAndLog :> UIElement, RowDefinition(Height = makeGridLength 200.0)
            StatusBar.bar:> UIElement, RowDefinition(Height = GridLength.Auto)                
            // TODO add https://github.com/SwensenSoftware/fseye
            ]

        