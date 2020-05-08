namespace Seff

open System
open System.Environment
open System.IO
open System.Threading
open Seff.Model
open ICSharpCode
open System.Windows.Media // for color brushes
open System.Text
open System.Diagnostics
open System.Collections.Generic
open System.Windows.Controls
open System.Windows
open Seff.Util.WPF

/// A Static class holding the main grid of Tabs and the Log Window
/// Includes logic for toggeling the view split and saving and restoring size and position
type TabsAndLog private ()=
    

    static let grid                = new Grid()
    static let editorRowHeight     = new RowDefinition   (Height = makeGridLength (Config.Settings.GetFloat "EditorHeight"  400.0))
    static let logRowHeight        = new RowDefinition   (Height = makeGridLength (Config.Settings.GetFloat "LogHeight"     400.0))
    static let editorColumnWidth   = new ColumnDefinition(Width  = makeGridLength (Config.Settings.GetFloat "EditorWidth"   400.0))
    static let logColumnWidth      = new ColumnDefinition(Width  = makeGridLength (Config.Settings.GetFloat "LogWidth"      400.0))
    static let splitterHor         = new GridSplitter()             //|> Appearance.setForHorSplitter
    static let splitterVert        = new GridSplitter()             //|> Appearance.setForVertSplitter
    static let mutable isLogMaxed = false

    static let setGridHor() = 
        Config.Settings.setBool "isVertSplit" false
        setGridHorizontal grid [
            Tabs.Control        :> UIElement, editorRowHeight 
            splitterHor         :> UIElement, RowDefinition(Height = GridLength.Auto) 
            Log.ReadOnlyEditor  :> UIElement, logRowHeight
            ]
    
    static let setGridVert() =         
        Config.Settings.setBool "isVertSplit" true
        setGridVertical grid [         
            Tabs.Control        :> UIElement, editorColumnWidth 
            splitterVert        :> UIElement, ColumnDefinition(Width = GridLength.Auto) 
            Log.ReadOnlyEditor  :> UIElement, logColumnWidth 
            ]
    


    static member val MainWindow:Window = null with get,set // neded for toggeling max view, set in Win.fs
    
    static member Initialize() =
        if Config.Settings.getBool "isVertSplit" true then setGridVert()            
        else                                               setGridHor()

        splitterHor.Height <- 4.0
        splitterHor.HorizontalAlignment <- Windows.HorizontalAlignment.Stretch
        splitterHor.VerticalAlignment <- Windows.VerticalAlignment.Center
        splitterHor.ToolTip <- "Drag to resize code editor and log window"

        splitterVert.Width  <- 4.0        
        splitterVert.VerticalAlignment <- Windows.VerticalAlignment.Stretch
        splitterVert.HorizontalAlignment <- Windows.HorizontalAlignment.Center //needed only on vertical split
        splitterVert.ToolTip <- "Drag to resize code editor and log window"

        splitterHor.DragCompleted.Add  (fun _ -> 
                isLogMaxed <- false                    
                editorRowHeight.Height <- makeGridLength    editorRowHeight.ActualHeight
                logRowHeight.Height    <- makeGridLength    logRowHeight.ActualHeight
                Config.Settings.SetFloat "EditorHeight"     editorRowHeight.ActualHeight
                Config.Settings.SetFloat "LogHeight"        logRowHeight.ActualHeight            
                Config.Settings.Save ()
                )

        splitterVert.DragCompleted.Add (fun _ -> 
                isLogMaxed <- false                    
                editorColumnWidth.Width <- makeGridLength   editorColumnWidth.ActualWidth
                logColumnWidth.Width    <- makeGridLength   logColumnWidth.ActualWidth
                Config.Settings.SetFloat "EditorWidth"      editorColumnWidth.ActualWidth
                Config.Settings.SetFloat "LogWidth"         logColumnWidth.ActualWidth            
                Config.Settings.Save ()
                )

    static member ToggleSplit() = 
        if Config.Settings.getBool "isVertSplit" true then setGridHor()            
        else                                               setGridVert()
        Config.Settings.Save ()

    static member ToggleMaxLog() = 
        if  isLogMaxed then // if it is already maxed then size down again
            isLogMaxed <- false
            editorRowHeight.Height   <- makeGridLength <|Config.Settings.GetFloat "EditorHeight" 99.// TODO ad vert
            logRowHeight.Height      <- makeGridLength <|Config.Settings.GetFloat "LogHeight"    99.
            editorColumnWidth.Width  <- makeGridLength <|Config.Settings.GetFloat "EditorWidth" 99.
            logColumnWidth.Width     <- makeGridLength <|Config.Settings.GetFloat "LogWidth"    99.
            let wasMax = Config.Settings.getBool "WindowWasMax" false 
            if isNull TabsAndLog.MainWindow then Log.PrintAppErrorMsg "TabsAndLog.MainWindow null"// should never happen
            elif not wasMax then TabsAndLog.MainWindow.WindowState <- WindowState.Normal
        else
            isLogMaxed <- true
            let isMinOrMax = Config.Settings.getBool "WindowIsMinOrMax" false 
            Config.Settings.setBool "WindowWasMax" isMinOrMax             
            if isNull TabsAndLog.MainWindow then Log.PrintAppErrorMsg "TabsAndLog.MainWindow null" // should never happen
            elif not isMinOrMax then TabsAndLog.MainWindow.WindowState <- WindowState.Maximized
            editorRowHeight.Height   <- makeGridLength 0.
            logRowHeight.Height      <- makeGridLength 999.
            editorColumnWidth.Width  <- makeGridLength 0.
            logColumnWidth.Width     <- makeGridLength 999.

    static member Grid = grid
    
    