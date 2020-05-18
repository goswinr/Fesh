namespace Seff

open Seff.Views
open Seff.Config
open Seff.Editor
open System.Windows

/// the main App holding all UI and interaction ellements
/// this is passed on to hosting apps
type Seff (win:Views.Window,config:Config,tabsAndLog:TabsAndLog,statusBar:StatusBar, menu:Menu, cmds:Commands) =

    do
        let dockP = Util.dockPanelVert(menu.Bar , tabsAndLog.Grid , statusBar.Bar)   
        dockP.Margin <- Thickness(4.)
        win.Window.Content <-   dockP   
        

    member this.Config = config

    member this.Tabs= tabsAndLog.Tabs

    member this.Log = tabsAndLog.Log

    member this.StatuBar = statusBar

    member this.Menu = menu

    member this.Checker = Checker.GetOrCreate(config)

    member this.Fsi = Fsi.GetOrCreate(config)

    member this.Window = win.Window 

    member this.Commands = cmds