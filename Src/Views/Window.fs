namespace Seff.Views

open System
open System.Windows.Media.Imaging
open System.Runtime.InteropServices
open Seff.Model
open Seff.Config
open Seff.Util


/// A class holding the main WPF Window
/// Includes loading icon
type SeffWindow (config:Config)= 

    let win = new FsEx.Wpf.PositionedWindow(config.Hosting.SettingsFileInfo,ISeffLog.printError)

    let mutable wasMax = false //indicating if the Window was in Full-screen mode before switching to temporary Log only full-screen

    do
        if win.Settings.GetBool ("WindowIsMax", false) then
            wasMax <- true
        
        // Set Title Bar:  
        let name =         
            match config.Hosting.HostName with
            |None     -> "Seff  |  Scripting editor for fsharp"        
            |Some n   -> "Seff  |  Scripting editor for fsharp in " + n

        let plat = 
            if Environment.Is64BitProcess then "  |  64bit" else "  |  32bit"
               
        let version = 
            let v = Reflection.Assembly.GetAssembly(typeof<ISeffLog>).GetName().Version
            $"  |  v {v.Major}.{v.Minor}.{v.Revision}"  + if  v.MinorRevision <> 0s then $".{v.MinorRevision}" else ""

        let fscore  = 
            let v = [].GetType().Assembly.GetName().Version 
            $"  |  Fsharp.Core {v.Major}.{v.Minor}.{v.Revision}"  + if  v.MinorRevision <> 0s then $".{v.MinorRevision}" else ""

        let frameW =
            let d = RuntimeInformation.FrameworkDescription
            let t = if d.EndsWith ".0" then d[..^2] else d
            $"  |  {t}" 

        win.Title <- name + version + plat + frameW + fscore
             

        //Add Icon:
        try
            // Add the Icon at the top left of the window and in the status bar, musst be called  after loading window.
            // Media/logo.ico with Build action : "Resource"
            // (for the exe file icon in explorer use <Win32Resource>Media\logo.res</Win32Resource>  in fsproj, where the .res file contains the .ico file )
            let defaultUri = Uri("pack://application:,,,/Seff;component/Media/logo.ico")
            match config.Hosting.Logo with
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



