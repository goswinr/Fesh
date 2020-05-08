namespace Seff

open System
open System.Windows
open Seff.Model
open Seff.FsService


[<RequireQualifiedAccess>]
module Initialize =    
    
    let newWay(context:AppRunContext, startupArgs:string[])=
        let log = Views.Log()        
        let config = Config.Config(log,context)
        log.ApplyConfig(config)



    let enviroment(context:AppRunContext, startupArgs:string[]) =
        Timer.InstanceStartup.tic()             // optional timer for full init process
        Sync.installSynchronizationContext()    // do first

        // http://fsharp.github.io/FSharp.Compiler.Service/caches.html
        // https://github.com/fsharp/FSharp.Compiler.Service/blob/71272426d0e554e0bac32ad349bbd9f5fa8a3be9/src/fsharp/service/service.fs#L35
        Environment.SetEnvironmentVariable ("FCS_ParseFileCacheSize", "5") 
        
        let en_US = Globalization.CultureInfo.CreateSpecificCulture("en-US")        
        Globalization.CultureInfo.DefaultThreadCurrentCulture   <- en_US
        Globalization.CultureInfo.DefaultThreadCurrentUICulture <- en_US

        //--------------ERROR Handeling --------------------

        (* //TODO with this the app fails to start. why?
        Application.Current.DispatcherUnhandledException.Add(fun e ->  //exceptions generated on the UI thread
            if e <> null then 
                Log.Print "Application.Current.DispatcherUnhandledException in main Thread: %A" e.Exception           
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
        //------ Seff Views----------------
        
        Log.Initialize()                        // do second so it can be used in Config already
        Config.Initialize(context)              // do third so settings are loaded from file and  availabe 
        Win.Initialize(startupArgs)