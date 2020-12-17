namespace Seff


open System
open System.Windows
open System.IO
open System.Threading
open FSharp.Compiler.Interactive.Shell
open Seff.Model
open Seff.Config
open Seff.Util
open System.Windows.Media



type Fsi private (config:Config) =    
    let log = config.Log
    
    ///FSI events
    let startedEv        = new Event<CodeToEval>()      
    let runtimeErrorEv   = new Event<Exception>() 
    let canceledEv       = new Event<unit>() 
    let completedOkEv    = new Event<unit>()
    let isReadyEv        = new Event<unit>()
    let resetEv          = new Event<unit>()
    let modeChangedEv    = new Event<FsiMode>()
    
    let mutable state = NotLoaded

    let mutable mode =  Async
    
    let mutable sessionOpt :FsiEvaluationSession option = None 

    let mutable thread : Thread option = None


    let mutable currentDir = ""
    let mutable currentFile = ""
    let mutable currentTopLine = 1    
    let setDir (session:FsiEvaluationSession) (fi:FileInfo) = 
        try
            let dir = fi.DirectoryName
            if dir <> currentDir then 
                let cd = sprintf "# silentCd @\"%s\" \n ;;" dir
                session.EvalInteraction(cd)//does it work ? see https://github.com/fsharp/FSharp.Compiler.Service/issues/957
                currentDir <- dir
                log.PrintfnInfoMsg "Current directory set to:%s" dir
            else
                log.PrintfnDebugMsg  "Current directory is already set to:%s" dir   
        with e->            
            log.PrintfnFsiErrorMsg "silentCD on FSI failed: %A" e 
    
    let setFileAndLine (session:FsiEvaluationSession) (topLine:int) (fi:FileInfo) = 
        try
            let file = fi.FullName
            if file  <> currentFile || currentTopLine <> topLine then 
                let ln = sprintf "# %d @\"%s\"  \n ;;" topLine file // then \n before the ;; is required somehow.
                session.EvalInteraction(ln) //does it work ? see https://github.com/fsharp/FSharp.Compiler.Service/issues/957
                if file  <> currentFile then 
                    log.PrintfnInfoMsg "Current line set to %d , file set to:%s" topLine file
                currentFile <- file
                currentTopLine <- topLine
            else
                log.PrintfnDebugMsg  "Current line and file and is already set to Line %d for:%s" topLine file    
        with e->
            log.PrintfnFsiErrorMsg "setFileAndLine on FSI failed: %A" e 
             
    [< Security.SecurityCritical >] // TODO do these Attributes appy in to async thread too ?
    [< Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions >] //to handle AccessViolationException too //https://stackoverflow.com/questions/3469368/how-to-handle-accessviolationexception/4759831
    let init() = 
        match state with 
        | Initalizing -> log.PrintfnInfoMsg "FSI initialization can't be started because it is already in process.."
        | NotLoaded | Ready | Evaluating -> 
            let  prevState = state
            state <- Initalizing
            async{
                //let timer = Seff.Timer()
                //timer.tic()
                if config.Settings.GetBool "asyncFsi" true then mode <- Async else mode <- FsiMode.Sync
                match sessionOpt with 
                |None -> ()
                |Some session -> session.Interrupt()  //TODO does this cancel running session correctly ?? // TODO how to dispose previous session ?        
                        
          
                let inStream = new StringReader("")
                // first arg is ignored: https://github.com/fsharp/FSharp.Compiler.Service/issues/420 
                // and  https://github.com/fsharp/FSharp.Compiler.Service/issues/877 
                // and  https://github.com/fsharp/FSharp.Compiler.Service/issues/878            
                    
                let allArgs = 
                        // "--shadowcopyreferences" is ignored https://github.com/fsharp/FSharp.Compiler.Service/issues/292
                    if config.Settings.GetBool Settings.keyFsiQuiet false then Array.append  config.FsiArugments.Get [| "--quiet"|] // TODO or fsi.ShowDeclarationValues <- false ??
                    else                                                                     config.FsiArugments.Get
                        
                let settings = Settings.fsi
                // Default: https://github.com/dotnet/fsharp/blob/c0d6f6abbf14a19c631cd647b6440ec2c63c668f/src/fsharp/fsi/fsi.fs#L3244
                // evLoop = (new SimpleEventLoop() :> IEventLoop)
                // showIDictionary = true
                // showDeclarationValues = true
                // args = Environment.GetCommandLineArgs()
                // fpfmt = "g10"
                // fp = (CultureInfo.InvariantCulture :> System.IFormatProvider)
                // printWidth = 78
                // printDepth = 100
                // printLength = 100
                // printSize = 10000
                // showIEnumerable = true
                // showProperties = true
                // addedPrinters = []
                settings.PrintWidth <- 200 //TODO adapt to Log view size taking fontsize into account
                settings.FloatingPointFormat <- "g7"
                let fsiConfig = FsiEvaluationSession.GetDefaultConfiguration(settings,false) // https://github.com/dotnet/fsharp/blob/4978145c8516351b1338262b6b9bdf2d0372e757/src/fsharp/fsi/fsi.fs#L2839                    
                let fsiSession = FsiEvaluationSession.Create(fsiConfig, allArgs, inStream, log.TextWriterFsiStdOut, log.TextWriterFsiErrorOut) //, collectible=false ??) //https://github.com/dotnet/fsharp/blob/6b0719845c928361e63f6e38a9cce4ae7d621fbf/src/fsharp/fsi/fsi.fs#L2440
                                        
                //AppDomain.CurrentDomain.UnhandledException.AddHandler (new UnhandledExceptionEventHandler( (new ProcessCorruptedState(config)).Handler)) //Add(fun ex -> log.PrintfnFsiErrorMsg "*** FSI AppDomain.CurrentDomain.UnhandledException:\r\n %A" ex.ExceptionObject)
                Console.SetOut  (log.TextWriterConsoleOut)   // TODO needed to redirect printfn or coverd by TextWriterFsiStdOut? //https://github.com/fsharp/FSharp.Compiler.Service/issues/201
                Console.SetError(log.TextWriterConsoleError) // TODO needed if evaluate non throwing or coverd by TextWriterFsiErrorOut? 
                //if mode = Mode.Sync then do! Async.SwitchToContext Sync.syncContext            
                //fsiSession.Run() // TODO ? dont do this it crashes the app when hosted in Rhino! 
                state <- Ready
                sessionOpt <- Some fsiSession
                //timer.stop()
                if prevState = NotLoaded then () //log.PrintfnInfoMsg "FSharp Interactive session created in %s"  timer.tocEx  
                else                          log.PrintfnInfoMsg "FSharp Interactive session reset." // in %s" timer.tocEx     
            
                if config.Hosting.IsHosted then 
                    match mode with
                    |Sync ->  log.PrintfnInfoMsg "FSharp Interactive will evaluate synchronously on UI Thread."
                    |Async -> log.PrintfnInfoMsg "FSharp Interactive will evaluate asynchronously on new Thread."    
                //fsiSession.AssemblyReferenceAdded.Add (config.AssemblyReferenceStatistic.Add)  //TODO fails in FCS 37.0.0                  
                do! Async.SwitchToContext Sync.syncContext 
                currentDir <- ""
                currentFile <- ""
                currentTopLine <- 1 
                isReadyEv.Trigger()
                } |> Async.Start
    


    [< Security.SecurityCritical >] // TODO do these Attributes appy in to async thread too ?
    [< Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions >] //to handle AccessViolationException too //https://stackoverflow.com/questions/3469368/how-to-handle-accessviolationexception/4759831
    let eval(code:CodeToEval)=
      if not (String.IsNullOrWhiteSpace code.code) then   
        if not config.Hosting.FsiCanRun then 
            log.PrintfnAppErrorMsg "The Hosting App has blocked Fsi from Running, maybe because the App is busy in another command or task."
        else
            match sessionOpt with 
            |None -> 
                init()
                log.PrintfnFsiErrorMsg "Please wait till FSI is initalized for running scripts"
            |Some session ->
                state <- Evaluating                
                startedEv.Trigger(code) // do always sync

                //TODO https://github.com/dotnet/fsharp/blob/6b0719845c928361e63f6e38a9cce4ae7d621fbf/src/fsharp/fsi/fsi.fs#L2618
                // change via reflection??? 
                // let dummyScriptFileName = "input.fsx"

                let asyncEval = async{
                    if mode = FsiMode.Sync then 
                        do! Async.Sleep 40 // this helps to show "FSI is running" immediatly
                        do! Async.SwitchToContext Sync.syncContext                         
                
                    //Done already at startup in Initalize.fs, not neded here?
                    //if notNull Application.Current then // null if application is not yet created, or no application in hosted context
                    //    Application.Current.DispatcherUnhandledException.Add(fun e ->  //exceptions generated on the UI thread // TODO realy do this on every evaluataion?
                    //        log.PrintfnAppErrorMsg "Application.Current.DispatcherUnhandledException in fsi thread: %A" e.Exception        
                    //        e.Handled <- true) 
                    //AppDomain.CurrentDomain.UnhandledException.AddHandler (//catching unhandled exceptions generated from all threads running under the context of a specific application domain. //https://dzone.com/articles/order-chaos-handling-unhandled
                    //    new UnhandledExceptionEventHandler( (new ProcessCorruptedState(config)).Handler)) //https://stackoverflow.com/questions/14711633/my-c-sharp-application-is-returning-0xe0434352-to-windows-task-scheduler-but-it
            

                    // set current dir, file and Topline TODO
                    // TODO https://github.com/dotnet/fsharp/blob/6b0719845c928361e63f6e38a9cce4ae7d621fbf/src/fsharp/fsi/fsi.fs#L2618
                    // change via reflection??? 
                    // let dummyScriptFileName = "input.fsx"

                    match code.file with 
                    | NotSet -> () //setFileAndLine session code.fromLine "Unnamed File"
                    | SetTo fi -> 
                        //setDir session fi
                        //setFileAndLine session code.fromLine fi // TODO both fail ??
                        ()

                    let choice, errs =  
                        try session.EvalInteractionNonThrowing(code.code) //,fsiCancelScr.Value.Token)   // cancellation token here fails to cancel in sync, might still throw OperationCanceledException if async       
                        with e -> Choice2Of2 e , [| |]
               
                    if mode = Async then 
                        do! Async.SwitchToContext Sync.syncContext 
               
                    thread <- None
                    state <- Ready //TODO reached when canceled ?                     
               
                    match choice with //TODO move out of Thread?
                    |Choice1Of2 vo -> 
                        completedOkEv.Trigger()
                        isReadyEv.Trigger()
                        for e in errs do log.PrintfnAppErrorMsg "EvalInteractionNonThrowing should not have errors, but this happend: %A" e
                        //match vo with None-> () |Some v -> log.PrintfnDebugMsg "Interaction evaluted to %A <%A>" v.ReflectionValue v.ReflectionType
                   
                    |Choice2Of2 exn ->     
                        match exn with 
                        | :? OperationCanceledException ->
                            canceledEv.Trigger()
                            isReadyEv.Trigger()
                            if config.Hosting.IsHosted && mode = FsiMode.Async && isNull exn.StackTrace  then 
                                log.PrintfnFsiErrorMsg "FSI evaluation was canceled,\r\nif you did not trigger this cancellation try running FSI in Synchronos evaluation mode (instead of Async)."    
                            else 
                                log.PrintfnFsiErrorMsg "FSI evaluation was canceled by user!" //:\r\n%A" exn.StackTrace  //: %A" exn                
                           
                        | :? FsiCompilationException -> 
                            runtimeErrorEv.Trigger(exn)
                            isReadyEv.Trigger()
                            log.PrintfnFsiErrorMsg "Compiler Error:"
                            let mutable postMsg = ""
                            for e in errs do    
                                let msg = sprintf "%A" e
                                if msg.Contains "is defined in an assembly that is not referenced." then 
                                    postMsg <- 
                                        "Fix:\r\n" + 
                                        "  For assembly refrence errors that are not shown by editor tooling try to re-arrange the inlital loading sequens of '#r' statements\n\r" +
                                        "  This error might happen when you are loading a dll with #r that is already loaded, but from a diffrent location\n\r" +
                                        "  E.G. as a dependency from a already loaded dll."
                                log.PrintfnFsiErrorMsg "%A" e
                            if postMsg <> "" then 
                                log.PrintfnCustomColor 0 0 200 "%s" postMsg

                        | _ ->    
                            runtimeErrorEv.Trigger(exn)
                            isReadyEv.Trigger()
                            log.PrintfnFsiErrorMsg "Runtime Error:" 
                        
                            //highlight line number:
                            let et = sprintf "%A" exn
                            let t,r = String.splitOnce ".fsx:" et
                            if r="" then 
                                log.PrintfnFsiErrorMsg "%s" et
                            else
                                let ln,rr = String.splitOnce "\r\n" r                        
                                log.PrintfFsiErrorMsg "%s.fsx:" t
                                log.PrintfnCustomColor 0 0 200 "%s" ln
                                log.PrintfnFsiErrorMsg "%s" rr
                              
                    } 
        
                //TODO trigger from a new thread even in Synchronous evaluation ?
                let thr = new Thread(fun () -> 
                    // a cancellation token here fails to cancel evaluation.
                    // dsyme: Thread.Abort - it is needed in interruptible interactive execution scenarios: https://github.com/dotnet/fsharp/issues/9397#issuecomment-648376476
                    // Thread.Abort method is not supported in .NET 5 (including .NET Core)
                    //https://github.com/dotnet/runtime/issues/41291
                    Async.StartImmediate(asyncEval))  
                thread <- Some thr           
                if mode = Async then thr.SetApartmentState(ApartmentState.STA) //TODO always ok ? needed to run WPF? https://stackoverflow.com/questions/127188/could-you-explain-sta-and-mta
                thr.Start()
    
    
    static let mutable singleInstance:Fsi option  = None
    
    /// ensures only one instance is created
    static member GetOrCreate(config:Config) :Fsi = 
        match singleInstance with 
        |Some fsi -> fsi
        |None -> singleInstance <- Some (new Fsi(config)); singleInstance.Value
        
        //match config.Hosting.Mode with
        //|Hosted h when h= "Revit" ->
        //    config.Log.PrintfnInfoMsg "Fsi in Sync only mode for Revit"
        //    FsiSync.GetOrCreate(config)
        //| _ -> 
        //    match singleInstance with 
        //    |Some fsi -> fsi
        //    |None -> singleInstance <- Some (new Fsi(config)); singleInstance.Value

    //-------------- public interface: ---------

    member this.State = state

    member this.Mode = mode

    /// starts a new Fsi session 
    member this.Initalize() =  init() // Checker class will call this after first run of checker, to start fsi when checker is  idle
    
    member this.CancelIfAsync() = 
        match state  with 
        | Ready | Initalizing | NotLoaded -> ()
        | Evaluating -> 
            match mode with
            |Sync ->() //don't block event completion by doing some debug logging. TODO test how to log !//log.PrintfnInfoMsg "Current synchronous Fsi Interaction cannot be canceled"     // UI for this only available in asynchronous mode anyway, see Commands  
            |Async ->                
                match thread with 
                |None ->() 
                |Some thr -> 
                    thread<-None
                    state<-Ready 
                    thr.Abort() // raises OperationCanceledException  
                    // Thread.Abort method is not supported in .NET 5 (including .NET Core)
                    // https://github.com/dotnet/runtime/issues/41291
                    // dsyme: Thread.Abort - it is needed in interruptible interactive execution scenarios: https://github.com/dotnet/fsharp/issues/9397#issuecomment-648376476

    member this.AskIfCancellingIsOk() = 
        match state with 
        | Ready | Initalizing | NotLoaded -> NotEvaluating      
        | Evaluating ->
            match mode with 
            |Sync -> NotPossibleSync
            |Async ->
                match MessageBox.Show("Do you want to Cancel currently running code?", "Cancel Current Evaluation?", MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.No) with
                | MessageBoxResult.Yes -> 
                    match state with // might have changed in the meantime of Messagebox show
                    | Ready | Initalizing | NotLoaded -> NotEvaluating      
                    | Evaluating -> YesAsync            
                | _  -> 
                    match state with // might have changed in the meantime of Messagebox show
                    | Ready | Initalizing | NotLoaded -> NotEvaluating      
                    | Evaluating -> Dont  
    

    ///shows UI to confirm cancelling, returns new state
    member this.AskAndCancel() =
        match this.AskIfCancellingIsOk () with 
        | NotEvaluating   -> Ready
        | YesAsync        -> this.CancelIfAsync();Ready
        | Dont            -> Evaluating
        | NotPossibleSync -> Evaluating     // UI for this only available in asynchronous mode anyway, see Commands  
    
    member this.Evaluate(code) =         
        //if DateTime.Today > DateTime(2020, 12, 30) then log.PrintfnFsiErrorMsg "*** Your Seff Editor has expired, please download a new version. or contact goswin@rothenthal.com ***"
        if DateTime.Today > DateTime(2021, 06, 30) then log.PrintfnFsiErrorMsg "Seff Exception %A" (NullReferenceException().GetType())
        else 
            //if DateTime.Today > DateTime(2021, 03, 30) then log.PrintfnFsiErrorMsg "*** Your Seff Editor will expire on 2020-12-31, please download a new version soon. or contact goswin@rothenthal.com***"        
            match this.AskIfCancellingIsOk () with 
            | NotEvaluating   -> eval(code) 
            | YesAsync        -> this.CancelIfAsync();eval(code) 
            | Dont            -> ()
            | NotPossibleSync -> log.PrintfnInfoMsg "Wait till current synchronous evaluation completes before starting new one."
     

    member this.Reset() =  
        match this.AskIfCancellingIsOk () with 
        | NotEvaluating   ->                       init (); resetEv.Trigger() //log.PrintfnInfoMsg "FSI reset." done by this.Initialize()
        | YesAsync        -> this.CancelIfAsync(); init (); resetEv.Trigger()
        | Dont            -> ()
        | NotPossibleSync -> log.PrintfnInfoMsg "ResetFsi is not be possibe in current synchronous evaluation." // TODO test
      

    member this.SetMode(sync:FsiMode) =         
        let setConfig()=
            match mode with
            |Sync ->  config.Settings.SetBool "asyncFsi" false                          
            |Async -> config.Settings.SetBool "asyncFsi" true   

        match this.AskIfCancellingIsOk() with 
        | NotEvaluating | YesAsync    -> 
            mode <- sync
            modeChangedEv.Trigger(sync)
            setConfig()
            config.Settings.Save()
            init ()
        | Dont -> () 
        | NotPossibleSync -> log.PrintfnInfoMsg "Wait till current synchronous evaluation completes before seting mode to Async."
    
    member this.ToggleSync()=
        match mode with
        |Async ->  this.SetMode FsiMode.Sync
        |Sync ->   this.SetMode FsiMode.Async      
          

    ///Triggered whenever code is sent to Fsi for evaluation
    [<CLIEvent>]
    member this.OnStarted = startedEv.Publish

    /// Interactive evaluation was cancelled because of a runtime error
    [<CLIEvent>]
    member this.OnRuntimeError = runtimeErrorEv.Publish
             
    /// Interactive evaluation was cancelled by user (e.g. by pressing Esc)
    [<CLIEvent>]
    member this.OnCanceled = canceledEv.Publish
 
    /// This event will  trigger after succesfull completion, NOT on runtime error or cancelling of Fsi
    [<CLIEvent>]
    member this.OnCompletedOk = completedOkEv.Publish
      
    /// This event will trigger on the end of each fsi session, 
    /// so after completion, runtime error or cancelling of Fsi
    [<CLIEvent>]
    member this.OnIsReady  = isReadyEv .Publish
     
    /// This event will be trigger after Fsi is reset
    [<CLIEvent>]
    member this.OnReset  = resetEv.Publish   
    
    ///Triggered whenever Fsi for evaluation mode changes between Sync and Async
    [<CLIEvent>]
    member this.OnModeChanged = modeChangedEv.Publish

        
        

