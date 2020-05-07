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
        Initialize.enviroment (Hosted hostName , [| |])        
        Interop.WindowInteropHelper(Win.Window).Owner <- mainWindowHandle
        //win.Show() // do in host instead, so that the host can control the window show time
        Win.Window


    [< EntryPoint >]
    [< STAThread >] 
    let runEditorStandalone args : int =   
        Initialize.enviroment (Standalone,args)
        (new Application()).Run(Win.Window) 