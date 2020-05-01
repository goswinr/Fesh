namespace Seff

open System
open System.Windows
open Seff.Model

/// A Static class holding the main WPF Window
type Win private ()= 
    
    static let win = new Window()    

    static let mutable isMinOrMax = false

    static let mutable wasMax = false

    static member Window = win
    
    //indicating if the Window is in Fullscreen mode
    static member IsMinOrMax = isMinOrMax
    
    //indicating if the Window was in  Fullscreen mode before switching to temporary Log only fullscreeen
    static member WasMax 
        with get() = wasMax 
        and set(v) = wasMax <- v

    /// loads window size and position from last run and sets up events to save window state in Config
    static member Initialize() :Window =
        
        win.Title       <- match Config.Context.Mode with Standalone -> "Seff | Scripting editor for fsharp"  | Hosted n ->  "Seff | Scripting editor for fsharp in " + n
        win.ResizeMode  <- ResizeMode.CanResize  

        //---- load ICON ----
        win.Loaded.Add(fun _ ->
            // Add the Icon at the top left of the window and in the status bar,         
            // musst be called at later moment(eg. after loading).
            // (for the exe file icon in explorer use <Win32Resource>Media\Logo15.res</Win32Resource>  in fsproj )
            let uri = new Uri("pack://application:,,,/Seff;component/Media/Logo15.ico", UriKind.RelativeOrAbsolute) //Build action : "Resource"; Copy to ouput Dir: "Do not copy" 
            try  win.Icon <-  Media.Imaging.BitmapFrame.Create(Application.GetResourceStream(uri).Stream)
            with ex -> Log.PrintAppErrorMsg  "Failed to load Media/Logo15.ico from Application.ResourceStream : %A" ex)


        //----------------------------------------------
        // -  load and safe window location and size ---
        //----------------------------------------------
        
        if Config.Settings.getBool "WindowIsMax" false then
            win.WindowState <- WindowState.Maximized
            isMinOrMax <- true
            wasMax <- true
        else
            win.WindowStartupLocation <- WindowStartupLocation.Manual
            //let maxW = float <| Array.sumBy (fun (sc:Forms.Screen) -> sc.WorkingArea.Width)  Forms.Screen.AllScreens  // neded for dual screens ?, needs wins.forms
            //let maxH = float <| Array.sumBy (fun (sc:Forms.Screen) -> sc.WorkingArea.Height) Forms.Screen.AllScreens //https://stackoverflow.com/questions/37927011/in-wpf-how-to-shift-a-win-onto-the-screen-if-it-is-off-the-screen/37927012#37927012
    
            let maxW = SystemParameters.VirtualScreenWidth   + 8.0
            let maxH = SystemParameters.VirtualScreenHeight  + 8.0 // somehow a window docked on the right is 7 pix bigger than the screen ??
            win.Top <-     Config.Settings.getFloat "WindowTop"    0.0
            win.Left <-    Config.Settings.getFloat "WindowLeft"   0.0 
            win.Height <-  Config.Settings.getFloat "WindowHeight" 800.0
            win.Width <-   Config.Settings.getFloat "WindowWidth"  800.0
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
                        Config.Settings.setFloatDelayed "WindowTop"  win.Top  89 // get float in statchange maximised needs to access this before 350 ms pass
                        Config.Settings.setFloatDelayed "WindowLeft" win.Left 95
                        Config.Settings.Save ()
                        //Log.Print  "%s Location Changed: Top=%.0f Left=%.0f State=%A" Time.nowStrMilli win.Top win.Left win.WindowState
                }
                |> Async.StartImmediate
            )

        win.StateChanged.Add (fun e ->
            match win.WindowState with 
            | WindowState.Normal -> // because when Window is hosted in other App the restore from maximised does not remember the previous position automatically                
                win.Top <-     Config.Settings.getFloat "WindowTop"    0.0
                win.Left <-    Config.Settings.getFloat "WindowLeft"   0.0 
                win.Height <-  Config.Settings.getFloat "WindowHeight" 800.0
                win.Width <-   Config.Settings.getFloat "WindowWidth"  800.0
                Config.Settings.setBool  "WindowIsMax" false
                isMinOrMax <- false
                Config.Settings.Save ()
                //Log.Print "Normal: %s State changed=%A Top=%.0f Left=%.0f Width=%.0f Height=%.0f" Time.nowStrMilli win.WindowState win.Top win.Left win.ActualWidth win.ActualHeight 

            | WindowState.Maximized ->
                // normally the state change event comes after the location change event but before size changed. async sleep in LocationChanged prevents this
                isMinOrMax <- true
                Config.Settings.setBool  "WindowIsMax" true
                Config.Settings.Save  ()    
                //Log.Print "Maximised: %s State changed=%A Top=%.0f Left=%.0f Width=%.0f Height=%.0f" Time.nowStrMilli win.WindowState win.Top win.Left win.ActualWidth win.ActualHeight 
                          

            |WindowState.Minimized ->                 
                isMinOrMax <- true
                //Log.Print "Minimised: %s State changed=%A Top=%.0f Left=%.0f Width=%.0f Height=%.0f" Time.nowStrMilli win.WindowState win.Top win.Left win.ActualWidth win.ActualHeight 
               
            |wch -> 
                Log.Print "unknown WindowState State change=%A" wch
                isMinOrMax <- true
            )

        win.SizeChanged.Add (fun e ->
            if win.WindowState = WindowState.Normal &&  not isMinOrMax then 
                Config.Settings.setFloatDelayed "WindowHeight" win.Height 89
                Config.Settings.setFloatDelayed "WindowWidth"  win.Width  95
                Config.Settings.Save ()
                //Log.dlog (sprintf "%s Size Changed: Width=%.0f Height=%.0f State=%A" Time.nowStrMilli win.Width win.Height win.WindowState)
            )
        
        win
       
