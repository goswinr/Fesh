namespace Seff

open Seff.Views
open Seff.Config
open Seff.Editor
open System.Windows

/// the main App holding all UI and interaction ellements
/// this is passed on to hosting apps
type Seff (config:Config,log:Log) =
    
    let win = new Views.Window(config)
    let tabs = new Tabs(config, win.Window)
    let tabsAndLog = new TabsAndLog(config, tabs, log, win)
    let commands = Commands(tabsAndLog)
    let statusBar = StatusBar(tabsAndLog, commands)
    let menu = Menu(config, commands, tabs, log)
    let dockP = Util.dockPanelVert(menu.Bar , tabsAndLog.Grid , statusBar.Bar)   
    
    do
        dockP.Margin <- Thickness(4.)
        
        commands.SetUpGestureInputBindings()
        
        if config.HostingMode.IsHosted then statusBar.AddFsiSynchModeStatus()

        win.Window.Content <-   dockP   
        win.Window.Background  <- menu.Bar.Background // call after setting up content, otherwise space next to tab headers is in an odd color
              

        // finish setting up window:
        win.Window.ContentRendered.Add(fun _ -> 
            //if not <| Tabs.Current.Editor.Focus() then log.PrintAppErrorMsg "Tabs.Current.Editor.Focus failed"  //or System.Windows.Input.FocusManager.SetFocusedElement(...)             
            log.PrintInfoMsg "* Time for loading and render main window: %s"  Timer.InstanceStartup.tocEx
            ) 
                  
        win.Window.Closing.Add( fun e ->
            // first check for running FSI
            match tabs.Fsi.AskIfCancellingIsOk () with 
            | NotEvaluating   -> ()
            | YesAsync        -> tabs.Fsi.CancelIfAsync() 
            | Dont            -> e.Cancel <- true // dont close window   
            | NotPossibleSync -> () // still close despite running thread ??
                  
            //then check for unsaved files:
            let canClose = tabs.AskIfClosingWindowIsOk() 
            if not canClose then e.Cancel <- true // dont close window  
            ) 

    member this.Config = config

    member this.Tabs= tabsAndLog.Tabs

    member this.Log = tabsAndLog.Log

    member this.StatuBar = statusBar

    member this.Menu = menu

    member this.Checker = Checker.GetOrCreate(config)

    member this.Fsi = Fsi.GetOrCreate(config)

    member this.Window = win.Window 

    member this.Commands = commands