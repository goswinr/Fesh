namespace Seff

open System
open System.Windows
open System.Windows.Controls
open System.Windows.Controls.Primitives // status bar
open System.Windows.Media
open ICSharpCode
open Seff.UtilWPF

module Appearance=    
    
    let private w  = 255uy // white
    let private aw = 240uy // almost white
    let private lg = 210uy // lightgrey
    let private dg = 99uy // dark grey
    let logBackgroundFsiEvaluating   = SolidColorBrush(Color.FromRgb(dg,dg,dg))//DarkGray 
    let logBackgroundFsiReady    = Brushes.Black
    let logBackgroundFsiHadError = Brushes.DarkRed
    let editorBackgroundOk       = Brushes.White
    let editorBackgroundErr      = SolidColorBrush(Color.FromRgb(w,aw,aw))// pink
    let editorBackgroundChecking = SolidColorBrush(Color.FromRgb(lg,lg,lg))// light grey

    let defaultFontSize = 14.0
    let defaultFont = FontFamily("Consolas")
    let mutable fontSize = defaultFontSize // current size 


    let setForLog (l:AvalonEdit.TextEditor)=
        AvalonEdit.Search.SearchPanel.Install(l) |> ignore
        l.FontFamily       <- defaultFont
        l.FontSize         <- Config.getFloat "FontSize" defaultFontSize
        l.IsReadOnly       <- true
        l.ShowLineNumbers  <- true
        l.WordWrap         <- true
        l.Options.EnableHyperlinks <- true 
        l.Background <- logBackgroundFsiReady
        l.Foreground <- Brushes.White
        l.TextArea.TextView.LinkTextForegroundBrush <- l.Foreground //Hyperlinks color
        l.TextArea.SelectionCornerRadius <- 0.0 
        //l.TextArea.SelectionBorder <- null
        l.VerticalScrollBarVisibility <- ScrollBarVisibility.Auto        
        if Config.getBool "logHasLineWrap" true then 
            l.WordWrap         <- true // or 
            l.HorizontalScrollBarVisibility <- ScrollBarVisibility.Disabled   
        else
            l.WordWrap         <- false 
            l.HorizontalScrollBarVisibility <- ScrollBarVisibility.Auto  
        l

    let setForEditor (e:AvalonEdit.TextEditor)=
        e.Background <- editorBackgroundOk
        e.FontFamily <- defaultFont
        e.FontSize <- Config.getFloat "FontSize" defaultFontSize
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
    let async = StatusBarItem(Content="*unknown*")
    
    let bar = 
        let b = new StatusBar()   
        b.Items.Add (StatusBarItem(Content="FSI is evaluation mode: "))  |> ignore
        b.Items.Add (async) |> ignore 
        b.Items.Add (Separator())|> ignore 
        b.Items.Add (StatusBarItem())  |> ignore // fill remaining space
        b
    

module UI =     
    let editorRowHeight     = RowDefinition   (Height = makeGridLength (Config.getFloat "EditorHeight"  400.0))//, MinHeight = minRowHeight)
    let logRowHeight        = RowDefinition   (Height = makeGridLength (Config.getFloat "LogHeight"     400.0))//, MinHeight = minRowHeight)
    let editorColumnWidth   = ColumnDefinition(Width  = makeGridLength (Config.getFloat "EditorWidth"   400.0))
    let logColumnWidth      = ColumnDefinition(Width  = makeGridLength (Config.getFloat "LogWidth"      400.0))

    let menu            = new Menu()
    let tabControl      = new TabControl()
    let splitterHor     = new GridSplitter()             |> Appearance.setForHorSplitter
    let splitterVert    = new GridSplitter()             |> Appearance.setForVertSplitter
    let log             = new AvalonEdit.TextEditor()    |> Appearance.setForLog
    
    
    let gridHor() = 
        Config.setBool "isVertSplit" false
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
        Config.setBool "isVertSplit" true
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

        