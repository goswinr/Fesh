namespace Seff.Views

open Seff
open Seff.Config
open System
open System.Windows.Controls
open System.Windows
open Seff.Views.Util
open FSharp.Compiler.CodeAnalysis

/// A class holding the main grid of Tabs and the log Window
/// Includes logic for toggeling the view split and saving and restoring size and position
type TabsAndLog (config:Config, tabs:Tabs, log:Log, win:Views.Window) as this =
    
    let gridSplitterSize = 4.0

    let grid                = new Grid()
    let editorRowHeight     = new RowDefinition   (Height = makeGridLength (config.Settings.GetFloat "EditorHeight"  400.0))
    let logRowHeight        = new RowDefinition   (Height = makeGridLength (config.Settings.GetFloat "LogHeight"     400.0))
    let editorColumnWidth   = new ColumnDefinition(Width  = makeGridLength (config.Settings.GetFloat "EditorWidth"   400.0))
    let logColumnWidth      = new ColumnDefinition(Width  = makeGridLength (config.Settings.GetFloat "LogWidth"      400.0))
    let splitterHor         = new GridSplitter()             
    let splitterVert        = new GridSplitter()             
    let mutable isLogMaxed = false

    let setGridHor() = 
        config.Settings.SetBool "isVertSplit" false
        setGridHorizontal grid [
            tabs.Control        :> UIElement, editorRowHeight 
            splitterHor         :> UIElement, RowDefinition(Height = GridLength.Auto) 
            log.ReadOnlyEditor  :> UIElement, logRowHeight
            ]
    
    let setGridVert() =         
        config.Settings.SetBool "isVertSplit" true
        setGridVertical grid [         
            tabs.Control        :> UIElement, editorColumnWidth 
            splitterVert        :> UIElement, ColumnDefinition(Width = GridLength.Auto) 
            log.ReadOnlyEditor  :> UIElement, logColumnWidth 
            ]
  
    static let mutable instance = Unchecked.defaultof<TabsAndLog>
    
    do
        TabsAndLog.Instance <- this
        if config.Settings.GetBool "isVertSplit" true then setGridVert()            
        else                                               setGridHor()

        splitterHor.Height <- gridSplitterSize
        splitterHor.HorizontalAlignment <- Windows.HorizontalAlignment.Stretch
        splitterHor.VerticalAlignment <- Windows.VerticalAlignment.Center
        splitterHor.ToolTip <- "Drag to resize code editor and log window"

        splitterVert.Width  <- gridSplitterSize       
        splitterVert.VerticalAlignment <- Windows.VerticalAlignment.Stretch
        splitterVert.HorizontalAlignment <- Windows.HorizontalAlignment.Center //needed only on vertical split
        splitterVert.ToolTip <- "Drag to resize code editor and log window"

        splitterHor.DragCompleted.Add  (fun _ -> 
                isLogMaxed <- false                    
                editorRowHeight.Height <- makeGridLength    editorRowHeight.ActualHeight
                logRowHeight.Height    <- makeGridLength    logRowHeight.ActualHeight
                config.Settings.SetFloat "EditorHeight"     editorRowHeight.ActualHeight
                config.Settings.SetFloat "LogHeight"        logRowHeight.ActualHeight            
                config.Settings.Save ()
                )

        splitterVert.DragCompleted.Add (fun _ -> 
                isLogMaxed <- false                    
                editorColumnWidth.Width <- makeGridLength   editorColumnWidth.ActualWidth
                logColumnWidth.Width    <- makeGridLength   logColumnWidth.ActualWidth
                config.Settings.SetFloat "EditorWidth"      editorColumnWidth.ActualWidth
                config.Settings.SetFloat "LogWidth"         logColumnWidth.ActualWidth            
                config.Settings.Save ()
                )
    
    static member Instance 
        with get() = instance
        and set v = instance <- v

    member this.ToggleSplit() = 
        if config.Settings.GetBool "isVertSplit" true then setGridHor()            
        else                                               setGridVert()
        config.Settings.Save ()

    member this.ToggleMaxLog() = 
        if  isLogMaxed then // if it is already maxed then size down again
            isLogMaxed <- false
            editorRowHeight.Height   <- makeGridLength <|config.Settings.GetFloat "EditorHeight" 99.
            logRowHeight.Height      <- makeGridLength <|config.Settings.GetFloat "LogHeight"    99.
            editorColumnWidth.Width  <- makeGridLength <|config.Settings.GetFloat "EditorWidth" 99.
            logColumnWidth.Width     <- makeGridLength <|config.Settings.GetFloat "LogWidth"    99.            
            if not win.WasMax then win.Window.WindowState <- WindowState.Normal
        else
            // maximase log view
            isLogMaxed <- true
            win.WasMax <- win.IsMinOrMax            
            if not win.IsMinOrMax  then win.Window.WindowState <- WindowState.Maximized
            editorRowHeight.Height   <- makeGridLength 0.
            logRowHeight.Height      <- makeGridLength 999.
            editorColumnWidth.Width  <- makeGridLength 0.
            logColumnWidth.Width     <- makeGridLength 999.

    member this.Grid = grid    
    
    member this.Config = config

    member this.Tabs= tabs

    member this.Log = log

    member this.Window = win 

    member this.GridSplitterSize = gridSplitterSize
