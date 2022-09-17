﻿namespace Seff


open System
open System.IO
open System.Threading
open System.Windows
open System.Windows.Media

open Seff.Model
open Seff.Config
open Seff.Util

open FsEx.Wpf
open FSharp.Compiler
open FSharp.Compiler.Interactive.Shell
open FSharp.Compiler.Diagnostics
open System.Windows.Threading

type FsiState = 
    Ready | Evaluating | Initializing | NotLoaded

type FsiMode  = 
    Sync | Async472 | Async60

    /// either Async60 or Async472
    member this.IsAsync = 
        match this with
        | Sync -> false
        | Async472 | Async60 -> true

type FsiIsCancelingIsOk = 
    | NotEvaluating // Nothing can be cancelled because no evaluation is running
    | YesAsync472 // an async evaluation on net472 or net48 is running, it can be cancelled via thread.Abort()
    | NoAsync60  // an async evaluation on net6 , net 6 does not support  thread.Abort(), cannot be cancelled
    | UserDoesntWantTo // Don't cancel because the user actually doesn't want to cancel 
    | NotPossibleSync // Not-Possible-Sync because during sync eval the UI should be frozen anyway and this request should not be happening

// not needed in Net6.0 ??
#nowarn "44" //for: This construct is deprecated. Recovery from corrupted process state exceptions is not supported; HandleProcessCorruptedStateExceptionsAttribute is ignored.

module GoTo =
    
    /// open any foldings if required and select at location
    let line (lineNumber :int, ied:IEditor) =   
        /// similar to Foldings.GoToLineAndUnfold
        let ava = ied.AvaEdit
        let ln = ava.Document.GetLineByNumber(lineNumber)
        let mutable unfoldedOneOrMore = false
        for fold in ied.FoldingManager.GetFoldingsContaining(ln.Offset) do
            if fold.IsFolded then
                fold.IsFolded <- false
                unfoldedOneOrMore <- true        
        ava.ScrollTo(ln.LineNumber,1)
        //ied.AvaEdit.CaretOffset<- loc.EndOffset // done by ied.AvaEdit.Select too
        ava.Select(ln.Offset, ln.Length)
        

