namespace Seff

open System.Windows
open System
open FsEx.Wpf.DependencyProps

open Seff.Views
open Seff.Config
open Seff.Editor
open Seff.Model

//#nowarn "44" // for AppDomain.GetCurrentThreadId()

/// the main App holding all UI and interaction ellements
/// this is passed on to hosting apps
type Seff (config:Config,log:Log) =    
    
    let win = new Views.Window(config)
    let tabs = new Tabs(config, win.Window)
    let tabsAndLog = new TabsAndLog(config, tabs, log, win)
    
    let statusBar = SeffStatusBar(tabsAndLog)
    let commands = Commands(tabsAndLog, statusBar)
    let menu = Menu(config, commands, tabs, statusBar, log)
    let dockP = dockPanelVert(menu.Bar , tabsAndLog.Grid , statusBar.Bar) 
        
    do        
        dockP.Margin <- Thickness(tabsAndLog.GridSplitterSize)
        
        commands.SetUpGestureInputBindings()        
        
        win.Window.AllowDrop <- true // so it works on tab bar 
        win.Window.Drop.Add (fun e -> CursorBehaviour.TabBarDragAndDrop(log,tabs.AddFiles, e)) // text editor has it own drag event, this aplies to all other area ( eg log, tab bar) except the editor

        win.Window.Content     <- dockP   
        win.Window.Background  <- menu.Bar.Background // call after setting up content, otherwise space next to tab headers is in an odd color  
        
        win.Window.ContentRendered.Add(fun _    -> KeyboardNative.hookUpForAltKeys(win.Window) )        
        win.Window.Closed.Add(         fun _    -> KeyboardNative.unHookForAltKeys() |> ignore )
        KeyboardNative.OnAltKeyCombo.Add(fun ac -> KeyboardShortcuts.altKeyCombo(ac) )
        
        //win.Window.PreviewKeyDown.Add( fun k -> log.PrintfnDebugMsg "key down: %A syss: %A" k.Key k.SystemKey)
        //if config.Hosting.IsStandalone then win.Window.ContentRendered.Add(fun _ -> log.PrintfnInfoMsg "* Time for loading and rendering of main window: %s"  Timer.InstanceStartup.tocEx) 
                 
        win.Window.Closing.Add( fun (e:ComponentModel.CancelEventArgs) ->
            // first check for running FSI
            match tabs.Fsi.AskIfCancellingIsOk () with 
            | NotEvaluating   -> ()
            | YesAsync472     -> tabs.Fsi.CancelIfAsync() 
            | Dont            -> e.Cancel <- true // dont close window   
            | NotPossibleSync -> () // cant show a dialog when in sync mode. show dialog from new thread ? TODO
            | NoAsync50       -> 
                match MessageBox.Show("Do you want to close the window while net50 code is still evaluating?", "Close Window during Evaluation?", MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.No) with
                | MessageBoxResult.Yes -> ()          
                | _  -> e.Cancel <- true // dont close window   
                    
                  
            //then check for unsaved files if not already canceled
            if not e.Cancel then 
                let canClose = tabs.AskForFileSavingToKnowIfClosingWindowIsOk() 
                if not canClose then e.Cancel <- true // dont close window  
            )        

        
        win.Window.ContentRendered.Add(fun _ -> tabs.CurrAvaEdit.Focus() |> ignore )

        // to show file changed event only when app gets focus again
        win.Window.Activated.Add( fun a -> 
            // only show thw events of the current active tab:
            //then other get shown when then editor chnages focus see FileWatcher.fs
            let actions = ResizeArray(tabs.Current.FileWatcher.OnFocusActions)
            tabs.Current.FileWatcher.OnFocusActions.Clear() // clone and clear first
            for action in actions do action()
            )

    member this.Config = config

    member this.Tabs= tabsAndLog.Tabs

    member this.Log = tabsAndLog.Log

    member this.StatuBar = statusBar

    member this.Menu = menu

    member this.Checker = Checker.GetOrCreate(config)

    member this.Fsi = tabs.Fsi

    member this.Window = win.Window 

    member this.Commands = commands

    