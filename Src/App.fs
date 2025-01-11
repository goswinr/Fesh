namespace Fesh

open System
open System.Windows
open Fesh.Config
open Velopack

module App =

    /// To statically access the currently running instance.
    /// For debugging only
    let mutable current = Unchecked.defaultof<Fesh>

    /// mainWindowHandle: Pointer to main window(nativeInt),
    /// hostName: a string for the name of the hosting App (will be used for settings file name an displayed in the Title Bar.)
    /// fsiCanRun: a function to check if evaluation of fsi is currently allowed
    /// Call fesh.Window.Show() on the returned Fesh object.
    /// Use fesh.Fsi.OnStarted and fesh.Fsi.OnIsReady Events to implement undo and redo in host App.
    let createEditorForHosting (host:HostedStartUpData) : Fesh =
        let fesh = Initialize.everything (Some host , [| |])
        current <- fesh
        if host.mainWindowHandel <> IntPtr.Zero then
            // so that the editor window opens and closes at the same time as the main host window:
            Interop.WindowInteropHelper(fesh.Window).Owner <- host.mainWindowHandel

        //win.Show() // do in host instead, so that the host can control the window show time
        fesh


    [< EntryPoint >]
    [< STAThread >]
    let runEditorStandalone (args: string []) : int =
        VelopackApp.Build().Run() //https://docs.velopack.io/getting-started/csharp
        let app  = Application() // do first so that pack Uris work
        let fesh = Initialize.everything (None, args)
        current <- fesh
        // app.Run(fesh.Window)

        try
            app.Run(fesh.Window)
        with e ->
            eprintfn $"Fesh Application.Run Error:\r\n{e}"
            1


