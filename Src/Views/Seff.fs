namespace Seff

open System.Windows
open System
open Fittings.DependencyProps

open Seff.Views
open Seff.Config
open Seff.Editor

//#nowarn "44" // for AppDomain.GetCurrentThreadId()

/// the main App holding all UI and interaction elements
/// this is passed on to hosting apps
type Seff (config:Config,log:Log) = 

    let seffWin = new Views.SeffWindow(config)
    let win = seffWin.Window
    let tabs = new Tabs(config, log, seffWin)
    let tabsAndLog = new TabsAndLog(config, tabs, log, seffWin)

    let statusBar = SeffStatusBar(tabsAndLog)
    let commands = Commands(tabsAndLog, statusBar)
    let menu = Menu(config, commands, tabs, statusBar, log)
    let dockP = dockPanelVert(menu.Bar , tabsAndLog.Grid , statusBar.Bar)

    do
        dockP.Margin <- Thickness(tabsAndLog.GridSplitterSize)

        commands.SetUpGestureInputBindings()        

        win.AllowDrop <- true // so it works on tab bar
        win.Drop.Add (fun e -> DragAndDrop.onTabHeaders(tabs.AddFiles, e)) // text editor has it own drag event, this applies to all other area ( eg log, tab bar) except the editor (see handler)

        win.Content     <- dockP
        win.Background  <- menu.Bar.Background // call after setting up content, otherwise space next to tab headers is in an odd color

        win.ContentRendered.Add(fun _    -> KeyboardNative.hookUpForAltKeys(win) )
        win.Closed.Add(         fun _    -> KeyboardNative.unHookForAltKeys() |> ignore )
        KeyboardNative.OnAltKeyCombo.Add(fun ac -> KeyboardShortcuts.altKeyCombo(ac) )
        
        //if config.RunContext.IsStandalone then win.Window.ContentRendered.Add(fun _ -> log.PrintfnInfoMsg "* Time for loading and rendering of main window: %s"  Timer.InstanceStartup.tocEx)

        win.Closing.Add( fun (e:ComponentModel.CancelEventArgs) ->
            // first check for running FSI
            match tabs.Fsi.AskIfCancellingIsOk () with
            | NotEvaluating   -> ()
            | YesAsync472     -> tabs.Fsi.CancelIfAsync()
            | YesAsync70      -> tabs.Fsi.CancelIfAsync()
            | UserDoesntWantTo-> e.Cancel <- true // don't close window
            | NotPossibleSync -> () // cant show a dialog when in sync mode. show dialog from new thread ? TODO            

            //second check for unsaved files if not already canceled
            if not e.Cancel then
                let canClose = tabs.AskForFileSavingToKnowIfClosingWindowIsOk()                
                if not canClose then 
                    e.Cancel <- true // don't close window               
            )
        
        win.Closed.Add(fun _ ->  tabs.Fsi.TriggerShutDownThreadEv() )// to clean up threads

        win.ContentRendered.Add(fun _ -> tabs.CurrAvaEdit.Focus() |> ignore )
        

        tabs.Fsi.OnRuntimeError.Add(fun _ ->  
            let w = win // because it might be hidden manually, or not visible from the start ( e.g. current script is evaluated in Seff.Rhino)
            if w.Visibility <> Visibility.Visible || w.WindowState=WindowState.Minimized then  win.Show() ) 

    member this.Config = config

    member this.Tabs= tabsAndLog.Tabs

    member this.Log = tabsAndLog.Log

    member this.StatusBar = statusBar

    member this.Menu = menu
    member this.Fsi = tabs.Fsi

    member this.Window = win

    member this.Commands = commands


