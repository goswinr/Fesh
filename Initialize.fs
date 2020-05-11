namespace Seff

open System
open System.Windows
open Seff.Model
open Seff.FsService
open Seff.Views
open Seff.Config

[<RequireQualifiedAccess>]
module Initialize =    
    
    let views(context:AppRunContext, startupArgs:string[])=
        let log = new Log()        
        let config = new Config(log,context,startupArgs)
        log.AdjustToSettingsInConfig(config)

        let win = new Window(config)
        let tabs = new Tabs(config, win.Window)
        let tabsAndLog = new TabsAndLog(config, tabs, log, win)
        let fsi = Fsi(config)// TODO first start FS checker only then FSI
        let statusBar = StatusBar(fsi)
        let menu = Menu(config)

        // finish setting up window:
        win.Window.ContentRendered.Add(fun _ -> 
            //if not <| Tabs.Current.Editor.Focus() then log.PrintAppErrorMsg "Tabs.Current.Editor.Focus failed"  //or System.Windows.Input.FocusManager.SetFocusedElement(...) 
            
            log.PrintInfoMsg "* Time for loading and render main window: %s"  Timer.InstanceStartup.tocEx
            
            fsi.Initalize() // do late to be sure errors can print to log and dont get lost (Rhino has problems with FSI from  FCS 33.0.1 on)
            ) 
            
        win.Window.Closing.Add( fun e ->
            // first check for running FSI
            match fsi.AskIfCancellingIsOk () with 
            | NotEvaluating   -> ()
            | YesAsync        -> fsi.CancelIfAsync() 
            | Dont            -> e.Cancel <- true // dont close window   
            | NotPossibleSync -> () // still close despite running thread ??
            
            //second check for unsaved files:
            let canClose = tabs.AskIfClosingWindowIsOk() 
            if not canClose then e.Cancel <- true // dont close window  
            ) 
         

        win.Window.Background  <- menu.Bar.Background // call after setting up content, otherwise space next to tab headers is in an odd color
        win.Window.Content     <- Util.dockPanelVert(menu.Bar, tabsAndLog.Grid, statusBar.Bar)
        win.Window.InputBindings.AddRange (Commands.allShortCutKeyGestures ())

    let enviroment() =
        Timer.InstanceStartup.tic()             // optional timer for full init process
        Sync.installSynchronizationContext()    // do first

        let en_US = Globalization.CultureInfo.CreateSpecificCulture("en-US")        
        Globalization.CultureInfo.DefaultThreadCurrentCulture   <- en_US
        Globalization.CultureInfo.DefaultThreadCurrentUICulture <- en_US

        //--------------ERROR Handeling --------------------

        (* //TODO with this the app fails to start. why?
        Application.Current.DispatcherUnhandledException.Add(fun e ->  //exceptions generated on the UI thread
            if e <> null then 
                log.Print "Application.Current.DispatcherUnhandledException in main Thread: %A" e.Exception           
                e.Handled<- true) *)
        
        //catching unhandled exceptions generated from all threads running under the context of a specific application domain. 
        //https://dzone.com/articles/order-chaos-handling-unhandled
        //https://stackoverflow.com/questions/14711633/my-c-sharp-application-is-returning-0xe0434352-to-windows-task-scheduler-but-it        
        AppDomain.CurrentDomain.UnhandledException.AddHandler (  new UnhandledExceptionEventHandler( Seff.ProcessCorruptedState.Handler)) 

        //-------------WPF----------------

        Controls.ToolTipService.ShowOnDisabledProperty.OverrideMetadata(  typeof<Controls.Control>, new FrameworkPropertyMetadata(true)) // to still show-tooltip-when a button(or menu item )  is disabled-by-command //https://stackoverflow.com/questions/4153539/wpf-how-to-show-tooltip-when-button-disabled-by-command
        Controls.ToolTipService.ShowDurationProperty.OverrideMetadata(    typeof<DependencyObject>, new FrameworkPropertyMetadata(Int32.MaxValue))
        Controls.ToolTipService.InitialShowDelayProperty.OverrideMetadata(typeof<DependencyObject>, new FrameworkPropertyMetadata(50))


        Tabs.OnTabAdded.Add TabEvents.setUpForTab
        Tabs.OnTabChanged.Add (fun t -> textChanged (TabChanged , t) ) 
        