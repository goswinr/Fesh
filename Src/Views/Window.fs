namespace Seff.Views

open Seff
open Seff.Config
open System
open System.Windows
open System.Windows.Media.Imaging


/// A class holding the main WPF Window
/// Includes loading Icon and logic for saving and restoring size and position
type Window (config:Config)= 
    
    let win = new Windows.Window()    

    let log = config.Log

    let mutable setMaxAfterLoading = false

    let mutable isMinOrMax = false 
    
    let mutable wasMax = false //indicating if the Window was in  Fullscreen mode before switching to temporary Log only fullscreeen

    do       
        win.ResizeMode  <- ResizeMode.CanResize  
        
        let plat = if Environment.Is64BitProcess then " - 64bit" else " - 32bit"
        win.Title       <- match config.Hosting.HostName with 
                           |None     -> "Seff | Scripting editor for fsharp"         + plat
                           |Some n   -> "Seff | Scripting editor for fsharp in " + n + plat                
        
        try                 
            // Add the Icon at the top left of the window and in the status bar, musst be called  after loading window.
            // Media/logo.ico with Build action : "Resource"
            // (for the exe file icon in explorer use <Win32Resource>Media\logo.res</Win32Resource>  in fsproj )  
            let defaultUri = Uri("pack://application:,,,/Seff;component/Media/logo.ico")
            match config.Hosting.Logo with 
            |Some uri -> 
                try             win.Icon <- BitmapFrame.Create(uri)
                with  _ ->      win.Icon <- BitmapFrame.Create(defaultUri)
            |None -> 
                win.Icon <- BitmapFrame.Create(defaultUri) 
        with ex -> 
            log.PrintfnAppErrorMsg  "Failed to load Media/logo.ico from Application.ResourceStream : %A" ex 
              
        //-------------------------------------------------------------------------
        // -  all below code is for load and safe window location and size ---
        //-------------------------------------------------------------------------        
        
        
        win.WindowStartupLocation <- WindowStartupLocation.Manual
        let winTop    = config.Settings.GetFloat "WindowTop"    0.0
        let winLeft   = config.Settings.GetFloat "WindowLeft"   0.0 
        let winHeight = config.Settings.GetFloat "WindowHeight" 800.0
        let winWidth  = config.Settings.GetFloat "WindowWidth"  800.0

        //let maxW = float <| Array.sumBy (fun (sc:Forms.Screen) -> sc.WorkingArea.Width)  Forms.Screen.AllScreens  // neded for dual screens ?, needs wins.forms
        //let maxH = float <| Array.sumBy (fun (sc:Forms.Screen) -> sc.WorkingArea.Height) Forms.Screen.AllScreens //https://stackoverflow.com/questions/37927011/in-wpf-how-to-shift-a-win-onto-the-screen-if-it-is-off-the-screen/37927012#37927012
            
        let offTolerance = 25.0 // beeing 20 pixel off screen is still good enough for beeing on screen and beeing draggable

        let maxW = SystemParameters.VirtualScreenWidth   + offTolerance
        let maxH = SystemParameters.VirtualScreenHeight  + offTolerance // somehow a window docked on the right is 7 pix bigger than the screen ?? // TODO check dual screens !!
            
        win.Top <-     winTop 
        win.Left <-    winLeft 
        win.Height <-  winHeight
        win.Width <-   winWidth
        
        if config.Settings.GetBool "WindowIsMax" false then
            //win.WindowState <- WindowState.Maximized // always put is on first screen, do in loaded event instead
            setMaxAfterLoading <- true
            isMinOrMax  <- true
            wasMax <- true
        
        elif  winTop  < -offTolerance || winHeight + winTop  > maxH then 
            log.PrintfnAppErrorMsg "Could not restore previous Editor Window position:"
            log.PrintfnAppErrorMsg "winTopPosition: %.1f  + winHeight: %.1f  = %.1f that is bigger than maxH: %.1f + %.1f tolerance" winTop winHeight   ( winHeight + winTop ) SystemParameters.VirtualScreenHeight offTolerance
            win.WindowStartupLocation <- WindowStartupLocation.CenterScreen                
            win.Height <- 600.0                
            win.Width  <- 600.0

        elif winLeft < -offTolerance || winWidth  + winLeft > maxW then
            log.PrintfnAppErrorMsg "Could not restore previous Editor Window position:"
            log.PrintfnAppErrorMsg "winLeftPosition: %.1f  + winWidth: %.1f = %.1f that is bigger than maxW: %.1f + %.1f tolerance" winLeft winWidth ( winWidth +  winLeft) SystemParameters.VirtualScreenWidth offTolerance
            win.WindowStartupLocation <- WindowStartupLocation.CenterScreen
            win.Height <- 600.0                
            win.Width  <- 600.0
        
        //Turns out that we cannot maximize the window until it's loaded. 
        //http://mostlytech.blogspot.com/2008/01/maximizing-wpf-window-to-second-monitor.html
        win.Loaded.Add (fun _ -> if setMaxAfterLoading then win.WindowState <- WindowState.Maximized)
        
        win.LocationChanged.Add(fun e -> // occures for every pixel moved
            async{
                // normally the state change event comes after the location change event but before size changed. async sleep in LocationChanged prevents this
                do! Async.Sleep 200 // so that StateChanged event comes first
                if win.WindowState = WindowState.Normal &&  not isMinOrMax then 
                    if win.Top > -500. && win.Left > -500. then // to not save on minimizing on minimized: Top=-32000 Left=-32000 
                        config.Settings.SetFloatDelayed "WindowTop"  win.Top  89 // get float in statchange maximised needs to access this before 350 ms pass
                        config.Settings.SetFloatDelayed "WindowLeft" win.Left 95
                        config.Settings.Save ()
                        //log.PrintfnDebugMsg  "Location Changed: Top=%.0f Left=%.0f State=%A" win.Top win.Left win.WindowState
                }
                |> Async.StartImmediate
            )

        win.StateChanged.Add (fun e ->
            match win.WindowState with 
            | WindowState.Normal -> // because when Window is hosted in other App the restore from maximised does not remember the previous position automatically                
                win.Top <-     config.Settings.GetFloat "WindowTop"    0.0
                win.Left <-    config.Settings.GetFloat "WindowLeft"   0.0 
                win.Height <-  config.Settings.GetFloat "WindowHeight" 800.0
                win.Width <-   config.Settings.GetFloat "WindowWidth"  800.0
                config.Settings.SetBool  "WindowIsMax" false
                isMinOrMax <- false
                config.Settings.Save ()
                //log.PrintfnDebugMsg "Normal: State changed=%A Top=%.0f Left=%.0f Width=%.0f Height=%.0f" win.WindowState win.Top win.Left win.ActualWidth win.ActualHeight 

            | WindowState.Maximized ->
                // normally the state change event comes after the location change event but before size changed. async sleep in LocationChanged prevents this
                isMinOrMax  <- true
                config.Settings.SetBool  "WindowIsMax" true
                config.Settings.Save  ()    
                //log.PrintfnDebugMsg "Maximised: State changed=%A Top=%.0f Left=%.0f Width=%.0f Height=%.0f" win.WindowState win.Top win.Left win.ActualWidth win.ActualHeight 
                          

            |WindowState.Minimized ->                 
                isMinOrMax  <- true
                //log.PrintfnDebugMsg "Minimised: State changed=%A Top=%.0f Left=%.0f Width=%.0f Height=%.0f"  win.WindowState win.Top win.Left win.ActualWidth win.ActualHeight 
               
            |wch -> 
                log.PrintfnAppErrorMsg "unknown WindowState State change=%A" wch
                isMinOrMax  <- true
            )

        win.SizeChanged.Add (fun e -> // does no get trigger on maximising 
            if win.WindowState = WindowState.Normal &&  not isMinOrMax  then 
                config.Settings.SetFloatDelayed "WindowHeight" win.Height 89
                config.Settings.SetFloatDelayed "WindowWidth"  win.Width  95
                config.Settings.Save ()
                //log.PrintfnInfoMsg "Size Changed: State=%A Top=%.0f Left=%.0f Width=%.0f Height=%.0f" win.WindowState win.Top win.Left win.ActualWidth win.ActualHeight
            )
        
       
    member this.Window = win
    
    //indicating if the Window is in Fullscreen mode
    member this.IsMinOrMax = isMinOrMax
      

    //indicating if the Window was in  Fullscreen mode before switching to temporary Log only fullscreeen
    member this.WasMax 
        with get() = wasMax  
        and set(v) = wasMax <- v 


    