type Fsi private (config:Config) = 
    let log = config.Log

    ///FSI events
    let startedEv        = new Event<CodeToEval>()
    let runtimeErrorEv   = new Event<Exception>()
    let canceledEv       = new Event<CodeToEval>()
    let completedOkEv    = new Event<CodeToEval>()
    let isReadyEv        = new Event<unit>()
    let resetEv          = new Event<unit>()
    let modeChangedEv    = new Event<FsiMode>()

    let shutDownThreadEv = new Event<unit>()
    let onShutDownThread = shutDownThreadEv.Publish

    let mutable state = NotLoaded

    /// either Async60 or Async472
    let asyncMode = if config.Hosting.IsRunningOnDotNetCore then Async60 else Async472

    let mutable mode = asyncMode

    let mutable sessionOpt :FsiEvaluationSession option = None
    
    let mutable asyncContext: option<SynchronizationContext> = None

    let mutable asyncThread: option<Thread> = None 
    
    let mutable pendingEval :option<CodeToEval> = None // for storing evaluations that are triggered before fsi is ready

    let abortThenMakeAndStartAsyncThread() = 
        //shutDownThreadEv.Trigger() // don't do this ! this shuts down all of Seff !!

        match asyncThread with 
        |Some thr -> 
            asyncContext <- None
            asyncThread <- None
            #if NETFRAMEWORK
            // Thread.Abort method is not supported in .NET 6 // https://github.com/dotnet/runtime/issues/41291
            // dsyme: Thread.Abort - it is needed in interruptible interactive execution scenarios: https://github.com/dotnet/fsharp/issues/9397#issuecomment-648376476
            thr.Abort() // raises OperationCanceledException on Netframework and would raise Platfrom-not-supported-Exception on net60
            #endif           
        |None -> ()

        let thread = 
            new Thread(new ThreadStart( fun () ->    
                // http://reedcopsey.com/2011/11/28/launching-a-wpf-window-in-a-separate-thread-part-1/
                // Create our context, and install it:
                let ctx = new DispatcherSynchronizationContext( Dispatcher.CurrentDispatcher)
                asyncContext <- Some (ctx:>SynchronizationContext)
                SynchronizationContext.SetSynchronizationContext( new DispatcherSynchronizationContext( Dispatcher.CurrentDispatcher))
                onShutDownThread.Add ( fun _ ->  
                    asyncContext <- None
                    asyncThread <- None
                    Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background) // TODO does this fail if it is shut down already ??
                    ) 
                // Start the Dispatcher Processing
                System.Windows.Threading.Dispatcher.Run()
                ))
        thread.SetApartmentState(ApartmentState.STA)
        thread.IsBackground <- true
        thread.Start()        
        asyncThread <- Some thread
    
    (* does not work somehow see issues, just set System.Environment.CurrentDirectory instead on every tab change !?
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
    *)  
    
    let createSession() =         
        let fsiArgs =
            // first arg is ignored:
            //      https://github.com/fsharp/FSharp.Compiler.Service/issues/420
            // and  https://github.com/fsharp/FSharp.Compiler.Service/issues/877
            // and  https://github.com/fsharp/FSharp.Compiler.Service/issues/878        
            // "--shadowcopyreferences" is ignored https://github.com/fsharp/FSharp.Compiler.Service/issues/292
            let args = config.FsiArguments.Get            
            let beQuiet = config.Settings.GetBool ("fsiOutputQuiet", false)
            let qargs = 
                match beQuiet, args |> Array.tryFindIndex (fun s -> s="--quiet") with 
                | true , Some _  -> args
                | false, None    -> args
                | true , None    -> Array.append args [| "--quiet"|] // TODO or fsi.ShowDeclarationValues <- false ??
                | false , Some i -> args |> Array.removeAt i            
            if config.Hosting.IsRunningOnDotNetCore then // --multiemit is always there on netCore
                qargs 
            else
                match qargs |> Array.tryFindIndex (fun s -> s="--multiemit") with 
                | Some _  -> qargs
                | None    -> Array.append qargs [| "--multiemit"|] // to have line numbers in exceptions: https://github.com/dotnet/fsharp/discussions/13293
        
        let fsiConfig = 
            let fsiObj = Interactive.Shell.Settings.fsi
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
            //settings.ShowDeclarationValues <- true // use this instead of switching the quiet flag ?
            fsiObj.PrintWidth <- 200 //TODO adapt to Log view size taking fontsize into account
            fsiObj.FloatingPointFormat <- "g7" 
            fsiObj.AddPrinter<DateTime>(fun d -> if d.Hour=0 && d.Minute=0 && d.Second = 0 then d.ToString("yyyy-MM-dd") else d.ToString("yyyy-MM-dd HH:mm:ss"))
            // https://github.com/dotnet/fsharp/blob/4978145c8516351b1338262b6b9bdf2d0372e757/src/fsharp/fsi/fsi.fs#L2839
            FsiEvaluationSession.GetDefaultConfiguration(fsiObj, useFsiAuxLib = false) // useFsiAuxLib = FSharp.Compiler.Interactive.Settings.dll . But it is missing in FCS !! 

        let inStream = new StringReader("")
        //for i,ar in Seq.indexed fsiArgs  do ISeffLog.log.PrintfnDebugMsg $"{i} arg: {ar} "
        if config.Hosting.IsStandalone then  
            FsiEvaluationSession.Create(fsiConfig, fsiArgs, inStream, log.TextWriterFsiStdOut, log.TextWriterFsiErrorOut) //, collectible=false ??) //https://github.com/dotnet/fsharp/blob/6b0719845c928361e63f6e38a9cce4ae7d621fbf/src/fsharp/fsi/fsi.fs#L2440
        else
            (*  This is needed since FCS 34. it solves https://github.com/dotnet/fsharp/issues/9064
            FCS takes the current Directory wich might be the one of the hosting App and will then probaly not contain FSharp.Core.
            at https://github.com/dotnet/fsharp/blob/7b46dad60df8da830dcc398c0d4a66f6cdf75cb1/src/Compiler/Interactive/fsi.fs#L3213   *)
            let prevDir = Environment.CurrentDirectory
            Directory.SetCurrentDirectory(Path.GetDirectoryName(Reflection.Assembly.GetAssembly([].GetType()).Location))    
            let fsiSession = FsiEvaluationSession.Create(fsiConfig, fsiArgs, inStream, log.TextWriterFsiStdOut, log.TextWriterFsiErrorOut) //, collectible=false ??) //https://github.com/dotnet/fsharp/blob/6b0719845c928361e63f6e38a9cce4ae7d621fbf/src/fsharp/fsi/fsi.fs#L2440
            Directory.SetCurrentDirectory(prevDir)
            fsiSession 
 

    [< Security.SecurityCritical >] 
    [< Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions >] //to handle AccessViolationException too //https://stackoverflow.com/questions/3469368/how-to-handle-accessviolationexception/4759831
    //This construct is deprecated in net6.0 . Recovery from corrupted process state exceptions is not supported; HandleProcessCorruptedStateExceptionsAttribute is ignored.
    let evalSave (sess:FsiEvaluationSession,code:string)=
        sess.EvalInteractionNonThrowing(code) //,fsiCancelScr.Value.Token)   // cancellation token here fails to cancel in sync, might still throw OperationCanceledException if async

    let rec initFsi() :unit = 
        match state with
        | Initializing -> log.PrintfnInfoMsg "FSI initialization can't be started because it is already in process.."
        | NotLoaded | Ready | Evaluating ->
            let  prevState = state
            state <- Initializing
            async{
                //let timer = Seff.Timer()
                //timer.tic()
                if config.Settings.GetBool ("asyncFsi", true) then mode <- asyncMode else mode <- FsiMode.Sync
                match sessionOpt with
                |None -> ()
                |Some session -> session.Interrupt()  //TODO does this cancel running session correctly ?? // TODO how to dispose previous session ?  Thread.Abort() ??  
                
                
                sessionOpt <- Some <| createSession()

                //fsiSession.Run() // TODO ? dont do this it crashes the app when hosted in Rhino!
                //timer.stop()

                match prevState with
                |Initializing |Ready |Evaluating -> log.PrintfnInfoMsg "FSharp Interactive session reset." // in %s" timer.tocEx
                |NotLoaded  ->                     () //log.PrintfnInfoMsg "FSharp 40.0 Interactive session created." // in %s"  timer.tocEx

                (*
                if config.Hosting.IsHosted then
                    match mode with
                    |Sync ->               log.PrintfnInfoMsg "FSharp Interactive will evaluate synchronously on UI Thread."
                    |Async472| Async60 ->  log.PrintfnInfoMsg "FSharp Interactive will evaluate asynchronously on a new Thread with ApartmentState.STA."
                else
                    log.PrintfnInfoMsg "FSharp Interactive will evaluate asynchronously on a new Thread with ApartmentState.STA."
                *)                

                match mode with
                |Sync ->   () 
                |Async472| Async60 ->  abortThenMakeAndStartAsyncThread()

                do! Async.SwitchToContext SyncWpf.context
                match pendingEval with 
                |None -> 
                    state <- Ready
                    isReadyEv.Trigger()
                |Some ctE -> 
                    pendingEval <- None
                    eval(ctE)                
                }
                |> Async.Start      
        

    and eval(codeToEv:CodeToEval) :unit = 
        let avaEd = codeToEv.editor.AvaEdit
        let fsCode =             
            match codeToEv.amount with
            |All -> avaEd.Text
            |ContinueFromChanges ->
                let from = codeToEv.editor.EvaluateFrom
                let len = avaEd.Document.TextLength - from
                if len > 0 then avaEd.Document.GetText(from , len )
                else "" // ContinueFromChanges reached end, all of document is evaluated
            | FsiSegment seg -> seg.text

        if not(String.IsNullOrWhiteSpace fsCode) then
            if not config.Hosting.FsiCanRun then
                log.PrintfnAppErrorMsg "The Hosting App has blocked Fsi from Running, maybe because the App is busy in another command or task."
            else
                match sessionOpt with
                |None ->
                    pendingEval <- Some codeToEv
                    //startedEv.Trigger(codeToEv) //  to show "FSI is running" immediately , even while initalizing?
                    //initFsi()  //dont ! not needed !, setting pendingEval is enough
                    //previously: log.PrintfnFsiErrorMsg "Please wait till FSI is initialized for running scripts"

                |Some session ->
                    state <- Evaluating
                    startedEv.Trigger(codeToEv) // do always sync, to show "FSI is running" immediately

                    let asyncEval = async{
                        
                        // set context this or other async thread: 
                        match mode with 
                        |Sync -> 
                            do! Async.Sleep 1 // this helps to show "FSI is running" immediately
                            do! Async.SwitchToContext SyncWpf.context
                        |Async472| Async60 -> 
                            match asyncContext,asyncThread with 
                            |Some actx , Some athr  when athr.IsAlive ->                             
                                do! Async.SwitchToContext actx
                            | _ -> 
                                // this should never happen actually:
                                ISeffLog.log.PrintfnInfoMsg "asyncContext is None or asyncThread is not alive. was there a thread.Abort() ?"
                                abortThenMakeAndStartAsyncThread()
                                match asyncContext,asyncThread with 
                                |Some actx2 , Some athr  when athr.IsAlive ->                              
                                    do! Async.SwitchToContext actx2
                                |_ -> 
                                    ISeffLog.log.PrintfnFsiErrorMsg "asyncContext is None or asyncThread is not alive. abortMakeAndStartAsyncThread() cannot create it either! evaluation happens in sync"
                                    do! Async.SwitchToContext SyncWpf.context

                        //Done already at startup in Initialize.fs, not needed here? AppDomain.CurrentDomain is the same ? 
                        //if notNull Application.Current then // null if application is not yet created, or no application in hosted context
                        //    Application.Current.DispatcherUnhandledException.Add(fun e ->  //exceptions generated on the UI thread // TODO really do this on every evaluation?
                        //        log.PrintfnAppErrorMsg "Application.Current.DispatcherUnhandledException in fsi thread: %A" e.Exception
                        //        e.Handled <- true)
                        //AppDomain.CurrentDomain.UnhandledException.AddHandler (//catching un-handled exceptions generated from all threads running under the context of a specific application domain. //https://dzone.com/articles/order-chaos-handling-unhandled
                        //    new UnhandledExceptionEventHandler( (new ProcessCorruptedState(config)).Handler)) //https://stackoverflow.com/questions/14711633/my-c-sharp-application-is-returning-0xe0434352-to-windows-task-scheduler-but-it


                        // set current dir, file and Topline TODO
                        // TODO https://github.com/dotnet/fsharp/blob/6b0719845c928361e63f6e38a9cce4ae7d621fbf/src/fsharp/fsi/fsi.fs#L2618
                        // change via reflection???
                        // let dummyScriptFileName = "input.fsx"
                        //match codeToEv.editor.FilePath with
                        //| NotSet -> () //setFileAndLine session code.fromLine "Unnamed File"
                        //| SetTo fi ->
                            //let line = 
                            // match codeToEv.amount with
                            // |All -> 1
                            // |ContinueFromChanges ->  ed.Document.GetLineByOffset ...
                            // | FsiSegment seg -> seg.line
                            //setDir session fi
                            //setFileAndLine session code.fromLine fi // TODO both fail ??                                                  
                        

                        let evaluatedTo, errs = 
                            try evalSave(session,fsCode) 
                            with e -> Choice2Of2 e , [| |]
                        
                        // switch back to sync Thread:
                        match mode with 
                        |Sync -> ()
                        |Async472| Async60 -> do! Async.SwitchToContext SyncWpf.context
                        
                        state <- Ready //TODO reached when canceled ? or wrap in try..finally.. ?

                        match evaluatedTo with //TODO move out of this thread?
                        |Choice1Of2 evaluatedToValue ->
                            completedOkEv.Trigger(codeToEv)
                            isReadyEv.Trigger()
                            for e in errs do
                                match e.Severity with
                                | FSharpDiagnosticSeverity.Error  ->  log.PrintfnAppErrorMsg "EvalInteractionNonThrowing returned Error: %s" e.Message 
                                | FSharpDiagnosticSeverity.Warning -> () //log.PrintfnInfoMsg "EvalInteractionNonThrowing returned Warning: %s" e.Message
                                | FSharpDiagnosticSeverity.Hidden  -> () //log.PrintfnInfoMsg "EvalInteractionNonThrowing returned Hidden: %s" e.Message
                                | FSharpDiagnosticSeverity.Info   ->  () //log.PrintfnInfoMsg "EvalInteractionNonThrowing returned Info: %s" e.Message

                            //match evaluatedToValue with   //|Some v -> log.PrintfnDebugMsg "Interaction evaluated to %A <%A>" v.ReflectionValue v.ReflectionType //|None-> ()
                            if config.Settings.GetBoolSaveDefault("printDoneAfterEval",true) then  log.PrintfnInfoMsg "*Done!"

                        |Choice2Of2 exn ->
                            match exn with
                            | :? OperationCanceledException ->
                                canceledEv.Trigger(codeToEv)
                                isReadyEv.Trigger()
                                if config.Hosting.IsHosted && mode = FsiMode.Async472 && isNull exn.StackTrace  then
                                    log.PrintfnFsiErrorMsg "FSI evaluation was canceled,\r\nif you did not trigger this cancellation try running FSI in Synchronous evaluation mode (instead of Async)."
                                else
                                    log.PrintfnInfoMsg "FSI evaluation was canceled by user!" //:\r\n%A" exn.StackTrace  //: %A" exn

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
                                            "  For assembly reference errors that are not shown by editor tooling try to re-arrange the initial loading sequences of '#r' statements\n\r" +
                                            "  This error might happen when you are loading a dll with #r that is already loaded, but from a different location\n\r" +
                                            "  E.G. as a dependency from a already loaded dll."
                                    log.PrintfnFsiErrorMsg "%A" e
                                if postMsg <> "" then                                    
                                    log.PrintfnFsiErrorMsg "%s" postMsg

                            | _ ->
                                runtimeErrorEv.Trigger(exn)  // in seff.fs this is used to ensure the main window is visible, because it might be hidden manually, or not visible from the start ( e.g. current script is evaluated in Seff.Rhino)                             
                                log.PrintfnAppErrorMsg "Runtime Error:"
                                let et = sprintf "%A" exn
                                for ln in et.Split('\n')  do 
                                    if ln.Contains ".fsx:" then 
                                        log.PrintfnFsiErrorMsg "%s" (ln.TrimEnd())
                                        // go to error line number
                                        let _,lr = Str.splitOnce ".fsx:" ln
                                        match Int32.TryParse (lr.Replace("line","").Trim()) with 
                                        |true , i -> 
                                            //log.PrintfnDebugMsg "Going to line %d" i
                                            GoTo.line(i,codeToEv.editor)
                                        |_ -> 
                                            log.PrintfnDebugMsg "no int in  '%s'" lr
                                            ()                                        
                                    else
                                        log.PrintfnRuntimeErr "%s" (ln.TrimEnd())
                                (*
                                let printRuntimeError s = log.PrintfnColor 200 0 0 s
                                printRuntimeError "Runtime Error:"
                                //highlight line number in blue in error message: 
                                let et = sprintf "%A" exn
                                let t,r = Str.splitOnce ".fsx:" et
                                if r="" then
                                    printRuntimeError "%s" et
                                else
                                    let ln,rr = Str.splitOnce "\r\n" r
                                    printRuntimeError "%s.fsx:" t
                                    log.PrintfnColor 0 0 200 "%s" ln
                                    printRuntimeError "%s" rr
                                *)
                                
                                isReadyEv.Trigger()
                        }
                    Async.StartImmediate(asyncEval)
                   

    static let mutable singleInstance:Fsi option  = None

    /// ensures only one instance is created
    static member GetOrCreate(config:Config) :Fsi = 
        match singleInstance with
        |Some fsi -> fsi
        |None -> singleInstance <- Some (new Fsi(config)); singleInstance.Value


    //-------------- public interface: ---------

    member this.State = state

    member this.Mode = mode

    /// starts a new Fsi session
    member this.Initialize() =  initFsi() // Checker class will call this after first run of checker, to start fsi when checker is  idle

    member this.CancelIfAsync() = 
        match state  with
        | Ready | Initializing | NotLoaded -> ()
        | Evaluating ->
            match mode with
            |Sync -> () //don't block event completion by doing some debug logging. TODO test how to log !//log.PrintfnInfoMsg "Current synchronous Fsi Interaction cannot be canceled"     // UI for this only available in asynchronous mode anyway, see Commands
            |Async60 |Async472 -> 
                abortThenMakeAndStartAsyncThread()
                state  <- Ready
                //isReadyEv.Trigger() // TODO needed


    member this.AskIfCancellingIsOk() = 
        match state with
        | Ready | Initializing | NotLoaded -> NotEvaluating
        | Evaluating ->
            match mode with
            |Sync -> NotPossibleSync
            |Async60 -> NoAsync60
            |Async472 ->
                match MessageBox.Show("Do you want to Cancel currently running code?", "Cancel Current Evaluation?", MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.No) with
                | MessageBoxResult.Yes ->
                    match state with // might have changed in the meantime of Message box show
                    | Ready | Initializing | NotLoaded -> NotEvaluating
                    | Evaluating -> YesAsync472
                | MessageBoxResult.No | _ ->
                    match state with // might have changed in the meantime of Message box show
                    | Ready | Initializing | NotLoaded -> NotEvaluating
                    | Evaluating -> UserDoesntWantTo


    ///shows UI to confirm canceling, returns new state
    member this.AskAndCancel() = 
        match this.AskIfCancellingIsOk () with
        | NotEvaluating    -> Ready
        | YesAsync472      -> this.CancelIfAsync();Ready
        | NoAsync60        -> Evaluating
        | UserDoesntWantTo -> Evaluating
        | NotPossibleSync  -> Evaluating     // UI for this only available in asynchronous mode anyway, see Commands

    member this.Evaluate(code:CodeToEval) = 
        //if DateTime.Today > DateTime(2022, 12, 30) then 
        //    log.PrintfnFsiErrorMsg "*** Your Seff Editor has expired, please download a new version. or contact goswin@rothenthal.com ***"        
        //else
            //if DateTime.Today > DateTime(2022, 10, 30) then log.PrintfnFsiErrorMsg "*** Your Seff Editor will expire on 2022-12-31, please download a new version soon. or contact goswin@rothenthal.com***"            
            match this.AskIfCancellingIsOk () with
            | NotEvaluating   -> eval(code)
            | YesAsync472     -> this.CancelIfAsync();eval(code)
            | NoAsync60       -> log.PrintfnInfoMsg "Wait till current async evaluation on net6.0 completes before starting new one."
            | UserDoesntWantTo-> ()
            | NotPossibleSync -> log.PrintfnInfoMsg "Wait till current synchronous evaluation completes before starting new one."


    member this.Reset() = 
        match this.AskIfCancellingIsOk () with
        | NotEvaluating   ->                       initFsi (); resetEv.Trigger() 
        | YesAsync472     -> this.CancelIfAsync(); initFsi (); resetEv.Trigger()
        | NoAsync60       -> log.PrintfnInfoMsg "ResetFsi is not be possible in current async evaluation on net50." // TODO test
        | UserDoesntWantTo-> ()
        | NotPossibleSync -> log.PrintfnInfoMsg "ResetFsi is not be possible in current synchronous evaluation." // TODO test


    member this.SetMode(sync:FsiMode) = 
        let setConfig()= 
            match mode with
            |Sync             ->  config.Settings.SetBool ("asyncFsi", false)    |> ignore
            |Async472| Async60 -> config.Settings.SetBool ("asyncFsi", true)     |> ignore

        match this.AskIfCancellingIsOk() with
        | NotEvaluating | YesAsync472    ->
            mode <- sync
            modeChangedEv.Trigger(sync)
            setConfig()
            config.Settings.Save()
            initFsi ()
        | UserDoesntWantTo -> ()
        | NoAsync60       -> log.PrintfnInfoMsg "Wait till current async evaluation on net6.0 completes before setting mode to sync."// TODO test
        | NotPossibleSync -> log.PrintfnInfoMsg "Wait till current synchronous evaluation completes before setting mode to Async."

    member this.ToggleSync()= 
        match mode with
        |Async472| Async60  ->  this.SetMode FsiMode.Sync
        |Sync               ->  this.SetMode asyncMode


    ///Triggered whenever code is sent to Fsi for evaluation
    [<CLIEvent>]
    member this.OnStarted = startedEv.Publish

    /// Interactive evaluation was canceled because of a runtime error
    [<CLIEvent>]
    member this.OnRuntimeError = runtimeErrorEv.Publish

    /// Interactive evaluation was canceled by user (e.g. by pressing Esc)
    [<CLIEvent>]
    member this.OnCanceled = canceledEv.Publish

    /// This event will  trigger after successful completion, NOT on runtime error or canceling of Fsi
    [<CLIEvent>]
    member this.OnCompletedOk = completedOkEv.Publish

    /// This event will trigger on the end of each fsi session,
    /// so after completion, runtime error or canceling of Fsi
    [<CLIEvent>]
    member this.OnIsReady  = isReadyEv.Publish

    /// This event will be trigger after Fsi is reset
    [<CLIEvent>]
    member this.OnReset  = resetEv.Publish

    ///Triggered whenever Fsi for evaluation mode changes between Sync and Async
    [<CLIEvent>]
    member this.OnModeChanged = modeChangedEv.Publish

    /// used to clean up other threads when seff is shutting down
    member this.TriggerShutDownThreadEv() = 
        // see http://reedcopsey.com/2011/11/28/launching-a-wpf-window-in-a-separate-thread-part-1/
        shutDownThreadEv.Trigger()




