namespace Seff


open System
open System.Windows
open System.IO
open System.Threading
open FSharp.Compiler.Interactive.Shell
open Seff.Config
open Seff.Util.General




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
    
    let mutable session:FsiEvaluationSession option = None 

    let mutable thread :Thread option = None


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
                    if session.IsSome then 
                        session.Value.Interrupt()  //TODO does this cancel running session correctly ??         
                        // TODO how to dispose previous session ?
          
                    let inStream = new StringReader("")
                    // first arg is ignored: https://github.com/fsharp/FSharp.Compiler.Service/issues/420 
                    // and  https://github.com/fsharp/FSharp.Compiler.Service/issues/877 
                    // and  https://github.com/fsharp/FSharp.Compiler.Service/issues/878            
                    
                    let allArgs = 
                         // "--shadowcopyreferences" is ignored https://github.com/fsharp/FSharp.Compiler.Service/issues/292
                        if config.Settings.GetBool Settings.keyFsiQuiet false then Array.append  config.FsiArugments.Get [| "--quiet"|] 
                        else                                                                     config.FsiArugments.Get
                        //|>> (String.concat "; " >> log.PrintDebugMsg "Fsi Args %s")
                        
                    
                    //if printQuietInfo then 
                    //    printQuietInfo <- false // print just once:
                    //    if allArgs |> Array.exists (fun a -> a.ToLower() =  "--quiet") then 
                    //        log.PrintInfoMsg "This Fsi Log window will not print the default FSI output because the '--quite' flag is use as fsi arguments."
                    //        log.PrintInfoMsg "See FsiArugments.txt file in Menu -> About -> Settings Folder"                        
                    
                    let fsiConfig = FsiEvaluationSession.GetDefaultConfiguration() // https://github.com/dotnet/fsharp/blob/4978145c8516351b1338262b6b9bdf2d0372e757/src/fsharp/fsi/fsi.fs#L2839
                    let fsiSession = FsiEvaluationSession.Create(fsiConfig, allArgs, inStream, log.TextWriterFsiStdOut, log.TextWriterFsiErrorOut) //, collectible=false ??) //https://github.com/dotnet/fsharp/blob/6b0719845c928361e63f6e38a9cce4ae7d621fbf/src/fsharp/fsi/fsi.fs#L2440
                    //AppDomain.CurrentDomain.UnhandledException.AddHandler (new UnhandledExceptionEventHandler( (new ProcessCorruptedState(config)).Handler)) //Add(fun ex -> log.PrintFsiErrorMsg "*** FSI AppDomain.CurrentDomain.UnhandledException:\r\n %A" ex.ExceptionObject)
                    Console.SetOut  (log.TextWriterConsoleOut)   // TODO needed to redirect printfn or coverd by TextWriterFsiStdOut? //https://github.com/fsharp/FSharp.Compiler.Service/issues/201
                    Console.SetError(log.TextWriterConsoleError) // TODO needed if evaluate non throwing or coverd by TextWriterFsiErrorOut? 
                    //if mode = Mode.Sync then do! Async.SwitchToContext Sync.syncContext            
                    //fsiSession.Run() // TODO ? dont do this it crashes the app when hosted in Rhino! 
                    state <- Ready
                    session <- Some fsiSession
                    //timer.stop()
                    if prevState = NotLoaded then () //log.PrintInfoMsg "FSharp Interactive session created in %s"  timer.tocEx  
                    else                          log.PrintInfoMsg "FSharp Interactive session reset." // in %s" timer.tocEx     
            
                    if config.HostingInfo.IsHosted then 
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
        state <- Evaluating
        //fsiCancelScr <- Some (new CancellationTokenSource()) //does not work? needs Thread.Abort () ?
        startedEv.Trigger(code) // do always sync
        if session.IsNone then 
            log.PrintInfoMsg "Please wait till FSI is initalized for running scripts"
        else
        
            //TODO https://github.com/dotnet/fsharp/blob/6b0719845c928361e63f6e38a9cce4ae7d621fbf/src/fsharp/fsi/fsi.fs#L2618
            // change via reflection??? 
            // let dummyScriptFileName = "input.fsx"

            let asyncEval = async{
                if mode = FsiMode.Sync then 
                    do! Async.SwitchToContext Sync.syncContext 
                    //TODO hide window while running !
                
                //Done already at startup, not neded here?
                //if notNull Application.Current then // null if application is not yet created, or no application in hosted context
                //    Application.Current.DispatcherUnhandledException.Add(fun e ->  //exceptions generated on the UI thread // TODO realy do this on every evaluataion?
                //        log.PrintAppErrorMsg "Application.Current.DispatcherUnhandledException in fsi thread: %A" e.Exception        
                //        e.Handled <- true)  
                //     
                //
                //AppDomain.CurrentDomain.UnhandledException.AddHandler (//catching unhandled exceptions generated from all threads running under the context of a specific application domain. //https://dzone.com/articles/order-chaos-handling-unhandled
                //    new UnhandledExceptionEventHandler( (new ProcessCorruptedState(config)).Handler)) //https://stackoverflow.com/questions/14711633/my-c-sharp-application-is-returning-0xe0434352-to-windows-task-scheduler-but-it
            
                // TODO set current directory  form fileInfo

                let choice, errs =  
                    try session.Value.EvalInteractionNonThrowing(code.code) //,fsiCancelScr.Value.Token)   // cancellation token here fails to cancel in sync, might still throw OperationCanceledException if async       
                    with e -> Choice2Of2 e , [| |]
               
                if mode = Async then do! Async.SwitchToContext Sync.syncContext 
               
                thread <- None
                state <- Ready //TODO reached when canceled ?                     
               
                match choice with //TODO move out of Thread?
                |Choice1Of2 vo -> 
                    completedOkEv.Trigger()
                    isReadyEv.Trigger()
                    for e in errs do log.PrintAppErrorMsg " **** Why Error? EvalInteractionNonThrowing should not have errors: %A" e
                    //match vo with None-> () |Some v -> log.PrintDebugMsg "Interaction evaluted to %A <%A>" v.ReflectionValue v.ReflectionType
                   
                |Choice2Of2 exn ->     
                    match exn with 
                    | :? OperationCanceledException ->
                        canceledEv.Trigger()
                        isReadyEv.Trigger()
                        log.PrintFsiErrorMsg "Fsi evaluation was canceled by user!" //: %A" exn                
                           
                    | :? FsiCompilationException -> 
                        runtimeErrorEv.Trigger(exn)
                        isReadyEv.Trigger()
                        log.PrintFsiErrorMsg "Compiler Error:"
                        for e in errs do    
                            log.PrintFsiErrorMsg "%A" e
                    | _ ->    
                        runtimeErrorEv.Trigger(exn)
                        isReadyEv.Trigger()
                        log.PrintFsiErrorMsg "Runtime Error: %A" exn     
                } 
        
            //TODO trigger from a new thread even in Synchronous evaluation ?
            let thr = new Thread(fun () -> Async.StartImmediate(asyncEval)) // a cancellation token here fails to cancel evaluation,
            thread <- Some thr           
            thr.Start()
    
    
    static let mutable singleInstance:Fsi option  = None
    
    /// ensures only one instance is created
    static member GetOrCreate(config:Config) :Fsi = 
        match singleInstance with 
        |Some fsi -> fsi
        |None -> singleInstance <- Some (new Fsi(config)); singleInstance.Value
        
        //match config.HostingInfo.Mode with
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
        if DateTime.Today > DateTime(2020, 12, 31) then log.PrintFsiErrorMsg "*** Your Seff Editor has expired, please download a new version. or contact goswin@rothenthal.com ***"
        else 
            if DateTime.Today > DateTime(2020, 10, 30) then log.PrintFsiErrorMsg "*** Your Seff Editor will expire on 2020-12-31, please download a new version soon. or contact goswin@rothenthal.com***"        
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
 
    /// This event will be trigger after succesfull completion, NOT on runtime error or cancelling of Fsi
    [<CLIEvent>]
    member this.OnCompletedOk = completedOkEv.Publish
      
    /// This event will be trigger after completion, runtime error or cancelling of Fsi
    [<CLIEvent>]
    member this.OnIsReady  = isReadyEv .Publish
     
    /// This event will be trigger after Fsi is reset
    [<CLIEvent>]
    member this.OnReset  = resetEv.Publish   
    
    ///Triggered whenever Fsi for evaluation mode changes
    [<CLIEvent>]
    member this.OnModeChanged = modeChangedEv.Publish

        
        

