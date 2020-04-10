namespace Seff

open System
open System.Windows
open Seff.Util
open Seff.Model

module App =    
    

    /// mainWindowHandle: Pointer to main window(nativeInt), 
    /// hostName: a string for the name of the hosting App (will be used for settings file name an displayed in the Title Bar.
    /// Call window.Show() on the returned window object.
    let runEditorHosted (mainWindowHandle, hostName) =
        Sync.installSynchronizationContext() // do first
        Config.initialize (Hosted hostName)
        let win = MainWindow.create(Array.empty)
        Interop.WindowInteropHelper(win).Owner <- mainWindowHandle 
        win.Title <- win.Title + " for " + hostName
        //win.Show() // do in host instead
        win


    [< EntryPoint >]
    [< STAThread >] 
    let runEditorStandalone args =        
        Sync.installSynchronizationContext() // do first
        Config.initialize (Standalone)
        let win = MainWindow.create(args)
        (new Application()).Run(win) 