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

    let mutable isMinOrMax = false 
    
    let mutable wasMax = false //indicating if the Window was in  Fullscreen mode before switching to temporary Log only fullscreeen

    do       
        win.ResizeMode  <- ResizeMode.CanResize  
        
        let plat = if Environment.Is64BitProcess then " - 64bit" else " - 32bit"
        win.Title       <- match config.Hosting.HostName with 
                           |None     -> "Seff | Scripting editor for fsharp"         + plat
                           |Some n   -> "Seff | Scripting editor for fsharp in " + n + plat
                
        // delete if no bug//win.Loaded.Add(fun _ -> //---- load ICON ----
        try                 
            // Add the Icon at the top left of the window and in the status bar, musst be called at later moment(eg. after loading).
            // (for the exe file icon in explorer use <Win32Resource>Media\LogoCursorTr.res</Win32Resource>  in fsproj )
            // TODO delete if no bug //let uri = new Uri("pack://application:,,,/Seff;component/Media/LogoCursorTr.ico", UriKind.RelativeOrAbsolute) 
            // delete if no bug //win.Icon <-  Media.Imaging.BitmapFrame.Create(Application.GetResourceStream(uri).Stream)
            //win.Icon <-  BitmapFrame.Create(Uri("pack://application:,,,/Media/LogoCursorTr.ico"))//Build action : "Resource"
            win.Icon <-  BitmapFrame.Create(Uri("pack://application:,,,/Seff;component/Media/LogoCursorTr.ico")) // so that it works hosted in other dlls too?
        with ex -> 
            log.PrintfnAppErrorMsg  "Failed to load Media/LogoCursorTr.ico from Application.ResourceStream : %A" ex 
        //)    
      
        //----------------------------------------------
        // -  all below code is for load and safe window location and size ---
        //----------------------------------------------
        
        if config.Settings.GetBool "WindowIsMax" false then
            win.WindowState <- WindowState.Maximized
            isMinOrMax  <- true
            wasMax <- true
        else
            win.WindowStartupLocation <- WindowStartupLocation.Manual
            //let maxW = float <| Array.sumBy (fun (sc:Forms.Screen) -> sc.WorkingArea.Width)  Forms.Screen.AllScreens  // neded for dual screens ?, needs wins.forms
            //let maxH = float <| Array.sumBy (fun (sc:Forms.Screen) -> sc.WorkingArea.Height) Forms.Screen.AllScreens //https://stackoverflow.com/questions/37927011/in-wpf-how-to-shift-a-win-onto-the-screen-if-it-is-off-the-screen/37927012#37927012
    
            let maxW = SystemParameters.VirtualScreenWidth   + 8.0
            let maxH = SystemParameters.VirtualScreenHeight  + 8.0 // somehow a window docked on the right is 7 pix bigger than the screen ??
            win.Top <-     config.Settings.GetFloat "WindowTop"    0.0
            win.Left <-    config.Settings.GetFloat "WindowLeft"   0.0 
            win.Height <-  config.Settings.GetFloat "WindowHeight" 800.0
            win.Width <-   config.Settings.GetFloat "WindowWidth"  800.0
            if  win.Top  < -8. || win.Height + win.Top  > maxH || // verify window fits screen (second screen might be off)
                win.Left < -8. || win.Width  + win.Left > maxW then                    
                    win.Top <-   0.0 ; win.Height <- 600.0
                    win.Left <-  0.0 ; win.Width  <- 800.0

        win.LocationChanged.Add(fun e -> // occures for every pixel moved
            async{
                // normally the state change event comes after the location change event but before size changed. async sleep in LocationChanged prevents this
                do! Async.Sleep 200 // so that StateChanged event comes first
                if win.WindowState = WindowState.Normal &&  not isMinOrMax then 
                    if win.Top > -500. && win.Left > -500. then // to not save on minimizing on minimized: Top=-32000 Left=-32000 
                        config.Settings.SetFloatDelayed "WindowTop"  win.Top  89 // get float in statchange maximised needs to access this before 350 ms pass
                        config.Settings.SetFloatDelayed "WindowLeft" win.Left 95
                        config.Settings.Save ()
                        //log.PrintfnDebugMsg  "%s Location Changed: Top=%.0f Left=%.0f State=%A" Time.nowStrMilli win.Top win.Left win.WindowState
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
                //log.PrintfnDebugMsg "Normal: %s State changed=%A Top=%.0f Left=%.0f Width=%.0f Height=%.0f" Time.nowStrMilli win.WindowState win.Top win.Left win.ActualWidth win.ActualHeight 

            | WindowState.Maximized ->
                // normally the state change event comes after the location change event but before size changed. async sleep in LocationChanged prevents this
                isMinOrMax  <- true
                config.Settings.SetBool  "WindowIsMax" true
                config.Settings.Save  ()    
                //log.PrintfnDebugMsg "Maximised: %s State changed=%A Top=%.0f Left=%.0f Width=%.0f Height=%.0f" Time.nowStrMilli win.WindowState win.Top win.Left win.ActualWidth win.ActualHeight 
                          

            |WindowState.Minimized ->                 
                isMinOrMax  <- true
                //log.PrintfnDebugMsg "Minimised: %s State changed=%A Top=%.0f Left=%.0f Width=%.0f Height=%.0f" Time.nowStrMilli win.WindowState win.Top win.Left win.ActualWidth win.ActualHeight 
               
            |wch -> 
                log.PrintfnAppErrorMsg "unknown WindowState State change=%A" wch
                isMinOrMax  <- true
            )

        win.SizeChanged.Add (fun e ->
            if win.WindowState = WindowState.Normal &&  not isMinOrMax  then 
                config.Settings.SetFloatDelayed "WindowHeight" win.Height 89
                config.Settings.SetFloatDelayed "WindowWidth"  win.Width  95
                config.Settings.Save ()
                //log.dlog (sprintf "%s Size Changed: Width=%.0f Height=%.0f State=%A" Time.nowStrMilli win.Width win.Height win.WindowState)
            )
        
       
    member this.Window = win
    
    //indicating if the Window is in Fullscreen mode
    member this.IsMinOrMax = isMinOrMax
      

    //indicating if the Window was in  Fullscreen mode before switching to temporary Log only fullscreeen
    member this.WasMax 
        with get() = wasMax  
        and set(v) = wasMax <- v 


    