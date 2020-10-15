namespace Seff


open System
open System.Windows
open System.IO
open System.Threading
open FSharp.Compiler.Interactive.Shell
open Seff.Config
open Seff.Util
open System.Windows.Media




/// A class to provide an Error Handler that can catch currupted state or access violation errors frim FSI threads too
type ProcessCorruptedState(config:Config) = 
      
    // TODO ingerate handler info FSI
    [< Security.SecurityCritical; Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions >] //to handle AccessViolationException too //https://stackoverflow.com/questions/3469368/how-to-handle-accessviolationexception/4759831
    member this.Handler (sender:obj) (e: UnhandledExceptionEventArgs) = 
            // Starting with the .NET Framework 4, this event is not raised for exceptions that corrupt the state of the process, 
            // such as stack overflows or access violations, unless the event handler is security-critical and has the HandleProcessCorruptedStateExceptionsAttribute attribute.
            // https://docs.microsoft.com/en-us/dotnet/api/system.appdomain.unhandledexception?redirectedfrom=MSDN&view=netframework-4.8
            // https://docs.microsoft.com/en-us/archive/msdn-magazine/2009/february/clr-inside-out-handling-corrupted-state-exceptions
            let t = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss-fff")
            let err = sprintf "ProcessCorruptedState Special Handler: AppDomain.CurrentDomain.UnhandledException: isTerminating: %b : time: %s\r\n%A" e.IsTerminating t e.ExceptionObject
            let filename = sprintf "Seff-UnhandledException-%s.txt" t
            let file = IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),filename)
            async {try  IO.File.WriteAllText(file, err) with _ -> ()} |> Async.Start // file might be open and locked
            config.Log.PrintAppErrorMsg "%s" err
 


