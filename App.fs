namespace Seff

open System
open System.Windows


module App =        

    /// mainWindowHandle: Pointer to main window(nativeInt), 
    /// hostName: a string for the name of the hosting App (will be used for settings file name an displayed in the Title Bar.
    /// Call seff.Window.Show() on the returned Seff object.    
    /// Use seff.Fsi.OnStarted and seff.Fsi.OnIsReady Events to implement undo and redo in host App.
    let runEditorHosted (mainWindowHandle, hostName) : Seff =
        let seff = Initialize.everything (Hosted hostName , [| |])        
        Interop.WindowInteropHelper(seff.Window).Owner <- mainWindowHandle
        //win.Show() // do in host instead, so that the host can control the window show time
        seff


    [< EntryPoint >]
    [< STAThread >] 
    let runEditorStandalone args : int =   
        let seff = Initialize.everything (Standalone,args)
        (new Application()).Run(seff.Window) 