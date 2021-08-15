namespace Seff.Views

open System
open System.Windows.Media.Imaging

open Seff.Config



/// A class holding the main WPF Window
/// Includes loading icon 
type Window (config:Config)= 
    
    let windowSettiengsFilename =   
        match config.Hosting.HostName with 
        |Some n -> "Seff." + n 
        |None   -> "Seff"

    let win = new FsEx.Wpf.PositionedWindow(windowSettiengsFilename)

    let mutable wasMax = false //indicating if the Window was in  Fullscreen mode before switching to temporary Log only fullscreeen

    do 
        if win.Settings.GetBool "WindowIsMax" false then
            wasMax <- true

        let plat = if Environment.Is64BitProcess then " - 64bit" else " - 32bit"
        win.Title       <- match config.Hosting.HostName with 
                           |None     -> "Seff | Scripting editor for fsharp"         + plat + " - " + Runtime.InteropServices.RuntimeInformation.FrameworkDescription 
                           |Some n   -> "Seff | Scripting editor for fsharp in " + n + plat + " - " + Runtime.InteropServices.RuntimeInformation.FrameworkDescription                
        
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
    member this.Window = win
    
    /// Indicating if the Window is in Fullscreen mode or minimized mode (not normal mode)
    member this.IsMinOrMax = win.IsMinOrMax      

    /// Indicating if the Window was in  Fullscreen mode before switching to temporary Log only fullscreeen
    member this.WasMax
       with get() = wasMax  
       and set(v) = wasMax <- v 


    