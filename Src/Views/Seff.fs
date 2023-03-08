﻿namespace Seff

open System.Windows
open System
open FsEx.Wpf.DependencyProps

open Seff.Views
open Seff.Config
open Seff.Editor

//#nowarn "44" // for AppDomain.GetCurrentThreadId()

/// the main App holding all UI and interaction ellements
/// this is passed on to hosting apps
type Seff (config:Config,log:Log) = 

    let win = new Views.SeffWindow(config)
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
        win.Window.Drop.Add (fun e -> CursorBehavior.TabBarDragAndDrop(tabs.AddFiles, e)) // text editor has it own drag event, this aplies to all other area ( eg log, tab bar) except the editor (see handler)

        win.Window.Content     <- dockP
        win.Window.Background  <- menu.Bar.Background // call after setting up content, otherwise space next to tab headers is in an odd color

        win.Window.ContentRendered.Add(fun _    -> KeyboardNative.hookUpForAltKeys(win.Window) )
        win.Window.Closed.Add(         fun _    -> KeyboardNative.unHookForAltKeys() |> ignore )
        KeyboardNative.OnAltKeyCombo.Add(fun ac -> KeyboardShortcuts.altKeyCombo(ac) )
        
        //if config.RunContext.IsStandalone then win.Window.ContentRendered.Add(fun _ -> log.PrintfnInfoMsg "* Time for loading and rendering of main window: %s"  Timer.InstanceStartup.tocEx)

        win.Window.Closing.Add( fun (e:ComponentModel.CancelEventArgs) ->
            // first check for running FSI
            match tabs.Fsi.AskIfCancellingIsOk () with
            | NotEvaluating   -> ()
            | YesAsync472     -> tabs.Fsi.CancelIfAsync()
            | YesAsync70      -> tabs.Fsi.CancelIfAsync()
            | UserDoesntWantTo-> e.Cancel <- true // dont close window
            | NotPossibleSync -> () // cant show a dialog when in sync mode. show dialog from new thread ? TODO
            

            //second check for unsaved files if not already canceled
            if not e.Cancel then
                let canClose = tabs.AskForFileSavingToKnowIfClosingWindowIsOk()                
                if not canClose then 
                    e.Cancel <- true // dont close window               
            )
        
        win.Window.Closed.Add(fun _ ->  tabs.Fsi.TriggerShutDownThreadEv() )// to clean up threads

        win.Window.ContentRendered.Add(fun _ -> tabs.CurrAvaEdit.Focus() |> ignore )

        tabs.Fsi.OnRuntimeError.Add(fun _ ->  
            let w = win.Window // because it might be hidden manually, or not visible from the start ( e.g. current script is evaluated in Seff.Rhino)
            if w.Visibility <> Visibility.Visible || w.WindowState=WindowState.Minimized then  win.Window.Show() ) 

    member this.Config = config

    member this.Tabs= tabsAndLog.Tabs

    member this.Log = tabsAndLog.Log

    member this.StatuBar = statusBar

    member this.Menu = menu

    member this.Checker = Checker.GetOrCreate(config)

    member this.Fsi = tabs.Fsi

    member this.Window = win.Window

    member this.Commands = commands


