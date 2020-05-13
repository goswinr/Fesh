namespace Seff

open System
open System.Windows


module App =        

    /// mainWindowHandle: Pointer to main window(nativeInt), 
    /// hostName: a string for the name of the hosting App (will be used for settings file name an displayed in the Title Bar.
    /// Call window.Show() on the returned Window object.
    /// Call Fsi.Initalize() to start it.
    /// Use Fsi.OnStarted and Fsi.OnIsReady Events to implement undo and redo in host App.
    //[< STAThread >] needed?
    let runEditorHosted (mainWindowHandle, hostName) : Window =
        let win = Initialize.everything (Hosted hostName , [| |])        
        Interop.WindowInteropHelper(win).Owner <- mainWindowHandle
        //win.Show() // do in host instead, so that the host can control the window show time
        win


    [< EntryPoint >]
    [< STAThread >] 
    let runEditorStandalone args : int =   
        let win = Initialize.everything (Standalone,args)
        (new Application()).Run(win) 