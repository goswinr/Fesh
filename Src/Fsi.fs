namespace Fesh

open System
open System.IO
open System.Threading
open System.Windows

open Fesh.Model
open Fesh.Config
open Fesh.Util

open Fittings
open FSharp.Compiler
open FSharp.Compiler.Interactive.Shell
open FSharp.Compiler.Diagnostics
open System.Windows.Threading

type FsiState =
    Ready | Compiling| Evaluating | Initializing | NotLoaded

type FsiSyncMode  =
    InSync | AsyncMode

    member this.IsAsync =
        match this with
        | InSync -> false
        | AsyncMode -> true

type FsiIsCancelingIsOk =
    | NotEvaluating // Nothing can be cancelled because no evaluation is running
    | YesAsync // an async evaluation on net472 or net48 is running, it can be cancelled via thread.Abort(), on net7 can be cancelled via ControlledExecution.Run
    | UserDoesntWantTo // Don't cancel because the user actually doesn't want to cancel
    | NotPossibleSync // Not-Possible-Sync because during sync eval the UI should be frozen anyway and this request should not be happening


module GoTo =

    /// open any foldings if required and don't select at location
    let errorLine (lineNumber :int, ied:IEditor) =
        //this implementation is similar to Foldings.GoToLineAndUnfold
        let ava = ied.AvaEdit
        let ln = ava.Document.GetLineByNumber(lineNumber)
        //let mutable unfoldedOneOrMore = false
        for fold in ied.FoldingManager.GetFoldingsContaining(ln.Offset) do
            if fold.IsFolded then
                fold.IsFolded <- false
                //unfoldedOneOrMore <- true
        ava.ScrollTo(ln.LineNumber,1)
        ied.AvaEdit.CaretOffset<- ln.Offset // done by ied.AvaEdit.Select too
        //ied.AvaEdit.CaretOffset<- loc.EndOffset // done by ied.AvaEdit.Select too
        // ava.Select(ln.Offset, ln.Length)

//for: HandleProcessCorruptedStateExceptionsAttribute: This construct is deprecated. Recovery from corrupted process state exceptions is not supported; HandleProcessCorruptedStateExceptionsAttribute is ignored.
//and for : Runtime.ControlledExecution.Run
//#nowarn "44"
open System.Reflection

