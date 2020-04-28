namespace Seff


open System
open System.Windows
open System.IO
open System.Threading
open FSharp.Compiler.Interactive.Shell
open Seff.Model


/// A static class to provide an Error Handler that can catch currupted state or access violation errors frim FSI threads too
type internal ProcessCorruptedState =  
    [< Security.SecurityCritical; Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions >] //to handle AccessViolationException too //https://stackoverflow.com/questions/3469368/how-to-handle-accessviolationexception/4759831
    static member Handler (sender:obj) (e: UnhandledExceptionEventArgs) = 
            // Starting with the .NET Framework 4, this event is not raised for exceptions that corrupt the state of the process, 
            // such as stack overflows or access violations, unless the event handler is security-critical and has the HandleProcessCorruptedStateExceptionsAttribute attribute.
            // https://docs.microsoft.com/en-us/dotnet/api/system.appdomain.unhandledexception?redirectedfrom=MSDN&view=netframework-4.8
            // https://docs.microsoft.com/en-us/archive/msdn-magazine/2009/february/clr-inside-out-handling-corrupted-state-exceptions
            let err = sprintf "AppDomain.CurrentDomain.UnhandledException: isTerminating: %b : %A" e.IsTerminating e.ExceptionObject
            let file = IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),"Seff-AppDomain.CurrentDomain.UnhandledException.txt")
            async { IO.File.WriteAllText(file, err)} |> Async.Start
            Log.printAppErrorMsg "%s" err


