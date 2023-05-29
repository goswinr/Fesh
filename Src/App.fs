namespace Seff

open System
open System.Windows
open Seff.Config


module App = 

    /// To statically access the currently running instance. For debugging only
    let mutable current = Unchecked.defaultof<Seff>

    /// mainWindowHandle: Pointer to main window(nativeInt),
    /// hostName: a string for the name of the hosting App (will be used for settings file name an displayed in the Title Bar.
    /// fsiCanRun: a function to check if evaluation of fsi is currently allowed
    /// Call seff.Window.Show() on the returned Seff object.
    /// Use seff.Fsi.OnStarted and seff.Fsi.OnIsReady Events to implement undo and redo in host App.
    let createEditorForHosting (host:HostedStartUpData) : Seff =         
        let seff = Initialize.everything (Some host , [| |])        
        current <- seff
        if host.mainWindowHandel <> IntPtr.Zero then
            // so that the editor window opens and closes at the same time as the main host window:
            Interop.WindowInteropHelper(seff.Window).Owner <- host.mainWindowHandel

        //win.Show() // do in host instead, so that the host can control the window show time
        seff


    [< EntryPoint >]
    [< STAThread >]
    let runEditorStandalone (args: string []) : int = 
        let app  = Application() // do first so that pack Uris work
        let seff = Initialize.everything (None, args)
        current <- seff
        app.Run(seff.Window)

