namespace Fesh

open Avalonia
open Avalonia.Controls
open System
open Fittings.DependencyProps

open Fesh.Views
open Fesh.Config
open Fesh.Editor
open System.Threading.Tasks

//#nowarn "44" // for AppDomain.GetCurrentThreadId()

/// the main App holding all UI and interaction elements
/// this is passed on to hosting apps
type Fesh (config:Config,log:Log) =

    let feshWin = new FeshWindow(config)
    let win = feshWin.Window

    let tabs = new Tabs(config, log, feshWin)
    let tabsAndLog = new TabsAndLog(config, tabs, log, feshWin)

    let statusBar = FeshStatusBar tabsAndLog
    let commands = Commands(tabsAndLog, statusBar)
    let menu = Menu(config, commands, tabs, statusBar, log)
    let dockP = dockPanelVert(menu.Bar , tabsAndLog.Grid , statusBar.Bar)

    do
        //dockP.Margin <- Thickness tabsAndLog.GridSplitterSize // TODO why?

        commands.SetUpGestureInputBindings()

        // win.AllowDrop <- true // so it works on tab bar
        // win.Drop.Add (fun e -> DragAndDrop.onTabHeaders(tabs.AddFiles, e)) // text editor has it own drag event, this applies to all other area ( eg log, tab bar) except the editor (see handler)

        win.Content     <- dockP

        //if config.RunContext.IsStandalone then win.Window.ContentRendered.Add(fun _ -> log.PrintfnInfoMsg "* Time for loading and rendering of main window: %s"  Timer.InstanceStartup.tocEx)

        let mutable closingNeedsConfirmation = true
        win.Closing.Add( fun (e:WindowClosingEventArgs) ->
            if closingNeedsConfirmation then
                e.Cancel <- true
                task{
                    // first check for running FSI
                    match! tabs.Fsi.AskIfCancellingIsOk() with
                    | UserDoesntWantTo->  () // don't close window
                    | NotEvaluating
                    | NotPossibleSync
                    | YesAsync        ->
                        tabs.Fsi.CancelIfAsync()
                        //second check for unsaved files if not already canceled
                        match! tabs.AskForFileSavingToKnowIfClosingWindowIsOk() with
                        | false -> () // don't close window
                        | true  ->
                            closingNeedsConfirmation <- false // so that this event handler doesn't get triggered again
                            win.Close() // close the window
                }
                |> ignore<Task<unit>>
            )

        win.Closed.Add(fun _ ->
            tabs.Fsi.TriggerShutDownThreadEv() )// to clean up threads

        // WPF: win.ContentRendered.Add(fun _ -> tabs.CurrAvaEdit.Focus() |> ignore )
        // When the Opened event is raised the editor of the current tab is not part of the visual tree yet, so it can't take the focus.
        // Posting it with a low priority runs it after the first layout pass.
        win.Opened.Add(fun _ ->
            Threading.Dispatcher.UIThread.Post(
                (fun () -> tabs.CurrAvaEdit.TextArea.Focus() |> ignore), // the TextEditor itself is not focusable in AvaloniaEdit, only its TextArea
                Threading.DispatcherPriority.Background))

        // tabs.Fsi.OnRuntimeError.Add(fun _ ->
        //     let w = win // because it might be hidden manually, or not visible from the start ( e.g. current script is evaluated in Fesh.Rhino)
        //     if w.Visibility <> Visibility.Visible || w.WindowState=WindowState.Minimized then win.Show() )
        // tabs.Fsi.OnFsiEvalError.Add(fun _ ->
        //     let w = win // because it might be hidden manually, or not visible from the start ( e.g. current script is evaluated in Fesh.Rhino)
        //     if w.Visibility <> Visibility.Visible || w.WindowState=WindowState.Minimized then win.Show() )


    member this.Config = config

    member this.Tabs= tabsAndLog.Tabs

    member this.Log = tabsAndLog.Log

    member this.StatusBar = statusBar

    member this.Menu = menu
    member this.Fsi = tabs.Fsi

    member this.Window = win

    member this.Commands = commands


