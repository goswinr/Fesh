﻿namespace Seff


open System
open System.Windows
open System.IO
open System.Threading
open FSharp.Compiler.Interactive.Shell



module Fsi =    
    
    [<AbstractClass; Sealed>]
    /// A static class to hold FSI events 
    type Events private () =
        
        static let startedEv        = new Event<unit>() 
        static let runtimeErrorEv   = new Event<Exception>() 
        static let canceledEv       = new Event<unit>() 
        static let completedEv      = new Event<unit>()
        static let isReadyEv        = new Event<unit>()         

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

    
    type States = Ready|Evaluating
    type Mode   = Sync |Async

    let mutable state = Ready
    let mutable internal mode =  Sync
    
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
            let inStream = new StringReader("")
            // first arg is ignored: https://github.com/fsharp/FSharp.Compiler.Service/issues/420 and https://github.com/fsharp/FSharp.Compiler.Service/issues/877 and  https://github.com/fsharp/FSharp.Compiler.Service/issues/878            
            let allArgs = [|"" ; "--langversion:preview" ; "--noninteractive" ; "--debug+"; "--debug:full" ;"--optimize+" ;"--nologo"; "--gui-"|] // ; "--shadowcopyreferences" is ignored https://github.com/fsharp/FSharp.Compiler.Service/issues/292           
            let fsiConfig = FsiEvaluationSession.GetDefaultConfiguration() // https://github.com/dotnet/fsharp/blob/4978145c8516351b1338262b6b9bdf2d0372e757/src/fsharp/fsi/fsi.fs#L2839
            let fsiSession = FsiEvaluationSession.Create(fsiConfig, allArgs, inStream, Log.textwriter, Log.textwriter) //, collectible=false ??) //TODO add error logger window
            AppDomain.CurrentDomain.UnhandledException.Add(fun ex -> Log.print "*** FSI background exception:\r\n %A" ex.ExceptionObject)
            Console.SetOut  (Log.textwriter) //needed to redirect printfn ? //https://github.com/fsharp/FSharp.Compiler.Service/issues/201
            Console.SetError(Log.textwriter) //TODO needed if evaluate non throwing ? 
            //if mode = Mode.Sync then do! Async.SwitchToContext Sync.syncContext            
            //fsiSession.Run()// dont do this it crashes the app !            
            session <- Some fsiSession
            Log.print "* Time for loading FSharp Interactive: %s"  timer.tocEx
            match mode with
            |Sync ->  Log.print "*FSharp Interactive will evaluate synchronously on UI Thread."
            |Async -> Log.print "*FSharp Interactive will evaluate asynchronously on new Thread."
            //}
    
    // to be able to cancel running Fsi eval
    let mutable private thread :Thread option = None
    //let mutable private fsiCancelScr :CancellationTokenSource option = None

    let private eval(code)=
        state <- Evaluating
        //fsiCancelScr <- Some (new CancellationTokenSource())
        UI.log.Background <- Appearance.logBackgroundFsiEvaluating
        Events.started.Trigger() // do always sync
        if session.IsNone then  startSession()     // sync 
        
        let thr = new Thread(fun () ->
            let a = 
                async{
                    if mode = Mode.Sync then do! Async.SwitchToContext Sync.syncContext 
                
                    let choice, errs =  
                        try session.Value.EvalInteractionNonThrowing(code)//,fsiCancelScr.Value.Token)   // cancellation token here fails to cancel in sync, might still throw OperationCanceledException if async       
                        with e -> Choice2Of2 e , [| |]
            
                    if mode = Mode.Async then do! Async.SwitchToContext Sync.syncContext 
            
                    thread<-None
                    state<-Ready //TODO reached when canceled ?                     
            
                    match choice with //TODO move out ?
                    |Choice1Of2 vo -> 
                        Events.completed.Trigger()
                        Events.isReady.Trigger()
                        for e in errs do Log.print "Why Error: %A" e
                        //match vo with None-> () |Some v -> Log.print "Interaction evaluted to %A <%A>" v.ReflectionValue v.ReflectionType
                
                    |Choice2Of2 exn ->     
                        match exn with 
                        | :? OperationCanceledException ->
                            Events.canceled.Trigger()
                            Events.isReady.Trigger()
                            Log.print "**FSI evaluation was cancelled with OperationCanceledException ++"                    
                        | :? FsiCompilationException -> 
                            Events.runtimeError.Trigger(exn)
                            Events.isReady.Trigger()
                            Log.print "Compiler Error:"
                            for e in errs do    
                                Log.print "%A" e
                        | _ ->    
                            Events.runtimeError.Trigger(exn)
                            Events.isReady.Trigger()
                            Log.print "Runtime Error: %A" exn     } 
            
            Async.StartImmediate(a)// cancellation token here fails to cancel evaluation,
            )
        thread<-Some thr
        thr.Start()

     
    //-------------- public interface: ---------

    type IsCancelOk = Yes|No|NotNeded 

    /// forces cancellation
    let cancel() =
        //fsiCancelScr.Value.Cancel()
        match state with 
        | Evaluating ->            
            match thread with 
            |Some thr -> 
                match mode with
                |Async ->                
                    thr.Abort()
                    thread<-None
                    state<-Ready                 
                    Events.canceled.Trigger()
                    Events.isReady.Trigger()
                    Log.print "Current Async Fsi Interaction thread was aborted."
                |Sync ->
                    thr.Interrupt()
                    thr.Abort()
                    thread<-None
                    state<-Ready                 
                    raise (new OperationCanceledException("async1"))
                    Events.canceled.Trigger()
                    Events.isReady.Trigger()
                    Log.print "Current Sync Fsi Interaction cannot be canceled"                    
                    
            |None -> 
                 Log.print "**No thread to cancel Fsi, should never happen !"            
        | Ready -> ()

    let isCancellingOk() = 
        match state with 
        | Ready -> NotNeded      
        | Evaluating ->
            match MessageBox.Show("Do you want to Cancel currently running code?", "Cancel Current Evaluation?", MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.No) with
            | MessageBoxResult.Yes -> 
                match state with // might have changed in the meantime of Messagebox show
                | Ready -> NotNeded      
                | Evaluating -> Yes            
            | _  -> No // no is no, dont swap to NotNeded
    
    ///shows UI to confirm cancelling, returns true if cancelled
    let tryCancel()=
        match isCancellingOk() with 
        | NotNeded -> cancel() ; true
        | Yes      -> cancel() ; true
        | No       -> ()       ; false          
    
    let evaluate(code) = 
        if DateTime.Today > DateTime(2020, 9, 30) then failwithf "Your Seff Editor has expired, please download a new version."
        if DateTime.Today > DateTime(2020, 7, 30) then Log.print "*** Your Seff Editor will expire on 2020-9-30, please download a new version soon. ***"        
        match isCancellingOk() with 
        | NotNeded -> eval(code)  
        | Yes      -> cancel();  eval(code)
        | No       -> ()       
            
    let reset() =         
        cancel()
        UI.log.Clear()
        startSession ()//|> Async.StartImmediate 


    let setMode(sync:Mode) =         
        let setConfig()=
            match mode with
            |Sync ->
                Config.setBool "asyncFsi" false
                StatusBar.async.Content <- "Synchronous in UI Thread"            
            |Async ->                 
                Config.setBool "asyncFsi" true
                StatusBar.async.Content <- "Asynchronous" 

        match isCancellingOk() with 
        | NotNeded ->            
            mode <- sync
            startSession () //|> Async.StartImmediate
            setConfig()   
        | Yes      -> 
            mode <- sync
            cancel()
            startSession () //|> Async.StartImmediate 
            setConfig()
        | No  -> () 

    
    let toggleSync()=
        match mode with
        |Async ->  setMode Sync
        |Sync ->   setMode Async

        
    let clearLog() =         
        UI.log.Clear()
        UI.log.Background <- Appearance.logBackgroundFsiReady // clearing log should remove red error color too.
        
    do
        Events.Canceled.Add        (fun () -> Log.print "+Fsi Canceled")
        Events.IsReady.Add         (fun () -> Log.print "+Fsi isReady")
        Events.RuntimeError.Add    (fun _  -> Log.print "+Fsi RuntimeError")
        Events.Started.Add         (fun () -> Log.print "+Fsi Started")
        Events.Completed.Add       (fun () -> Log.print "+Fsi Completed")
        
        Events.RuntimeError.Add (fun _  -> UI.log.Background <- Appearance.logBackgroundFsiHadError)
        //Events.Started.Add      (fun () -> UI.log.Background <- Appearance.logBackgroundFsiEvaluating) // happens at end of eval in sync mode
        Events.Completed.Add       (fun () -> UI.log.Background <- Appearance.logBackgroundFsiReady)
        Events.Canceled.Add       (fun () -> UI.log.Background <- Appearance.logBackgroundFsiReady)
        
        
