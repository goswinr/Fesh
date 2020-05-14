namespace Seff

open Seff.Views
open Seff.Config
open Seff.Editor

/// the main App holding all UI and interaction ellements
/// this is passed on to hosting apps
type Seff (win:Window,config:Config,tabsAndLog:TabsAndLog,statusBar:StatusBar, menu:Menu) =

    do
        win.Window.Content <- Util.dockPanelVert(menu.Bar , tabsAndLog.Grid , statusBar.Bar)        
    
    member this.Config = config

    member this.Tabs= tabsAndLog.Tabs

    member this.Log = tabsAndLog.Log

    member this.StatuBar = statusBar

    member this.Menu = menu

    member this.Checker = Checker.GetOrCreate(config)

    member this.Fsi = Fsi.GetOrCreate(config)

    member this.Window = win.Window 
