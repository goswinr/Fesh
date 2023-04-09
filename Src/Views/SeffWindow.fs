namespace Seff.Views

open System
open System.Windows.Media.Imaging
open System.Runtime.InteropServices
open Seff.Model
open Seff.Config

/// A class holding the main WPF Window
/// Includes loading icon
type SeffWindow (config:Config)= 

    let win =         
        let w = new FsEx.Wpf.PositionedWindow(config.RunContext.PositionedWindowSettingsFileInfo, ISeffLog.printError)
        IEditor.mainWindow <- w
        w
    
    let mutable wasMax = win.Settings.GetBool ("WindowIsMax", false) //indicating if the Window was in Full-screen mode before switching to temporary Log only full-screen

    // for Title Bar:  
    let appName =         
        match config.RunContext.HostName with
        |None     -> "Seff"          //, a Scripting editor for fsharp"        
        |Some n   -> "Seff for " + n //, a Scripting editor for fsharp in " + n

    let plat = 
        if Environment.Is64BitProcess then "64bit" else "32bit"
               
    let version = 
        let v = Reflection.Assembly.GetAssembly(typeof<ISeffLog>).GetName().Version
        $"v{v.Major}.{v.Minor}.{v.Revision}"  + if  v.MinorRevision <> 0s then $".{v.MinorRevision}" else ""

    let fscore  = 
        let v = [].GetType().Assembly.GetName().Version 
        $"Fsharp.Core {v.Major}.{v.Minor}.{v.Revision}"  + if  v.MinorRevision <> 0s then $".{v.MinorRevision}" else ""

    let frameW =
        let d = RuntimeInformation.FrameworkDescription
        let t = if d.EndsWith ".0" then d[..^2] else d
        $"{t}"  
    
    do   
        //Add Icon:
        try
            // Add the Icon at the top left of the window and in the status bar, musst be called  after loading window.
            // Media/logo.ico with Build action : "Resource"
            // (for the exe file icon in explorer use <Win32Resource>Media\logo.res</Win32Resource>  in fsproj, where the .res file contains the .ico file )
            let defaultUri = Uri("pack://application:,,,/Seff;component/Media/logo.ico")
            match config.RunContext.Logo with
            |Some uri ->
                try             win.Icon <- BitmapFrame.Create(uri)
                with  _ ->      win.Icon <- BitmapFrame.Create(defaultUri)
            |None ->
                win.Icon <- BitmapFrame.Create(defaultUri)
        with ex ->
            config.Log.PrintfnAppErrorMsg  "Failed to load Media/logo.ico from Application.ResourceStream : %A" ex

    /// The main WPF Window
    member this.Window : FsEx.Wpf.PositionedWindow = win //:> System.Windows.Window // cast to a FsEx.Wpf.PositionedWindow

    /// Indicating if the Window is in Full-screen mode or minimized mode (not normal mode)
    member this.IsMinOrMax = win.IsMinOrMax

    /// Indicating if the Window was in Full-screen mode before switching to temporary Log only full-screen
    member this.WasMax
        with get() = wasMax
        and set(v) = wasMax <- v

    member this.SetFileNameInTitle (fp:FilePath) =
        match fp with 
        |NotSet dummyName -> 
            let txt =  
                [
                dummyName
                appName 
                version
                plat 
                frameW 
                fscore                
                ] |> String.concat "  -  "
            win.Title <- txt         
        
        |Deleted fi |SetTo fi -> 
            let txt =  
                [
                fi.Name
                appName 
                version
                plat 
                frameW 
                fscore
                fi.DirectoryName
                ] |> String.concat "  -  "
            win.Title <- txt 
    
    
   



