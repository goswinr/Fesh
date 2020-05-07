namespace Seff

open System
open System.Windows
open Seff.Model
open System.Windows.Controls
open Seff.Util

/// A Static class holding the main WPF Window
/// Includes logic for saving and restoreing size and position
type Win private ()= 
    
    static let win = new Window()    

    //static let mutable isMinOrMax = false // TODO test and then clean up

    //indicating if the Window was in  Fullscreen mode before switching to temporary Log only fullscreeen
    //static let mutable wasMax = false

    static member Window = win
    
    //indicating if the Window is in Fullscreen mode
    static member IsMinOrMax 
        with get() = Config.Settings.getBool "WindowIsMinOrMax" false 
        and set(v) = Config.Settings.setBool "WindowIsMinOrMax" v  //isMinOrMax <- v

    //indicating if the Window was in  Fullscreen mode before switching to temporary Log only fullscreeen
    static member WasMax 
        // not using internal mutable so window can be declared at a later point and Log+Editor grid can access these values from config
        with get() = Config.Settings.getBool "WindowWasMax" false 
        and set(v) = Config.Settings.setBool "WindowWasMax" v  //wasMax <- v

    /// loads window size and position from last run and sets up events to save window state in Config
    static member Initialize()  =
        Tabs.MainWindow <- win
        TabsAndLog.MainWindow <- win
        win.ResizeMode  <- ResizeMode.CanResize  
        
        win.Content     <- WPF.dockPanelVert(Menu.Bar, TabsAndLog.Grid, StatusBar.Bar)
        
        win.Background  <- Menu.Bar.Background // call after setting up content, otherwise space next to tab headers is in an odd color
        win.Title       <- match Config.Context.Mode with Standalone -> "Seff | Scripting editor for fsharp"  | Hosted n ->  "Seff | Scripting editor for fsharp in " + n
        
        
        win.Loaded.Add(fun _ ->
            //---- load ICON ----
            // Add the Icon at the top left of the window and in the status bar,         
            // musst be called at later moment(eg. after loading).
            // (for the exe file icon in explorer use <Win32Resource>Media\LogoCursorTr.res</Win32Resource>  in fsproj )
            let uri = new Uri("pack://application:,,,/Seff;component/Media/LogoCursorTr.ico", UriKind.RelativeOrAbsolute) //Build action : "Resource"; Copy to ouput Dir: "Do not copy" 
            try  win.Icon <-  Media.Imaging.BitmapFrame.Create(Application.GetResourceStream(uri).Stream)
            with ex -> Log.PrintAppErrorMsg  "Failed to load Media/LogoCursorTr.ico from Application.ResourceStream : %A" ex            
            )    
        
        win.ContentRendered.Add(fun _ -> 
            //if not <| Tabs.Current.Editor.Focus() then Log.PrintAppErrorMsg "Tabs.Current.Editor.Focus failed"  //or System.Windows.Input.FocusManager.SetFocusedElement(...) 
            
            Log.PrintInfoMsg "* Time for loading and render main window: %s"  Timer.InstanceStartup.tocEx
            
            Fsi.Initalize() // do late to be sure errors can print to log and dont get lost (Rhino has problems with FSI from  FCS 33.0.1 on)
            ) 
            
        win.Closing.Add( fun e ->
            // first check for running FSI
            match Fsi.AskIfCancellingIsOk () with 
            | NotEvaluating   -> ()
            | YesAsync        -> Fsi.CancelIfAsync() 
            | Dont            -> e.Cancel <- true // dont close window   
            | NotPossibleSync -> () // still close despite running thread ??
            
            //second check for unsaved files:
            let canClose = FileDialogs.askIfClosingWindowIsOk(Tabs.AllTabs,Tabs.SaveAs) 
            if not canClose then e.Cancel <- true // dont close window  
            ) 
                
         

        //----------------------------------------------
        // -  load and safe window location and size ---
        //----------------------------------------------
        
        if Config.Settings.getBool "WindowIsMax" false then
            win.WindowState <- WindowState.Maximized
            Win.IsMinOrMax  <- true
            Win.WasMax <- true
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
                if win.WindowState = WindowState.Normal &&  not Win.IsMinOrMax then 
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
                Win.IsMinOrMax <- false
                Config.Settings.Save ()
                //Log.Print "Normal: %s State changed=%A Top=%.0f Left=%.0f Width=%.0f Height=%.0f" Time.nowStrMilli win.WindowState win.Top win.Left win.ActualWidth win.ActualHeight 

            | WindowState.Maximized ->
                // normally the state change event comes after the location change event but before size changed. async sleep in LocationChanged prevents this
                Win.IsMinOrMax  <- true
                Config.Settings.setBool  "WindowIsMax" true
                Config.Settings.Save  ()    
                //Log.Print "Maximised: %s State changed=%A Top=%.0f Left=%.0f Width=%.0f Height=%.0f" Time.nowStrMilli win.WindowState win.Top win.Left win.ActualWidth win.ActualHeight 
                          

            |WindowState.Minimized ->                 
                Win.IsMinOrMax  <- true
                //Log.Print "Minimised: %s State changed=%A Top=%.0f Left=%.0f Width=%.0f Height=%.0f" Time.nowStrMilli win.WindowState win.Top win.Left win.ActualWidth win.ActualHeight 
               
            |wch -> 
                Log.PrintAppErrorMsg "unknown WindowState State change=%A" wch
                Win.IsMinOrMax  <- true
            )

        win.SizeChanged.Add (fun e ->
            if win.WindowState = WindowState.Normal &&  not Win.IsMinOrMax  then 
                Config.Settings.setFloatDelayed "WindowHeight" win.Height 89
                Config.Settings.setFloatDelayed "WindowWidth"  win.Width  95
                Config.Settings.Save ()
                //Log.dlog (sprintf "%s Size Changed: Width=%.0f Height=%.0f State=%A" Time.nowStrMilli win.Width win.Height win.WindowState)
            )
        
       
       
