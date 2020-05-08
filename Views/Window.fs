namespace Seff.Views

open Seff
open System
open System.Windows
open Seff.Model

/// A class holding the main WPF Window
/// Includes logic for saving and restoreing size and position
type Window (config:Config.Config)= 
    
    let win = new Windows.Window()    

    let Log = config.Log

    let mutable isMinOrMax = false // TODO test and then clean up

    //indicating if the Window was in  Fullscreen mode before switching to temporary Log only fullscreeen
    let mutable wasMax = false

    do       
        win.ResizeMode  <- ResizeMode.CanResize  
                
        win.Title       <- match config.AppDataLocation.Mode with Standalone -> "Seff | Scripting editor for fsharp"  | Hosted n ->  "Seff | Scripting editor for fsharp in " + n
                
        win.Loaded.Add(fun _ ->
            //---- load ICON ----
            // Add the Icon at the top left of the window and in the status bar,         
            // musst be called at later moment(eg. after loading).
            // (for the exe file icon in explorer use <Win32Resource>Media\LogoCursorTr.res</Win32Resource>  in fsproj )
            let uri = new Uri("pack://application:,,,/Seff;component/Media/LogoCursorTr.ico", UriKind.RelativeOrAbsolute) //Build action : "Resource"; Copy to ouput Dir: "Do not copy" 
            try  win.Icon <-  Media.Imaging.BitmapFrame.Create(Application.GetResourceStream(uri).Stream)
            with ex -> Log.PrintAppErrorMsg  "Failed to load Media/LogoCursorTr.ico from Application.ResourceStream : %A" ex 
            )    
      
        //----------------------------------------------
        // -  load and safe window location and size ---
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
                        //Log.Print  "%s Location Changed: Top=%.0f Left=%.0f State=%A" Time.nowStrMilli win.Top win.Left win.WindowState
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
                //Log.Print "Normal: %s State changed=%A Top=%.0f Left=%.0f Width=%.0f Height=%.0f" Time.nowStrMilli win.WindowState win.Top win.Left win.ActualWidth win.ActualHeight 

            | WindowState.Maximized ->
                // normally the state change event comes after the location change event but before size changed. async sleep in LocationChanged prevents this
                isMinOrMax  <- true
                config.Settings.SetBool  "WindowIsMax" true
                config.Settings.Save  ()    
                //Log.Print "Maximised: %s State changed=%A Top=%.0f Left=%.0f Width=%.0f Height=%.0f" Time.nowStrMilli win.WindowState win.Top win.Left win.ActualWidth win.ActualHeight 
                          

            |WindowState.Minimized ->                 
                isMinOrMax  <- true
                //Log.Print "Minimised: %s State changed=%A Top=%.0f Left=%.0f Width=%.0f Height=%.0f" Time.nowStrMilli win.WindowState win.Top win.Left win.ActualWidth win.ActualHeight 
               
            |wch -> 
                Log.PrintAppErrorMsg "unknown WindowState State change=%A" wch
                isMinOrMax  <- true
            )

        win.SizeChanged.Add (fun e ->
            if win.WindowState = WindowState.Normal &&  not isMinOrMax  then 
                config.Settings.SetFloatDelayed "WindowHeight" win.Height 89
                config.Settings.SetFloatDelayed "WindowWidth"  win.Width  95
                config.Settings.Save ()
                //Log.dlog (sprintf "%s Size Changed: Width=%.0f Height=%.0f State=%A" Time.nowStrMilli win.Width win.Height win.WindowState)
            )
        
       
    member this.Window = win
    
    //indicating if the Window is in Fullscreen mode
    member this.IsMinOrMax_
        with get() = config.Settings.GetBool "WindowIsMinOrMax" false 
        and set(v) = config.Settings.SetBool "WindowIsMinOrMax" v  //isMinOrMax <- v

    //indicating if the Window was in  Fullscreen mode before switching to temporary Log only fullscreeen
    member this.WasMax_ 
          // not using internal mutable so window can be declared at a later point and Log+Editor grid can access these values from config
          with get() = config.Settings.GetBool "WindowWasMax" false 
          and set(v) = config.Settings.SetBool "WindowWasMax" v  //wasMax <- v 


          
          (* done in Initialize function 
          win.ContentRendered.Add(fun _ -> 
              //if not <| Tabs.Current.Editor.Focus() then Log.PrintAppErrorMsg "Tabs.Current.Editor.Focus failed"  //or System.Windows.Input.FocusManager.SetFocusedElement(...)             
              Log.PrintInfoMsg "* Time for loading and render main window: %s"  Timer.InstanceStartup.tocEx            
              Fsi.Initalize() // do late to be sure errors can print to log and dont get lost (Rhino has problems with FSI from  FCS 33.0.1 on) ) 
              
          win.Closing.Add( fun e ->
              // first check for running FSI
              match Fsi.AskIfCancellingIsOk () with 
              | NotEvaluating   -> ()
              | YesAsync        -> Fsi.CancelIfAsync() 
              | Dont            -> e.Cancel <- true // dont close window   
              | NotPossibleSync -> () // still close despite running thread ??            
              //second check for unsaved files:
              let canClose = tabs.askIfClosingWindowIsOk(Tabs.AllTabs,Tabs.SaveAs) 
              if not canClose then e.Cancel <- true // dont close window  
              ) *)