type Fsi private (config:Config) =
    let log = config.Log

    ///FSI events
    let compilingEv      = new Event<CodeToEval>()
    let emittingEv       = new Event<CodeToEval>()
    let canceledEv       = new Event<unit>()
    let completedOkEv    = new Event<CodeToEval>()
    let runtimeErrorEv   = new Event<Exception>()
    let fsiEvalErrorEv    = new Event<FSharpDiagnostic>()
    let isReadyEv        = new Event<unit>()
    let resetEv          = new Event<unit>()
    let modeChangedEv    = new Event<FsiSyncMode>()

    let shutDownThreadEv = new Event<unit>()
    let onShutDownThread = shutDownThreadEv.Publish

    let mutable state = NotLoaded

    let mutable mode = if config.RunContext.IsHosted then InSync else AsyncMode

    let mutable sessionOpt : FsiEvaluationSession option = None

    let mutable asyncContext : option<SynchronizationContext> = None

    let mutable asyncThread: option<Thread> = None

    let mutable pendingEval :option<CodeToEval> = None // for storing evaluations that are triggered before fsi is ready

    //let mutable codeInEval : option<CodeToEval> = None
    // let _ = // just for OnEmitting Event !!
    //     // ActivityStopped: Reflection.Emit // second last event
    //     // ActivityStarted: Run Bindings // last event
    //     Diagnostics.ActivitySource.AddActivityListener(
    //         new Diagnostics.ActivityListener(
    //             ShouldListenTo  = (fun a -> a.Name = ActivityNames.ProfiledSourceName),
    //             Sample          = (fun _ -> Diagnostics.ActivitySamplingResult.AllData),
    //             //ActivityStopped = (fun a -> if a.OperationName = "Run Bindings" then useNext <- true ), // because this is the only event not raised twice
    //             ActivityStarted = (fun a ->
    //                 if a.OperationName = "Run Bindings" then
    //                     async{
    //                         do! Async.Sleep 300 // wait 300 ms t see if evaluation has not yet completed
    //                         if state = Compiling  then
    //                             match codeInEval with
    //                             | None -> ()
    //                             | Some cie ->
    //                                     do! Async.SwitchToContext SyncWpf.context
    //                                     state <- Evaluating
    //                                     emittingEv.Trigger(cie)
    //                                     codeInEval <- None
    //                     } |> Async.StartImmediate
    //             )
    //         )
    //     )


    // will point to the token in https://github.com/dotnet/fsharp/blob/main/src/Compiler/Interactive/ControlledExecution.fs
    let mutable fscCancellationToken : CancellationTokenSource voption = ValueNone

    // TODO: use non-interactive flag instead of accessing controlled execution via reflection:
    // https://github.com/dotnet/fsharp/pull/15184

    let setAControlledExecutionCancellationToken()=
        if config.RunContext.IsRunningOnDotNetCore then
            match sessionOpt with
            |None ->
                IFeshLog.log.PrintfnFsiErrorMsg "Getting FSI token setter failed, no session to abort"
            |Some session ->
                fscCancellationToken <- ValueSome (new CancellationTokenSource())
                try
                    let flags = BindingFlags.NonPublic ||| BindingFlags.Instance
                    let fsiInterruptController = session.GetType().GetField( "fsiInterruptController" ,flags).GetValue(session)
                    let controlledExecution = fsiInterruptController.GetType().GetField( "controlledExecution" ,flags).GetValue(fsiInterruptController)
                    controlledExecution.GetType().GetField("cts",flags).SetValue(controlledExecution, fscCancellationToken)
                    //so that this triggers: https://github.com/dotnet/fsharp/blob/9db7f5b109f17746467531779ce6c9226b6d60b9/src/Compiler/Interactive/ControlledExecution.fs#L48
                    controlledExecution.GetType().GetField("isInteractive",flags).SetValue(controlledExecution, true)
                with e ->
                    IFeshLog.log.PrintfnFsiErrorMsg "Getting FSI token via reflection form Fsharp.Compiler.Service failed"

    /// returns tru if something was aborted
    let getFrameworkAgnosticAborter(thread:Thread) : unit -> bool =
        if config.RunContext.IsRunningOnDotNetCore then
            match sessionOpt with
            |None ->
                IFeshLog.log.PrintfnFsiErrorMsg "Getting FSI aborter failed, no session to abort"
                fun () -> false
            |Some session ->
                try
                    let flags = BindingFlags.NonPublic ||| BindingFlags.Instance
                    let fsiInterruptController = session.GetType().GetField( "fsiInterruptController" ,flags).GetValue(session)
                    let controlledExecution = fsiInterruptController.GetType().GetField( "controlledExecution" ,flags).GetValue(fsiInterruptController)
                    let cts = controlledExecution.GetType().GetField("cts",flags).GetValue(controlledExecution)
                    fun ()  ->
                        // tryAbortInfo.Invoke(controlledExecution, null) |> ignore
                        match (cts:?> ValueOption<Threading.CancellationTokenSource>) with
                        | ValueNone -> // the token is None initially when creating a session.
                            // a reset of FSI would always print this:
                            //IFeshLog.log.PrintfnFsiErrorMsg "No cancellation token for ControlledExecution found. Cancelling running scripts might not work."
                            false
                        | ValueSome ctk ->
                            ctk.Cancel()
                            true
                with e ->
                    IFeshLog.log.PrintfnFsiErrorMsg "Getting FSI aborter via reflection form Fsharp.Compiler.Service failed"
                    fun () -> false
        else
            fun () ->
                #if NETFRAMEWORK //the definition of NETFRAMEWORK is only needed to avoid a compiler error on netCore, the actual runtime detection happens in Config.fs
                    thread.Abort()
                    match state with
                    | Ready | Initializing | NotLoaded -> false
                    | Compiling | Evaluating ->  true
                #else
                    ignore thread // this ignore only exists to the avoid then warning when NETFRAMEWORK is not defined
                    false
                #endif

    let abortThenMakeAndStartAsyncThread() =
        // shutDownThreadEv.Trigger() // don't do this ! this shuts down all of Fesh !!
        // Use Interrupt instead of Abort  ? see https://github.com/dotnet/fsharp/pull/14546#pullrequestreview-1240043309

        match asyncThread with
        |None -> ()
        |Some thread -> // _ = thread
            asyncContext <- None
            asyncThread  <- None
            let aborter = getFrameworkAgnosticAborter(thread)
            if aborter() then
                asyncThread <- None
                SyncWpf.doSync( fun () ->
                    canceledEv.Trigger()
                    log.PrintfnInfoMsg "\r\nFSI evaluation was canceled by user!"
                    )

            // if not config.RunContext.IsRunningOnDotNetCore then
            //     thread.Abort() // raises OperationCanceledException on NetFramework and would raise Platform-not-supported-Exception on net60
            //     // Thread.Abort method is not supported in .NET 6 https://github.com/dotnet/runtime/issues/41291  but in there is a new way in net7 !
            //     // Don Syme: Thread.Abort - it is needed in interruptible interactive execution scenarios: https://github.com/dotnet/fsharp/issues/9397#issuecomment-648376476
            // else

            //     // TODO in the standalone version using the cancellation token should work too
            //     // also see https://github.com/dotnet/fsharp/issues/14486

            //     // net7cancellationToken.Cancel()
            //     // net7cancellationToken <- new CancellationTokenSource()
            //     // use reflection to access https://github.com/dotnet/fsharp/blob/main/src/Compiler/Interactive/ControlledExecution.fs ?
            //     // https://github.com/dotnet/fsharp/issues/14489
            //     // https://github.com/dotnet/fsharp/pull/14546
            //     // https://github.com/dotnet/fsharp/discussions/14491

        if asyncThread.IsNone then
            let nextThread =
                new Thread(new ThreadStart(
                    fun () ->
                        // Create our context, and install it: http://reedcopsey.com/2011/11/28/launching-a-wpf-window-in-a-separate-thread-part-1/
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
                    )
                )

            if not config.RunContext.IsRunningOnDotNetCore then
                nextThread.SetApartmentState(ApartmentState.STA) // works only on net48? so that the thread can create WPF windows.

            nextThread.IsBackground <- true
            nextThread.Start()
            asyncThread <- Some nextThread

            // do here because it seems that OperationCanceledException caught in handeleEvaluationResult is not thrown anymore, just thread stopped, on net 48
            state <- Ready
            SyncWpf.doSync( fun () -> isReadyEv.Trigger())

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
            // see: Config/FsiArguments.fs
            let args = config.FsiArguments.Get
            let beQuiet = config.Settings.GetBool ("fsiOutputQuiet", false)

            match beQuiet, args |> Array.tryFindIndex (fun s -> s="--quiet") with
            | true , Some _  -> args
            | false, None    -> args
            | true , None    -> Array.append args [| "--quiet"|] // TODO or fsi.ShowDeclarationValues <- false ??
            | false, Some i  -> args |> Array.removeAt i


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
            fsiObj.PrintWidth <- 200 //TODO adapt to Log view size taking font size into account
            fsiObj.FloatingPointFormat <- "g7"
            fsiObj.AddPrinter<DateTime>(fun d -> if d.Hour=0 && d.Minute=0 && d.Second = 0 then d.ToString "yyyy-MM-dd" else d.ToString "yyyy-MM-dd HH:mm:ss")
            fsiObj.AddPrinter<DateTimeOffset>(fun d -> d.ToString "yyyy-MM-dd HH:mm:ss K")

            // https://github.com/dotnet/fsharp/blob/4978145c8516351b1338262b6b9bdf2d0372e757/src/fsharp/fsi/fsi.fs#L2839
            FsiEvaluationSession.GetDefaultConfiguration(fsiObj, useFsiAuxLib = false) // useFsiAuxLib = FSharp.Compiler.Interactive.Settings.dll . But it is missing in FCS !!


        let inStream = new StringReader("")
        //for i,ar in Seq.indexed fsiArgs  do IFeshLog.log.PrintfnDebugMsg $"{i} arg: {ar} "
        if config.RunContext.IsStandalone then
            FsiEvaluationSession.Create(fsiConfig, fsiArgs, inStream, log.TextWriterFsiStdOut, log.TextWriterFsiErrorOut) //, collectible=false ??) //https://github.com/dotnet/fsharp/blob/6b0719845c928361e63f6e38a9cce4ae7d621fbf/src/fsharp/fsi/fsi.fs#L2440
        else
            (*  This is needed since FCS 34. it solves https://github.com/dotnet/fsharp/issues/9064
            FCS takes the current Directory which might be the one of the hosting App and will then probably not contain FSharp.Core.
            at https://github.com/dotnet/fsharp/blob/7b46dad60df8da830dcc398c0d4a66f6cdf75cb1/src/Compiler/Interactive/fsi.fs#L3213
            Cal this after all files are loaded and the current directory is set from the current tab, only then initialize FSI.
            If done earlier the current directory set by a tab might get lost in a race condition while creating the fsi session async.
            *)
            let prevDir = Environment.CurrentDirectory
            Directory.SetCurrentDirectory(Path.GetDirectoryName(Reflection.Assembly.GetAssembly([].GetType()).Location))
            let fsiSession = FsiEvaluationSession.Create(fsiConfig, fsiArgs, inStream, log.TextWriterFsiStdOut, log.TextWriterFsiErrorOut) //, collectible=false ??) //https://github.com/dotnet/fsharp/blob/6b0719845c928361e63f6e38a9cce4ae7d621fbf/src/fsharp/fsi/fsi.fs#L2440
            Directory.SetCurrentDirectory prevDir

            //fsiSession.Run() // don't call Run(), crashes app, done by WPF App.Run(). see https://github.com/dotnet/fsharp/issues/14486
            fsiSession

    let handeleEvaluationResult (evaluatedTo:Choice<FsiValue option,exn>, diagnostics: FSharpDiagnostic[], codeToEv:CodeToEval) =
        // switch back to sync Thread:
        async{
            match mode with
            |InSync -> ()
            |AsyncMode -> do! Async.SwitchToContext SyncWpf.context

            state <- Ready
            isReadyEv.Trigger()

            match evaluatedTo with //TODO move out of this thread?
            |Choice1Of2 _evaluatedToValue ->
                let errs = diagnostics |> Array.filter (fun e -> e.Severity = FSharpDiagnosticSeverity.Error )
                if errs.Length = 0 then
                    completedOkEv.Trigger codeToEv
                else
                    fsiEvalErrorEv.Trigger diagnostics.[0]


                for e in diagnostics do
                    match e.Severity with
                    | FSharpDiagnosticSeverity.Error   -> log.PrintfnAppErrorMsg "EvalInteractionNonThrowing returned Error:\r\n%s" e.Message
                    | FSharpDiagnosticSeverity.Warning -> () //log.PrintfnInfoMsg "EvalInteractionNonThrowing returned Warning: %s" e.Message
                    | FSharpDiagnosticSeverity.Hidden  -> () //log.PrintfnInfoMsg "EvalInteractionNonThrowing returned Hidden: %s" e.Message
                    | FSharpDiagnosticSeverity.Info    -> () //log.PrintfnInfoMsg "EvalInteractionNonThrowing returned Info: %s" e.Message

                //match evaluatedToValue with   //|Some v -> log.PrintfnDebugMsg "Interaction evaluated to %A <%A>" v.ReflectionValue v.ReflectionType //|None-> ()
                if config.Settings.GetBool("printDoneAfterEval",false) then log.PrintfnInfoMsg "*Done!"

            |Choice2Of2 exn ->
                match exn with
                | :? OperationCanceledException -> // only happens on net net 7+ when cancelling via Fesh ui
                    // thread.Abort raises a Threading.ThreadAbortException but it gets converted to a OperationCanceledException in FCS: fsi.fs line 3027
                    // FCS also handles the required ResetAbort:
                    // https://learn.microsoft.com/en-us/dotnet/api/system.threading.thread.abort?view=netframework-4.7.2#system-threading-thread-abort
                    // canceledEv.Trigger() // don in abortThenMakeAndStartAsyncThread()
                    if config.RunContext.IsHosted && mode = AsyncMode && isNull exn.StackTrace  then
                        log.PrintfnFsiErrorMsg "FSI evaluation was canceled,\r\nif you did not trigger this cancellation try running FSI in Synchronous evaluation mode (instead of Async)."


                | :? FsiCompilationException ->
                    runtimeErrorEv.Trigger(exn)
                    log.PrintfnFsiErrorMsg "Compiler Error:"
                    let es =
                        diagnostics
                        |> Array.map (sprintf "%A")
                        |> Array.distinct
                    for e in es do
                        log.PrintfnFsiErrorMsg "%s" e

                    if es|> Array.exists ( fun s -> s.Contains "is defined in an assembly that is not referenced." ) then
                        log.PrintfnInfoMsg "%s" "For assembly reference errors that are not shown by editor tooling try to re-arrange the initial loading sequences of '#r' statements"
                        log.PrintfnInfoMsg "%s" "This error might happen when you are loading a dll with #r that is already loaded, but from a different location"
                        log.PrintfnInfoMsg "%s" "E.G. as a dependency from a already loaded dll."


                | _ -> // any other runtime exception
                    runtimeErrorEv.Trigger(exn)  // in fesh.fs this is used to ensure the main window is visible, because it might be hidden manually, or not visible from the start ( e.g. current script is evaluated in Fesh.Rhino)
                    log.PrintfnAppErrorMsg "Runtime Error:"
                    match exn with
                    | :? Reflection.ReflectionTypeLoadException as ex ->
                            for le in ex.LoaderExceptions do log.PrintfnFsiErrorMsg $"{le}"
                    | _ -> ()

                    // find first error line in an fsx file
                    let et = sprintf "%A" exn
                    let mutable isFirstFsx = true
                    for ln in et.Split('\n')  do
                        if ln.Contains ".fsx:" && isFirstFsx then
                            isFirstFsx <- false
                            log.PrintfnFsiErrorMsg "%s" (ln.TrimEnd())
                            // go to first error line in an fsx file
                            let _,lr = Str.splitOnce ".fsx:" ln
                            match Int32.TryParse (lr.Replace("line","").Trim()) with
                            |true , i -> GoTo.errorLine(i,codeToEv.editor)
                            |_ -> ()
                        else
                            log.PrintfnRuntimeErr "%s" (ln.TrimEnd())

        } |> Async.StartImmediate

    #if NETFRAMEWORK //This construct is deprecated in net6.0 . Recovery from corrupted process state exceptions is not supported; HandleProcessCorruptedStateExceptionsAttribute is ignored.
    [< Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions >] //to handle AccessViolationExceptions too //https://stackoverflow.com/questions/3469368/how-to-handle-accessviolationexception/4759831
    #endif
    [< Security.SecurityCritical >]
    let evalSave (session:FsiEvaluationSession, code:string, codeToEv:CodeToEval) =
        // net472
        // Cancellation happens via Thread Abort
        // TODO actually using the token would work too but only if session.Run() has been called before, but that fails when hosted. see https://github.com/dotnet/fsharp/issues/14486
        // net6+ :
        // don't do System.Runtime.ControlledExecution.Run(action, net7cancellationToken.Token) // this is actually already done by FSI
        // when using: Run method: Compiler Error:input.fsx (1,1)-(1,1) interactive error internal error: The thread is already executing the ControlledExecution.Run method.
        // using  session.EvalInteractionNonThrowing(code, codeToEv.scriptName, net7cancellationToken.Token) the token does actually not cancel anything
        state <- Evaluating // actually this happens later better use: TODO: https://github.com/dotnet/fsharp/pull/15957
        let evaluatedTo, errs =
            try session.EvalInteractionNonThrowing(code, codeToEv.scriptName)
            with e -> Choice2Of2 e , [| |]
        handeleEvaluationResult(evaluatedTo, errs, codeToEv)

    let eval(codeToEv:CodeToEval) :unit =
        let avaEd = codeToEv.editor.AvaEdit
        let fsCode =
            match codeToEv.amount with
            |All -> avaEd.Text
            |ContinueFromChanges ->
                let fromLn = codeToEv.editor.EvaluateFromLine
                if fromLn = 0 then avaEd.Text
                else
                    let from = avaEd.Document.GetLineByNumber(fromLn).Offset
                    let len = avaEd.Document.TextLength - from
                    if len > 0 then avaEd.Document.GetText(from , len ) //|> (fun s -> printfn $"ContinueFromChanges ln: {fromLn}, off {from} to {len} :\r\n'{s}'" ; s)
                    else "" // ContinueFromChanges reached end, all of document is evaluated
            | FsiSegment seg -> seg.text

        if not(String.IsNullOrWhiteSpace fsCode) then
            if not config.RunContext.FsiCanRun then
                log.PrintfnAppErrorMsg "The Hosting App has blocked Fsi from Running, maybe because the App is busy in another command or task."
            else
                match sessionOpt with
                |None ->
                    pendingEval <- Some codeToEv
                    //compilingEv.Trigger(codeToEv) //  to show "FSI is running" immediately , even while initializing?
                    //initFsi()  //don't ! not needed !, setting pendingEval is enough
                    //previously: log.PrintfnFsiErrorMsg "Please wait till FSI is initialized for running scripts"

                |Some session ->
                    state <- Compiling
                    //codeInEval <- Some codeToEv
                    compilingEv.Trigger(codeToEv) // do always sync, to show "FSI is running" immediately

                    let asyncEval = async {
                        // set context this or other async thread:
                        match mode with
                        |InSync ->
                            do! Async.Sleep 1 // this helps to show "FSI is running" immediately in status bar
                            do! Async.SwitchToContext SyncWpf.context
                        |AsyncMode ->
                            match asyncContext, asyncThread with
                            |Some actx , Some athr  when athr.IsAlive ->
                                do! Async.SwitchToContext actx
                            | _ ->
                                // this should never happen actually:
                                IFeshLog.log.PrintfnInfoMsg "asyncContext is None or asyncThread is not alive. was there a thread.Abort() ?"
                                abortThenMakeAndStartAsyncThread()
                                match asyncContext, asyncThread with
                                |Some actx2 , Some athr  when athr.IsAlive ->
                                    do! Async.SwitchToContext actx2
                                |_ ->
                                    IFeshLog.log.PrintfnFsiErrorMsg "asyncContext is None or asyncThread is not alive."
                                    IFeshLog.log.PrintfnFsiErrorMsg "abortMakeAndStartAsyncThread() cannot create it either! evaluation happens in sync"
                                    do! Async.SwitchToContext SyncWpf.context

                        //Done already at startup in Initialize.fs, not needed here? AppDomain.CurrentDomain is the same ?
                        //if notNull Application.Current then // null if application is not yet created, or no application in hosted context
                        //    Application.Current.DispatcherUnhandledException.Add(fun e ->  //exceptions generated on the UI thread // TODO really do this on every evaluation?
                        //        log.PrintfnAppErrorMsg "Application.Current.DispatcherUnhandledException in fsi thread: %A" e.Exception
                        //        e.Handled <- true)
                        //AppDomain.CurrentDomain.UnhandledException.AddHandler (//catching un-handled exceptions generated from all threads running under the context of a specific application domain. //https://dzone.com/articles/order-chaos-handling-unhandled
                        //    new UnhandledExceptionEventHandler( (new ProcessCorruptedState(config)).Handler)) //https://stackoverflow.com/questions/14711633/my-c-sharp-application-is-returning-0xe0434352-to-windows-task-scheduler-but-it


                        // set current dir, file and top line TODO
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

                        evalSave(session, fsCode, codeToEv)
                        }
                    Async.StartImmediate(asyncEval)


    /// used for initial loading and  and reset!
    let initFsi(config:Config) :unit =
        match state with
        | Initializing                               -> log.PrintfnInfoMsg "FSI initialization can't be started because it is already in process.."
        | NotLoaded | Ready | Compiling | Evaluating ->
            let  prevState = state
            state <- Initializing
            async{
                try
                    //let timer = Fesh.Timer()
                    //timer.tic()
                    if config.Settings.GetBool ("asyncFsi", mode.IsAsync) then mode <- AsyncMode
                    else                                                       mode <- InSync

                    match sessionOpt with
                    |Some session -> session.Interrupt()  //TODO does this cancel running session correctly ?? // TODO how to dispose previous session ?  Thread.Abort() ??
                    |None -> ()

                    let fsiSession = createSession()
                    sessionOpt <- Some <| fsiSession

                    //timer.stop()

                    // fsiSession.Run()// don't do this, covered by WPF app loop:  https://github.com/dotnet/fsharp/issues/14486#issuecomment-1358310942
                    // This Run call crashes the app when hosted in Rhino and Standalone too !
                    // see https://github.com/dotnet/fsharp/issues/14486
                    // and https://github.com/dotnet/fsharp/blob/main/src/Compiler/Interactive/fsi.fs#L3759
                    // Is it needed to be able to cancel the evaluations in net7 and make the above net7cancellationToken work ??
                    // see https://github.com/dotnet/fsharp/pull/14546
                    // https://github.com/dotnet/fsharp/issues/14489


                    match prevState with
                    |Initializing |Ready | Compiling |Evaluating -> log.PrintfnInfoMsg "FSharp Interactive session reset." // in %s" timer.tocEx
                    |NotLoaded                                   -> () //log.PrintfnInfoMsg "Initial Interactive session created." // in %s"  timer.tocEx

                    (*
                    if config.RunContext.IsHosted then
                        match mode with
                        |InSync ->             log.PrintfnInfoMsg "FSharp Interactive will evaluate synchronously on UI Thread."
                        |Async472| Async60 ->  log.PrintfnInfoMsg "FSharp Interactive will evaluate asynchronously on a new Thread with ApartmentState.STA."
                    else
                        log.PrintfnInfoMsg "FSharp Interactive will evaluate asynchronously on a new Thread with ApartmentState.STA."
                    *)

                    // TODO what happens in Abort if current state is NotLoaded
                    match mode with
                    |InSync ->   ()
                    |AsyncMode ->
                        abortThenMakeAndStartAsyncThread()
                        setAControlledExecutionCancellationToken()

                    do! Async.SwitchToContext SyncWpf.context

                    match pendingEval with
                    |None ->
                        state <- Ready
                        isReadyEv.Trigger()
                    |Some ctE ->
                        pendingEval <- None
                        eval(ctE)

                with e -> // for example a  System.MissingMethodException when F# 8.0.2 is loaded but 8.0.4 required
                    state <- NotLoaded
                    sessionOpt <- None
                    log.PrintfnAppErrorMsg $"FSI initialization failed:\r\n{e}"
                    log.PrintfnFsiErrorMsg "Please report the issue on https://github.com/goswinr/Fesh/issues"
                    if config.RunContext.IsHosted then
                        let host = config.RunContext.HostName |> Option.defaultValue ""
                        log.PrintfnFsiErrorMsg $"Does the Hosting App {host} already hold a reference to an older version of FSharp.Core.dll ?"
                        log.PrintfnFsiErrorMsg $"Please try restarting the Hosting App {host}."
                        log.PrintfnFsiErrorMsg $"Then load the Fesh Editor first, that can solve assembly conflicts."
                    else
                        log.PrintfnFsiErrorMsg "Please try restarting Fesh."


                }
            |> Async.Start

    static let mutable singleInstance:Fsi option  = None

    /// ensures only one instance is created
    static member GetOrCreate(config:Config) :Fsi =
        match singleInstance with
        |Some fsi -> fsi
        |None -> singleInstance <- Some (new Fsi(config)); singleInstance.Value

    //------------------------------------------
    //-------------- public interface: ---------
    //------------------------------------------

    member this.State = state

    member this.Mode = mode

    /// starts a new Fsi session
    member this.Initialize() =  initFsi(config) // Checker class will call this after first run of checker, to start fsi when checker is  idle

    member this.CancelIfAsync() = // this is called directly from UI
        match state  with
        | Ready | Initializing | NotLoaded -> ()
        | Compiling | Evaluating ->
            match mode with
            | InSync -> () //don't block event completion by doing some debug logging. TODO test how to log !//log.PrintfnInfoMsg "Current synchronous Fsi Interaction cannot be canceled"     // UI for this only available in asynchronous mode anyway, see Commands
            | AsyncMode ->
                abortThenMakeAndStartAsyncThread()
                state  <- Ready
                //isReadyEv.Trigger() // TODO needed


    member this.AskIfCancellingIsOk() =
        match state with
        | Ready | Initializing | NotLoaded -> NotEvaluating
        | Compiling | Evaluating ->
            match mode with
            |InSync -> NotPossibleSync
            |AsyncMode  ->
                match MessageBox.Show(
                    IEditor.mainWindow,
                    "Do you want to Cancel currently running code?",
                    "Fesh | Cancel Current Evaluation?",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Exclamation,
                    MessageBoxResult.No, // default result
                    MessageBoxOptions.None) with
                | MessageBoxResult.Yes ->
                    match state with // might have changed in the meantime of Message box show
                    | Ready | Initializing | NotLoaded -> NotEvaluating
                    | Compiling | Evaluating -> YesAsync
                | MessageBoxResult.No | _ ->
                    match state with // might have changed in the meantime of Message box show
                    | Ready | Initializing | NotLoaded -> NotEvaluating
                    | Compiling | Evaluating -> UserDoesntWantTo

    // without this back and forth switch the UI freezes.
    // Use after showing the MessageBox.Show( "Do you want to Cancel currently running code?",
    member this.EvalDelayed(code)=
        async{
            do! Async.Sleep 20
            do! Async.SwitchToContext Fittings.SyncWpf.context
            eval(code)
        } |> Async.StartImmediate


    member this.Evaluate(code:CodeToEval) =
        if DateTime.Today > DateTime(2025, 12, 31) then
            log.PrintfnFsiErrorMsg "*** Your Fesh Editor has expired, please download a new version. ***"
            log.PrintfnFsiErrorMsg "*** https://github.com/goswinr/Fesh ***"
            log.PrintfnFsiErrorMsg "*** or contact goswin@rothenthal.com ***"
        else
            if DateTime.Today > DateTime(2025, 9, 30) then
                    log.PrintfnFsiErrorMsg "*** Your Fesh Editor will expire on 2025-12-31, please download a new version soon.***"
                    log.PrintfnFsiErrorMsg "*** https://github.com/goswinr/Fesh ***"
                    log.PrintfnFsiErrorMsg "*** or contact goswin@rothenthal.com ***"
            match this.AskIfCancellingIsOk () with
            | NotEvaluating    -> eval(code)
            | YesAsync         -> this.CancelIfAsync();this.EvalDelayed(code)
            | UserDoesntWantTo -> ()
            | NotPossibleSync  -> log.PrintfnInfoMsg "Wait till current synchronous evaluation completes before starting new one."


    member this.Reset() =
        match this.AskIfCancellingIsOk () with
        | NotEvaluating   ->                       initFsi (config); resetEv.Trigger()
        | YesAsync        -> this.CancelIfAsync(); initFsi (config); resetEv.Trigger()
        | UserDoesntWantTo-> ()
        | NotPossibleSync -> log.PrintfnInfoMsg "ResetFsi is not be possible in current synchronous evaluation." // TODO test


    member this.SetMode(sync:FsiSyncMode) =
        let setConfig()=
            match mode with
            |InSync    -> config.Settings.SetBool ("asyncFsi", false)    |> ignore
            |AsyncMode -> config.Settings.SetBool ("asyncFsi", true)     |> ignore

        match this.AskIfCancellingIsOk() with
        | NotEvaluating | YesAsync    ->
            mode <- sync
            modeChangedEv.Trigger(sync)
            setConfig()
            initFsi (config)
        | UserDoesntWantTo -> ()
        | NotPossibleSync -> log.PrintfnInfoMsg "Wait till current synchronous evaluation completes before setting mode to Async."

    member this.ToggleSync()=
        match mode with
        |AsyncMode  ->  this.SetMode InSync
        |InSync     ->  this.SetMode AsyncMode


    ///Triggered whenever code is sent to Fsi for compilation and evaluation, is followed by OnEmitting
    [<CLIEvent>]
    member this.OnCompiling = compilingEv.Publish

    ///Triggered whenever code sent has compiled and will be evaluated now
    [<CLIEvent>]
    member this.OnEmitting = emittingEv.Publish

    /// Interactive evaluation was canceled because of a runtime error
    [<CLIEvent>]
    member this.OnRuntimeError = runtimeErrorEv.Publish

    /// FSI evaluation returned an error
    [<CLIEvent>]
    member this.OnFsiEvalError = fsiEvalErrorEv.Publish

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

    /// used to clean up other threads when fesh is shutting down
    member this.TriggerShutDownThreadEv() =
        // see http://reedcopsey.com/2011/11/28/launching-a-wpf-window-in-a-separate-thread-part-1/
        shutDownThreadEv.Trigger()

    member this.Session = sessionOpt

    member this.ShutDown() = // to properly dispose the Fsi session in net8 Revit 2025?
        // in a race condition there might be a call to printfn, the buffer in AvalonLog would queue it, and wait for 50 ms,
        // but if within those 50ms the App shuts down it wil crash a hosting app such as Revit with a Thread cancelled Exception
        // In Revit 2025 this happens when closing the Fesh Editor, because some other plugins try to print to stdout at shout down.
        log.AvalonLog.IsAlive <- false // to stop logging
        match asyncThread with
        |Some thread ->
            let abort = getFrameworkAgnosticAborter(thread)
            abort()  |> ignore
        |None -> ()












