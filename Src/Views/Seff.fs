namespace Seff

open Seff.Views
open Seff.Config
open Seff.Editor
open System.Windows
open Seff.Model

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
        
        dockP.Margin <- Thickness(tabsAndLog.GridSplitterSize)
        
        commands.SetUpGestureInputBindings()        
        
        win.Window.Content     <- dockP   
        win.Window.Background  <- menu.Bar.Background // call after setting up content, otherwise space next to tab headers is in an odd color
              
        //if config.Hosting.IsStandalone then win.Window.ContentRendered.Add(fun _ -> log.PrintfnInfoMsg "* Time for loading and rendering of main window: %s"  Timer.InstanceStartup.tocEx) 
        
        win.Window.ContentRendered.Add(fun _ -> tabs.CurrAvaEdit.Focus() |> ignore )
          
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
        
        win.Window.AllowDrop <- true // so it works on tab bar 
        win.Window.Drop.Add (fun e -> CursorBehaviour.TabBarDragAndDrop(log,tabs.AddFiles, e)) // text editor has it own drag event, this aplies to all other area ( eg log, tab bar) except the editor

    member this.Config = config

    member this.Tabs= tabsAndLog.Tabs

    member this.Log = tabsAndLog.Log

    member this.StatuBar = statusBar

    member this.Menu = menu

    member this.Checker = Checker.GetOrCreate(config)

    member this.Fsi = tabs.Fsi

    member this.Window = win.Window 

    member this.Commands = commands

    