type Fsi private (config:Config) =    
    let log = config.Log
    
    ///FSI events
    let startedEv        = new Event<CodeToEval>()      //TODO why include mode in event arg ?
    let runtimeErrorEv   = new Event<Exception>() 
    let canceledEv       = new Event<unit>() 
    let completedOkEv    = new Event<unit>()
    let isReadyEv        = new Event<unit>()
    let resetEv          = new Event<unit>()
    let modeChangedEv    = new Event<FsiMode>()
    
    let mutable state = NotLoaded

    let mutable mode =  Async
    
    let mutable sessionOpt :FsiEvaluationSession option = None 

    let mutable thread :Thread option = None


    let mutable currentDir = ""
    let mutable currentFile = ""
    let mutable currentTopLine = 0    
    let setDir (session:FsiEvaluationSession) (fi:FileInfo) = 
        try
            let dir = fi.DirectoryName
            if dir <> currentDir then 
                let cd = sprintf "# silentCd @\"%s\" ;;" dir
                session.EvalInteraction(cd)
                currentDir <- dir
                log.PrintInfoMsg "Current directory set to:\r\n%s\\" dir
            else
                log.PrintDebugMsg  "Current directory is already set to:\r\n%s\\" dir   
        with e->            
            log.PrintFsiErrorMsg "silentCD on FSI failed: %A" e 
    let setFileAndLine (session:FsiEvaluationSession) (topLine:int) (fi:FileInfo) = 
        try
            let file = fi.Name
            if file  <> currentFile || currentTopLine <> topLine then 
                let ln = sprintf "# %d @\"%s\" " topLine file
                session.EvalInteraction(ln)
                if file  <> currentFile then 
                    log.PrintInfoMsg "Current file set to: %s\\" file
                currentFile <- file
                currentTopLine <- topLine
            else
                log.PrintDebugMsg  "Current lien and file and is already set to Line %d for: %s\\" topLine file    
        with e->
            log.PrintFsiErrorMsg "setFileAndLine on FSI failed: %A" e 
             

    let init() = 

            match state with 
            | Initalizing -> log.PrintInfoMsg "FSI initialization can't be started because it is already in process.."
            | NotLoaded | Ready | Evaluating -> 
                let  prevState = state
                state <- Initalizing
                async{
                    //let timer = Seff.Timer()
                    //timer.tic()
                    if config.Settings.GetBool "asyncFsi" true then mode <- Async else mode <- FsiMode.Sync
                    match sessionOpt with 
                    |None -> ()
                    |Some session ->
                        session.Interrupt()  //TODO does this cancel running session correctly ??         
                        // TODO how to dispose previous session ?
          
                    let inStream = new StringReader("")
                    // first arg is ignored: https://github.com/fsharp/FSharp.Compiler.Service/issues/420 
                    // and  https://github.com/fsharp/FSharp.Compiler.Service/issues/877 
                    // and  https://github.com/fsharp/FSharp.Compiler.Service/issues/878            
                    
                    let allArgs = 
                         // "--shadowcopyreferences" is ignored https://github.com/fsharp/FSharp.Compiler.Service/issues/292
                        if config.Settings.GetBool Settings.keyFsiQuiet false then Array.append  config.FsiArugments.Get [| "--quiet"|] 
                        else                                                                     config.FsiArugments.Get
                        
                    
                    let fsiConfig = FsiEvaluationSession.GetDefaultConfiguration() // https://github.com/dotnet/fsharp/blob/4978145c8516351b1338262b6b9bdf2d0372e757/src/fsharp/fsi/fsi.fs#L2839
                    let fsiSession = FsiEvaluationSession.Create(fsiConfig, allArgs, inStream, log.TextWriterFsiStdOut, log.TextWriterFsiErrorOut) //, collectible=false ??) //https://github.com/dotnet/fsharp/blob/6b0719845c928361e63f6e38a9cce4ae7d621fbf/src/fsharp/fsi/fsi.fs#L2440
                    //AppDomain.CurrentDomain.UnhandledException.AddHandler (new UnhandledExceptionEventHandler( (new ProcessCorruptedState(config)).Handler)) //Add(fun ex -> log.PrintFsiErrorMsg "*** FSI AppDomain.CurrentDomain.UnhandledException:\r\n %A" ex.ExceptionObject)
                    Console.SetOut  (log.TextWriterConsoleOut)   // TODO needed to redirect printfn or coverd by TextWriterFsiStdOut? //https://github.com/fsharp/FSharp.Compiler.Service/issues/201
                    Console.SetError(log.TextWriterConsoleError) // TODO needed if evaluate non throwing or coverd by TextWriterFsiErrorOut? 
                    //if mode = Mode.Sync then do! Async.SwitchToContext Sync.syncContext            
                    //fsiSession.Run() // TODO ? dont do this it crashes the app when hosted in Rhino! 
                    state <- Ready
                    sessionOpt <- Some fsiSession
                    //timer.stop()
                    if prevState = NotLoaded then () //log.PrintInfoMsg "FSharp Interactive session created in %s"  timer.tocEx  
                    else                          log.PrintInfoMsg "FSharp Interactive session reset." // in %s" timer.tocEx     
            
                    if config.Hosting.IsHosted then 
                        match mode with
                        |Sync ->  log.PrintInfoMsg "FSharp Interactive will evaluate synchronously on UI Thread."
                        |Async -> log.PrintInfoMsg "FSharp Interactive will evaluate asynchronously on new Thread."    
                    fsiSession.AssemblyReferenceAdded.Add (config.AssemblyReferenceStatistic.Add)
                    do! Async.SwitchToContext Sync.syncContext 
                    isReadyEv.Trigger()
                    } |> Async.Start
    
    
    [< Security.SecurityCritical >] // TODO do these Attributes appy in to async thread too ?
    [< Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions >] //to handle AccessViolationException too //https://stackoverflow.com/questions/3469368/how-to-handle-accessviolationexception/4759831
    let eval(code:CodeToEval)=
        
        if not config.Hosting.FsiCanRun then 
            log.PrintAppErrorMsg "The Hosting App has blocked Fsi from Running, maybe because the App is busy in another command or task."
        else
            match sessionOpt with 
            |None -> log.PrintInfoMsg "Please wait till FSI is initalized for running scripts"
            |Some session ->
                state <- Evaluating                
                startedEv.Trigger(code) // do always sync

                //TODO https://github.com/dotnet/fsharp/blob/6b0719845c928361e63f6e38a9cce4ae7d621fbf/src/fsharp/fsi/fsi.fs#L2618
                // change via reflection??? 
                // let dummyScriptFileName = "input.fsx"

                let asyncEval = async{
                    if mode = FsiMode.Sync then 
                        do! Async.SwitchToContext Sync.syncContext                         
                
                    //Done already at startup in Initalize.fs, not neded here?
                    //if notNull Application.Current then // null if application is not yet created, or no application in hosted context
                    //    Application.Current.DispatcherUnhandledException.Add(fun e ->  //exceptions generated on the UI thread // TODO realy do this on every evaluataion?
                    //        log.PrintAppErrorMsg "Application.Current.DispatcherUnhandledException in fsi thread: %A" e.Exception        
                    //        e.Handled <- true) 
                    //AppDomain.CurrentDomain.UnhandledException.AddHandler (//catching unhandled exceptions generated from all threads running under the context of a specific application domain. //https://dzone.com/articles/order-chaos-handling-unhandled
                    //    new UnhandledExceptionEventHandler( (new ProcessCorruptedState(config)).Handler)) //https://stackoverflow.com/questions/14711633/my-c-sharp-application-is-returning-0xe0434352-to-windows-task-scheduler-but-it
            
                    // set current dir, file and Topline
                    match code.file with 
                    | NotSet -> () //setFileAndLine session code.fromLine "Unnamed File"
                    | SetTo fi -> 
                        //setDir session fi
                        //setFileAndLine session code.fromLine fi
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
                        for e in errs do log.PrintAppErrorMsg "EvalInteractionNonThrowing should not have errors, but this happend: %A" e
                        //match vo with None-> () |Some v -> log.PrintDebugMsg "Interaction evaluted to %A <%A>" v.ReflectionValue v.ReflectionType
                   
                    |Choice2Of2 exn ->     
                        match exn with 
                        | :? OperationCanceledException ->
                            canceledEv.Trigger()
                            isReadyEv.Trigger()
                            if config.Hosting.IsHosted && mode = FsiMode.Async && isNull exn.StackTrace  then 
                                log.PrintFsiErrorMsg "FSI evaluation was canceled,\r\nif you did not trigger this cancellation try running FSI in Synchronos evaluation mode (instead of Async)."    
                            else 
                                log.PrintFsiErrorMsg "FSI evaluation was canceled by user!" //:\r\n%A" exn.StackTrace  //: %A" exn                
                           
                        | :? FsiCompilationException -> 
                            runtimeErrorEv.Trigger(exn)
                            isReadyEv.Trigger()
                            log.PrintFsiErrorMsg "Compiler Error:"
                            for e in errs do    
                                log.PrintFsiErrorMsg "%A" e
                        | _ ->    
                            runtimeErrorEv.Trigger(exn)
                            isReadyEv.Trigger()
                            log.PrintFsiErrorMsg "Runtime Error:" 
                        
                            //highlight line number:
                            let et = sprintf "%A" exn
                            let t,r = String.splitOnce ".fsx:" et
                            if r="" then 
                                log.PrintFsiErrorMsg "%s" et
                            else
                                let ln,rr = String.splitOnce "\r\n" r                        
                                log.Print_FsiErrorMsg "%s.fsx:" t
                                log.PrintCustomBrush Brushes.Blue "%s" ln
                                log.PrintFsiErrorMsg "%s" rr
                              
                    } 
        
                //TODO trigger from a new thread even in Synchronous evaluation ?
                let thr = new Thread(fun () -> 
                    // a cancellation token here fails to cancel evaluation.
                    // dsyme: Thread.Abort - it is needed in interruptible interactive execution scenarios: https://github.com/dotnet/fsharp/issues/9397#issuecomment-648376476
                    Async.StartImmediate(asyncEval))  
                thread <- Some thr           
                thr.Start()
    
    
    static let mutable singleInstance:Fsi option  = None
    
    /// ensures only one instance is created
    static member GetOrCreate(config:Config) :Fsi = 
        match singleInstance with 
        |Some fsi -> fsi
        |None -> singleInstance <- Some (new Fsi(config)); singleInstance.Value
        
        //match config.Hosting.Mode with
        //|Hosted h when h= "Revit" ->
        //    config.Log.PrintInfoMsg "Fsi in Sync only mode for Revit"
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
            |Sync ->() //don't block event completion by doing some debug logging. TODO test how to log !//log.PrintInfoMsg "Current synchronous Fsi Interaction cannot be canceled"     // UI for this only available in asynchronous mode anyway, see Commands  
            |Async ->                
                match thread with 
                |None ->() 
                |Some thr -> 
                    thread<-None
                    state<-Ready 
                    thr.Abort() // raises OperationCanceledException  
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
        //if DateTime.Today > DateTime(2020, 12, 30) then log.PrintFsiErrorMsg "*** Your Seff Editor has expired, please download a new version. or contact goswin@rothenthal.com ***"
        if DateTime.Today > DateTime(2021, 06, 30) then log.PrintFsiErrorMsg "Seff Exception %A" (NullReferenceException().GetType())
        else 
            //if DateTime.Today > DateTime(2021, 03, 30) then log.PrintFsiErrorMsg "*** Your Seff Editor will expire on 2020-12-31, please download a new version soon. or contact goswin@rothenthal.com***"        
            match this.AskIfCancellingIsOk () with 
            | NotEvaluating   -> eval(code) 
            | YesAsync        -> this.CancelIfAsync();eval(code) 
            | Dont            -> ()
            | NotPossibleSync -> log.PrintInfoMsg "Wait till current synchronous evaluation completes before starting new one."
     

    member this.Reset() =  
        match this.AskIfCancellingIsOk () with 
        | NotEvaluating   ->                       init (); resetEv.Trigger() //log.PrintInfoMsg "FSI reset." done by this.Initialize()
        | YesAsync        -> this.CancelIfAsync(); init (); resetEv.Trigger()
        | Dont            -> ()
        | NotPossibleSync -> log.PrintInfoMsg "ResetFsi is not be possibe in current synchronous evaluation." // TODO test
      

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
        | NotPossibleSync -> log.PrintInfoMsg "Wait till current synchronous evaluation completes before seting mode to Async."
    
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

        
        

