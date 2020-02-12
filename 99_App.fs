namespace Seff

open System
open System.Windows
open Seff.Util


module App =    


    /// Arguments: Pointer to main window(nativeInt), 
    /// a string for the name of the hosting App (will be used for settings file name).
    /// Call window.Show() on the returned window object.
    let runEditorHosted (mainWindowHandle, hostName) =
        Sync.syncContext <- Sync.installAndGetSynchronizationContext() // do first
        Config.hostName <- hostName // do before Config.setCurrentRunContext(..)
        Config.setCurrentRunContext(Config.RunContext.Hosted)
        Config.loadCompletionStats()
        let win = MainWindow.create(Array.empty)
        Interop.WindowInteropHelper(win).Owner <- mainWindowHandle 
        win.Title <- win.Title + " for " + hostName
        //win.Show() // do in host instead
        win


    [< EntryPoint >]
    [< STAThread >] 
    let runEditorStandalone args =        
        Sync.syncContext <- Sync.installAndGetSynchronizationContext() // do first
        Config.setCurrentRunContext(Config.RunContext.Standalone)
        Config.loadCompletionStats()
        (new Application()).Run(MainWindow.create(args)) // Returns application's exit code.