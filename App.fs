namespace Seff

open System
open System.Windows
open Seff.Model

module App =        

    /// mainWindowHandle: Pointer to main window(nativeInt), 
    /// hostName: a string for the name of the hosting App (will be used for settings file name an displayed in the Title Bar.
    /// Call window.Show() on the returned Window object.
    /// Call Fsi.Initalize() to start it.
    /// Use Fsi.OnStarted and Fsi.OnIsReady Events to implement undo and redo in host App.
    //[< STAThread >] needed?
    let runEditorHosted (mainWindowHandle, hostName) : Window =
        let win = Initialize.enviroment (Hosted hostName , [| |])
        let win = MainWindow.create( [| |], false ) // delay FSI start after window.Show(). Since this might fail. so the errors can be seen in logg
        Interop.WindowInteropHelper(win).Owner <- mainWindowHandle
        //win.Show() // do in host instead, so that the host can control the window show time
        win


    [< EntryPoint >]
    [< STAThread >] 
    let runEditorStandalone args : int =   
        let win = Initialize.enviroment (Standalone,args)
        (new Application()).Run(win) 