[<Sealed>]
type Fsi private () =    
       
    ///FSI events
    static let startedEv        = new Event<FsiMode>()      //TODO why include mode ?
    static let runtimeErrorEv   = new Event<Exception>() 
    static let canceledEv       = new Event<FsiMode>() 
    static let completedOkEv    = new Event<FsiMode>()
    static let isReadyEv        = new Event<FsiMode>()
    static let resetEv          = new Event<FsiMode>()
    
    static let mutable state = Ready

    static let mutable mode =  Async
    
    static let mutable session:FsiEvaluationSession option = None 

    static let mutable thread :Thread option = None


    [< Security.SecurityCritical >] 
    [< Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions >] //to handle AccessViolationException too //https://stackoverflow.com/questions/3469368/how-to-handle-accessviolationexception/4759831
    static let eval(code)=
        state <- Evaluating
        //fsiCancelScr <- Some (new CancellationTokenSource()) //does not work? needs Thread.Abort () ?
        startedEv.Trigger(mode) // do always sync
        if session.IsNone then  Fsi.Initalize()     // sync 
           
        let thr = new Thread(fun () ->
            let a = 
                async{
                    if mode = Sync then do! Async.SwitchToContext Sync.syncContext 
                       
                    Application.Current.DispatcherUnhandledException.Add(fun e ->  //exceptions generated on the UI thread
                        Log.printAppErrorMsg "Application.Current.DispatcherUnhandledException in fsi thread: %A" e.Exception        
                        e.Handled <- true)        
          
                    AppDomain.CurrentDomain.UnhandledException.AddHandler (//catching unhandled exceptions generated from all threads running under the context of a specific application domain. //https://dzone.com/articles/order-chaos-handling-unhandled
                        new UnhandledExceptionEventHandler( ProcessCorruptedState.Handler)) //https://stackoverflow.com/questions/14711633/my-c-sharp-application-is-returning-0xe0434352-to-windows-task-scheduler-but-it

                    let choice, errs =  
                        try session.Value.EvalInteractionNonThrowing(code) //,fsiCancelScr.Value.Token)   // cancellation token here fails to cancel in sync, might still throw OperationCanceledException if async       
                        with e -> Choice2Of2 e , [| |]
               
                    if mode = Async then do! Async.SwitchToContext Sync.syncContext 
               
                    thread <- None
                    state <- Ready //TODO reached when canceled ?                     
               
                    match choice with //TODO move out of Thread?
                    |Choice1Of2 vo -> 
                        completedOkEv.Trigger(mode)
                        isReadyEv.Trigger(mode)
                        for e in errs do Log.printAppErrorMsg " **** Why Error? EvalInteractionNonThrowing should not have errors: %A" e
                        //match vo with None-> () |Some v -> Log.print "Interaction evaluted to %A <%A>" v.ReflectionValue v.ReflectionType
                   
                    |Choice2Of2 exn ->     
                        match exn with 
                        | :? OperationCanceledException ->
                            canceledEv.Trigger(mode)
                            isReadyEv.Trigger(mode)
                            Log.printInfoMsg "Fsi evaluation was canceled: %s" exn.Message                    
                           
                        | :? FsiCompilationException -> 
                            runtimeErrorEv.Trigger(exn)
                            isReadyEv.Trigger(mode)
                            Log.printFsiErrorMsg "Compiler Error:"
                            for e in errs do    
                                Log.printFsiErrorMsg "%A" e
                        | _ ->    
                            runtimeErrorEv.Trigger(exn)
                            isReadyEv.Trigger(mode)
                            Log.printFsiErrorMsg "Runtime Error: %A" exn     
                    } 
               
            Async.StartImmediate(a) // a cancellation token here fails to cancel evaluation,
            )
           
        thread<-Some thr
           
        thr.Start()
     
    //-------------- public interface: ---------

    static member State = state

    static member Mode = mode

    /// starts a new Fsi session 
    static member Initalize () =     
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
       
        let timer = Seff.Timer()
        timer.tic()
        if Config.Settings.getBool "asyncFsi" true then mode <- Async else mode <- Sync
        if session.IsSome then 
            session.Value.Interrupt()  //TODO does this cancel running session correctly ??         
            // TODO how to dispose previous session ?
          

        // TODO change to async thead for  FsiEvaluationSession.Create ?
        let inStream = new StringReader("")
        // first arg is ignored: https://github.com/fsharp/FSharp.Compiler.Service/issues/420 
        // and  https://github.com/fsharp/FSharp.Compiler.Service/issues/877 
        // and  https://github.com/fsharp/FSharp.Compiler.Service/issues/878            
        let allArgs = [|"" ; "--langversion:preview" ; "--noninteractive" ; "--debug+"; "--debug:full" ;"--optimize+" ; "--gui-" ; "--nologo"|] // ; "--shadowcopyreferences" is ignored https://github.com/fsharp/FSharp.Compiler.Service/issues/292           
        let fsiConfig = FsiEvaluationSession.GetDefaultConfiguration() // https://github.com/dotnet/fsharp/blob/4978145c8516351b1338262b6b9bdf2d0372e757/src/fsharp/fsi/fsi.fs#L2839
        let fsiSession = FsiEvaluationSession.Create(fsiConfig, allArgs, inStream, Log.TextWriterFsiStdOut, Log.TextWriterFsiErrorOut) //, collectible=false ??)
        AppDomain.CurrentDomain.UnhandledException.Add(fun ex -> Log.printFsiErrorMsg "*** FSI AppDomain.CurrentDomain.UnhandledException:\r\n %A" ex.ExceptionObject)
        Console.SetOut  (Log.TextWriterConsoleOut)   // TODO needed to redirect printfn or coverd by TextWriterFsiStdOut? //https://github.com/fsharp/FSharp.Compiler.Service/issues/201
        Console.SetError(Log.TextWriterConsoleError) // TODO needed if evaluate non throwing or coverd by TextWriterFsiErrorOut? 
        //if mode = Mode.Sync then do! Async.SwitchToContext Sync.syncContext            
        //fsiSession.Run() // TODO ? dont do this it crashes the app when hosted in Rhino! 
              
        if session.IsNone then Log.printInfoMsg "Time for loading FSharp Interactive: %s"  timer.tocEx  
        else                   Log.printInfoMsg "New FSharp Interactive session created."    
        session <- Some fsiSession
        timer.stop()

        if Config.Context.IsStandalone then 
            match mode with
            |Sync ->  Log.printInfoMsg "*FSharp Interactive will evaluate synchronously on UI Thread."
            |Async -> Log.printInfoMsg "*FSharp Interactive will evaluate asynchronously on new Thread."           
     

    static member CancelIfAsync() = 
        match state  with 
        | Ready -> ()
        | Evaluating -> 
            match mode with
            |Sync ->() //don't block event completion by doing some debug logging. TODO test how to log !//Log.printInfoMsg "Current synchronous Fsi Interaction cannot be canceled"     // UI for this only available in asynchronous mode anyway, see Commands  
            |Async ->                
                match thread with 
                |None ->() 
                |Some thr -> 
                    thread<-None
                    state<-Ready 
                    thr.Abort() // raises OperationCanceledException  

    
    static member AskIfCancellingIsOk() = 
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
    static member AskAndCancel() =
        match Fsi.AskIfCancellingIsOk () with 
        | NotEvaluating   -> Ready
        | YesAsync        -> Fsi.CancelIfAsync();Ready
        | Dont            -> Evaluating
        | NotPossibleSync -> Evaluating     // UI for this only available in asynchronous mode anyway, see Commands  
        

    static member Evaluate(code) =         
        if DateTime.Today > DateTime(2020, 9, 30) then failwithf "Your Seff Editor has expired, please download a new version."
        if DateTime.Today > DateTime(2020, 7, 30) then Log.printInfoMsg "*** Your Seff Editor will expire on 2020-9-30, please download a new version soon. ***"        
        match Fsi.AskIfCancellingIsOk () with 
        | NotEvaluating   -> eval(code) 
        | YesAsync        -> Fsi.CancelIfAsync();eval(code) 
        | Dont            -> ()
        | NotPossibleSync -> Log.printInfoMsg "Wait till current synchronous evaluation completes before starting new one."
       

    static member  Reset() =  
        match Fsi.AskIfCancellingIsOk () with 
        | NotEvaluating   ->                      Log.printInfoMsg "FSI reset." ; Fsi.Initalize (); resetEv.Trigger(mode)
        | YesAsync        -> Fsi.CancelIfAsync(); Log.printInfoMsg "FSI reset." ; Fsi.Initalize (); resetEv.Trigger(mode)
        | Dont            -> ()
        | NotPossibleSync -> Log.printInfoMsg "ResetFsi is not be possibe in current synchronous evaluation." // TODO test
      

    static member  SetMode(sync:FsiMode) =         
        let setConfig()=
            match mode with
            |Sync -> Config.Settings.setBool "asyncFsi" false                          
            |Async -> Config.Settings.setBool "asyncFsi" true   

        match Fsi.AskIfCancellingIsOk() with 
        | NotEvaluating | YesAsync    -> 
            mode <- sync
            setConfig()
            Config.Settings.Save()
            Fsi.Initalize ()
        | Dont -> () 
        | NotPossibleSync -> Log.printInfoMsg "Wait till current synchronous evaluation completes before seting mode to Async."
    
    static member  ToggleSync()=
        match mode with
        |Async ->  Fsi.SetMode Sync
        |Sync ->   Fsi.SetMode Async      

   
    ///Triggered whenever code is sent to Fsi for evaluation
    [<CLIEvent>]
    static member OnStarted = startedEv.Publish

    /// Interactive evaluation was cancelled because of a runtime error
    [<CLIEvent>]
    static member OnRuntimeError = runtimeErrorEv.Publish
             
    /// Interactive evaluation was cancelled by user (e.g. by pressing Esc)
    [<CLIEvent>]
    static member OnCanceled = canceledEv.Publish
 
    /// This event will be trigger after succesfull completion, NOT on runtime error or cancelling of Fsi
    [<CLIEvent>]
    static member OnCompletedOk = completedOkEv.Publish
      
    /// This event will be trigger after completion, runtime error or cancelling of Fsi
    [<CLIEvent>]
    static member OnIsReady  = isReadyEv .Publish
     
    /// This event will be trigger after Fsi is reset
    [<CLIEvent>]
    static member OnReset  = resetEv.Publish   


        
        

