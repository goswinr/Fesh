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
        let win = MainWindow.create(Array.empty)
        Interop.WindowInteropHelper(win).Owner <- mainWindowHandle 
        win.Title <- win.Title + " for " + hostName
        //win.Show() // do in host instead
        win

    /// this is the same as getHostedEditorWindow but with two functions to register beginn and end undo steps in your App.
    /// Arguments: Pointer to main window(nativeInt), 
    /// A string for the name of the hosting App (will be used for settings file name),
    /// Two functions to register beginn and end of undo steps in your App
    /// Call window.Show() on the returned window object.
    let runEditorHostedWithUndo (mainWindowHandle, hostName, beginUndo, endUndo) =
        HostUndoRedo.beginUndo <- beginUndo //TODO use events instead
        HostUndoRedo.endUndo   <- endUndo
        runEditorHosted  (mainWindowHandle,hostName)


    [< EntryPoint >]
    [< STAThread >] 
    let runEditorStandalone args =        
        Sync.syncContext <- Sync.installAndGetSynchronizationContext() // do first
        Config.setCurrentRunContext(Config.RunContext.Standalone)
        (new Application()).Run(MainWindow.create(args)) // Returns application's exit code.