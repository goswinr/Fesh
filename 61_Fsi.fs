namespace Seff


open System
open System.Windows
open System.IO
open System.Threading
open FSharp.Compiler.Interactive.Shell


type internal ProcessCorruptedState =  
    [< Security.SecurityCritical; Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions >] //to handle AccessViolationException too //https://stackoverflow.com/questions/3469368/how-to-handle-accessviolationexception/4759831
    static member Handler (sender:obj) (e: UnhandledExceptionEventArgs) = 
            //Starting with the .NET Framework 4, this event is not raised for exceptions that corrupt the state of the process, 
            //such as stack overflows or access violations, unless the event handler is security-critical and has the HandleProcessCorruptedStateExceptionsAttribute attribute.
            //https://docs.microsoft.com/en-us/dotnet/api/system.appdomain.unhandledexception?redirectedfrom=MSDN&view=netframework-4.8
            let err = sprintf "AppDomain.CurrentDomain.UnhandledException: isTerminating: %b : %A" e.IsTerminating e.ExceptionObject
            Util.fileLoggingAgent.Post(err)
            Log.print "%s" err

module Fsi =    
    
    type States = Ready|Evaluating
    type Mode   = Sync |Async

    [<AbstractClass; Sealed>]
    /// A static class to hold FSI events 
    type Events internal () =
        
        static let startedEv        = new Event<Mode>() 
        static let runtimeErrorEv   = new Event<Exception>() 
        static let canceledEv       = new Event<Mode>() 
        static let completedEv      = new Event<Mode>()
        static let isReadyEv        = new Event<Mode>()         

        ///The event that can be triggered  
        static member started = startedEv        
        ///Triggered whenever code is sent to Fsi for evaluation
        [<CLIEvent>]
        static member Started = startedEv.Publish

        ///The event that can be triggered  
        static member runtimeError = runtimeErrorEv        
        /// Interactive evaluation was cancelled because of a runtime error
        [<CLIEvent>]
        static member RuntimeError = runtimeErrorEv.Publish
        
        ///The event that can be triggered        
        static member canceled = canceledEv        
        /// Interactive evaluation was cancelled by user (e.g. by pressing Esc)
        [<CLIEvent>]
        static member Canceled = canceledEv.Publish

        ///The event that can be triggered
        static member completed = completedEv        
        /// This event will be trigger after succesfull completion, NOT on runtime error or cancelling of Fsi
        [<CLIEvent>]
        static member Completed = completedEv.Publish

        ///The event that can be triggered
        static member isReady  = isReadyEv         
        /// This event will be trigger after completion, runtime error or cancelling of Fsi
        [<CLIEvent>]
        static member IsReady  = isReadyEv .Publish

        

    let mutable state = Ready
    let mutable mode =  Sync
    

    let mutable private session:FsiEvaluationSession option = None 
    
    let private startSession () =     
        (*
                - INPUT FILES -
        --use:<file>                             Use the given file on startup as initial input
        --load:<file>                            #load the given file on startup
        --reference:<file>                       Reference an assembly (Short form: -r)
        --compilertool:<file>                    Reference an assembly or directory containing a design time tool (Short form: -t)
        -- ...                                   Treat remaining arguments as command line arguments, accessed using fsi.CommandLineArgs

                - CODE GENERATION -
        --debug[+|-]                             Emit debug information (Short form: -g)
        --debug:{full|pdbonly|portable|embedded} Specify debugging type: full, portable, embedded, pdbonly. ('pdbonly' is the default if no debuggging type specified and
                                            enables attaching a debugger to a running program, 'portable' is a cross-platform format, 'embedded' is a cross-platform
                                            format embedded into the output file).
        --optimize[+|-]                          Enable optimizations (Short form: -O)
        --tailcalls[+|-]                         Enable or disable tailcalls
        --deterministic[+|-]                     Produce a deterministic assembly (including module version GUID and timestamp)
        --pathmap:<path=sourcePath;...>          Maps physical paths to source path names output by the compiler
        --crossoptimize[+|-]                     Enable or disable cross-module optimizations

                - ERRORS AND WARNINGS -
        --warnaserror[+|-]                       Report all warnings as errors
        --warnaserror[+|-]:<warn;...>            Report specific warnings as errors
        --warn:<n>                               Set a warning level (0-5)
        --nowarn:<warn;...>                      Disable specific warning messages
        --warnon:<warn;...>                      Enable specific warnings that may be off by default
        --consolecolors[+|-]                     Output warning and error messages in color

                - LANGUAGE -
        --langversion:{?|version|latest|preview} Display the allowed values for language version, specify language version such as 'latest' or 'preview'
        --checked[+|-]                           Generate overflow checks
        --define:<string>                        Define conditional compilation symbols (Short form: -d)
        --mlcompatibility                        Ignore ML compatibility warnings

                - MISCELLANEOUS -
        --nologo                                 Suppress compiler copyright message
        --help                                   Display this usage message (Short form: -?)

                - ADVANCED -
        --codepage:<n>                           Specify the codepage used to read source files
        --utf8output                             Output messages in UTF-8 encoding
        --preferreduilang:<string>               Specify the preferred output language culture name (e.g. es-ES, ja-JP)
        --fullpaths                              Output messages with fully qualified paths
        --lib:<dir;...>                          Specify a directory for the include path which is used to resolve source files and assemblies (Short form: -I)
        --simpleresolution                       Resolve assembly references using directory-based rules rather than MSBuild resolution
        --targetprofile:<string>                 Specify target framework profile of this assembly. Valid values are mscorlib, netcore or netstandard. Default - mscorlib
        --noframework                            Do not reference the default CLI assemblies by default
        --exec                                   Exit fsi after loading the files or running the .fsx script given on the command line
        --gui[+|-]                               Execute interactions on a Windows Forms event loop (on by default)
        --quiet                                  Suppress fsi writing to stdout
        --readline[+|-]                          Support TAB completion in console (on by default)
        --quotations-debug[+|-]                  Emit debug information in quotations
        --shadowcopyreferences[+|-]              Prevents references from being locked by the F# Interactive process
        *)
        //async{
            //do! Async.SwitchToThreadPool()
            //do! Async.SwitchToContext Sync.syncContext
            let timer = Util.Timer()
            timer.tic()
            //if session.IsSome then session.Value.Interrupt()  //TODO does this cancel it Ok ??         
            
            let inStream = new StringReader("")
            // first arg is ignored: https://github.com/fsharp/FSharp.Compiler.Service/issues/420 and https://github.com/fsharp/FSharp.Compiler.Service/issues/877 and  https://github.com/fsharp/FSharp.Compiler.Service/issues/878            
            let allArgs = [|"" ; "--langversion:preview" ; "--noninteractive" ; "--debug+"; "--debug:full" ;"--optimize+" ;"--nologo"; "--gui-"|] // ; "--shadowcopyreferences" is ignored https://github.com/fsharp/FSharp.Compiler.Service/issues/292           
            let fsiConfig = FsiEvaluationSession.GetDefaultConfiguration() // https://github.com/dotnet/fsharp/blob/4978145c8516351b1338262b6b9bdf2d0372e757/src/fsharp/fsi/fsi.fs#L2839
            let fsiSession = FsiEvaluationSession.Create(fsiConfig, allArgs, inStream, Log.textwriter, Log.textwriter) //, collectible=false ??) //TODO add error logger window
            AppDomain.CurrentDomain.UnhandledException.Add(fun ex -> Log.print "*** FSI background exception:\r\n %A" ex.ExceptionObject)
            Console.SetOut  (Log.textwriter) //needed to redirect printfn ? //https://github.com/fsharp/FSharp.Compiler.Service/issues/201
            Console.SetError(Log.textwriter) //TODO needed if evaluate non throwing ? 
            //if mode = Mode.Sync then do! Async.SwitchToContext Sync.syncContext            
            //fsiSession.Run()// dont do this it crashes the app when hosted in Rhino! 
            if session.IsNone then Log.print "* Time for loading FSharp Interactive: %s"  timer.tocEx
            session <- Some fsiSession
            match mode with
            |Sync ->  Log.print "*FSharp Interactive will evaluate synchronously on UI Thread."
            |Async -> Log.print "*FSharp Interactive will evaluate asynchronously on new Thread."            //}
            timer.stop()
    
    // to be able to cancel running Fsi eval
    let mutable private thread :Thread option = None
    //let mutable private fsiCancelScr :CancellationTokenSource option = None



    [< Security.SecurityCritical; Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions>] //to handle AccessViolationException too //https://stackoverflow.com/questions/3469368/how-to-handle-accessviolationexception/4759831
    let private eval(code)=
        state <- Evaluating
        //fsiCancelScr <- Some (new CancellationTokenSource())
        //UI.log.Background <- Appearance.logBackgroundFsiEvaluating //do in event below
        Events.started.Trigger(mode) // do always sync
        if session.IsNone then  startSession()     // sync 
        
        let thr = new Thread(fun () ->
            let a = 
                async{
                    if mode = Mode.Sync then do! Async.SwitchToContext Sync.syncContext 
                    
                    Application.Current.DispatcherUnhandledException.Add(fun e ->  //exceptions generated on the UI thread
                        Log.print "Application.Current.DispatcherUnhandledException in fsi thread: %A" e.Exception        
                        e.Handled <- true)        
       
                    AppDomain.CurrentDomain.UnhandledException.AddHandler (//catching unhandled exceptions generated from all threads running under the context of a specific application domain. //https://dzone.com/articles/order-chaos-handling-unhandled
                        new UnhandledExceptionEventHandler( ProcessCorruptedState.Handler)) //https://stackoverflow.com/questions/14711633/my-c-sharp-application-is-returning-0xe0434352-to-windows-task-scheduler-but-it

                    let choice, errs =  
                        try session.Value.EvalInteractionNonThrowing(code)//,fsiCancelScr.Value.Token)   // cancellation token here fails to cancel in sync, might still throw OperationCanceledException if async       
                        with e -> Choice2Of2 e , [| |]
            
                    if mode = Mode.Async then do! Async.SwitchToContext Sync.syncContext 
            
                    thread <- None
                    state <- Ready //TODO reached when canceled ?                     
            
                    match choice with //TODO move out ?
                    |Choice1Of2 vo -> 
                        Events.completed.Trigger(mode)
                        Events.isReady.Trigger(mode)
                        for e in errs do Log.print "****Why Error: %A" e
                        //match vo with None-> () |Some v -> Log.print "Interaction evaluted to %A <%A>" v.ReflectionValue v.ReflectionType
                
                    |Choice2Of2 exn ->     
                        match exn with 
                        | :? OperationCanceledException ->
                            Events.canceled.Trigger(mode)
                            Events.isReady.Trigger(mode)
                            Log.print "**Fsi evaluation was canceled: %s" exn.Message                    
                        | :? FsiCompilationException -> 
                            Events.runtimeError.Trigger(exn)
                            Events.isReady.Trigger(mode)
                            Log.print "Compiler Error:"
                            for e in errs do    
                                Log.print "%A" e
                        | _ ->    
                            Events.runtimeError.Trigger(exn)
                            Events.isReady.Trigger(mode)
                            Log.print "Runtime Error: %A" exn     } 
            
            Async.StartImmediate(a)// cancellation token here fails to cancel evaluation,
            )
        
        thread<-Some thr
        
        thr.Start()

     
    //-------------- public interface: ---------

    type IsCancelingOk = NotEvaluating | YesAsync | Dont | NotPossibleSync // Not-Possible-Sync because during sync eval the ui should be frozen anyway and this request should not be happening
    

    let cancelIfAsync() = 
        match state  with 
        | Ready -> ()
        | Evaluating -> 
            match mode with
            |Sync ->() //don't block event completion by doing some debug logging //Log.print "Current synchronous Fsi Interaction cannot be canceled"     
            |Async ->                
                match thread with 
                |None ->() // Log.print "**No thread to cancel Fsi, should never happen !" 
                |Some thr -> 
                    thread<-None
                    state<-Ready 
                    thr.Abort() // raises OperationCanceledException  

    
    let askIfCancellingIsOk() = 
        match state with 
        | Ready -> NotEvaluating      
        | Evaluating ->
            match mode with 
            |Sync -> NotPossibleSync
            |Async ->
                match MessageBox.Show("Do you want to Cancel currently running code?", "Cancel Current Evaluation?", MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.No) with
                | MessageBoxResult.Yes -> 
                    match state with // might have changed in the meantime of Messagebox show
                    | Ready -> NotEvaluating      
                    | Evaluating -> YesAsync            
                | _  -> 
                    match state with // might have changed in the meantime of Messagebox show
                    | Ready -> NotEvaluating      
                    | Evaluating -> Dont  
    

    ///shows UI to confirm cancelling, returns new state
    let askAndCancel() =
        match askIfCancellingIsOk () with 
        | NotEvaluating   -> Ready
        | YesAsync        -> cancelIfAsync();Ready
        | Dont            -> Evaluating
        | NotPossibleSync -> Evaluating       
        

    let evaluate(code) =         
        if DateTime.Today > DateTime(2020, 9, 30) then failwithf "Your Seff Editor has expired, please download a new version."
        if DateTime.Today > DateTime(2020, 7, 30) then Log.print "*** Your Seff Editor will expire on 2020-9-30, please download a new version soon. ***"        
        match askIfCancellingIsOk () with 
        | NotEvaluating   -> eval(code) 
        | YesAsync        -> cancelIfAsync();eval(code) 
        | Dont            -> ()
        | NotPossibleSync -> Log.print "Wait till current synchronous evaluation completes before starting new one."
       
    let clearLog() =         
        UI.log.Clear()
        UI.log.Background <- Appearance.logBackgroundFsiReady // clearing log should remove red error color too.

    let reset() =  
        match askIfCancellingIsOk () with 
        | NotEvaluating   -> clearLog(); startSession ()
        | YesAsync        -> cancelIfAsync(); UI.log.Clear(); startSession ()
        | Dont            -> ()
        | NotPossibleSync -> UI.log.Clear(); startSession () // Log.print "Wait till current synchronous evaluation completes before reseting."
      

    let setMode(sync:Mode) =         
        let setConfig()=
            match mode with
            |Sync ->
                StatusBar.asyncDesc.Content <- "Synchronous in UI Thread"  
                Config.Settings.setBool "asyncFsi" false
                          
            |Async ->                 
                StatusBar.asyncDesc.Content <- "Asynchronous" 
                Config.Settings.setBool "asyncFsi" true                
        

        match askIfCancellingIsOk() with 
        | NotEvaluating | YesAsync    -> 
            mode <- sync
            setConfig()
            Config.Settings.Save()
            startSession ()
        | Dont | NotPossibleSync -> () //Log.print "Wait till current synchronous evaluation completes before seting mode."
    
    let toggleSync()=
        match mode with
        |Async ->  setMode Sync
        |Sync ->   setMode Async      

   

    do
        //Events.Canceled.Add        (fun _ -> Log.print " +Fsi Canceled+")
        //Events.IsReady.Add         (fun _ -> Log.print " +Fsi isReady+")      
        //Events.Started.Add         (fun _ -> Log.print " +Fsi Started+")
        //Events.Completed.Add       (fun _ -> Log.print " +Fsi Completed+")

        Events.RuntimeError.Add    (fun _  -> Log.print " +Fsi RuntimeError+")
        
        Events.RuntimeError.Add (fun _  -> UI.log.Background <- Appearance.logBackgroundFsiHadError)
        Events.Started.Add      (fun _ -> UI.log.Background <- Appearance.logBackgroundFsiEvaluating) // happens at end of eval in sync mode
        Events.Completed.Add    (fun _ -> UI.log.Background <- Appearance.logBackgroundFsiReady)
        Events.Canceled.Add     (fun _ -> UI.log.Background <- Appearance.logBackgroundFsiReady)

        StatusBar.asyncDesc.MouseDown.Add(fun _ -> toggleSync())
        
        

