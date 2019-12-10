namespace Seff


open System
open System.Windows
open System.IO
open System.Threading
open FSharp.Compiler.Interactive.Shell



module HostUndoRedo = 
    let mutable beginUndo = fun ()          -> 0u // to preserev UNDO history when hosted in other app set in showHostedEditor
    let mutable endUndo   = fun (i:uint32)  -> () // https://github.com/mcneel/rhinocommon/blob/57c3967e33d18205efbe6a14db488319c276cbee/dotnet/rhino/rhinosdkdoc.cs#L857
    let mutable undoIndex = 0u


module Fsi =    
    
    [<AbstractClass; Sealed>]
    /// A static class to hold events 
    type Events private () =
        
        static let runtimeErrorEv = new Event<Exception>() 
        static let canceledEv = new Event<unit>()  

        static member runtimeError = runtimeErrorEv        
        [<CLIEvent>]
        static member RuntimeError = runtimeErrorEv.Publish
                
        static member canceled = canceledEv        
        [<CLIEvent>]
        static member Canceled = canceledEv.Publish


    //mostly taken from: https://github.com/ionide/FsInteractiveService/blob/master/src/FsInteractiveService/Main.fs

    type AgentMessage = 
        |Evaluate of string
        |Restart 
        |Cancel
        |Done
    
    type FsiState = Ready|Evaluating|HadError

    type FsiStatus () =
        static let mutable isEval = Ready
        static member Evaluation  // this is checked in main Window.Closing event
            with get() = isEval
            and set(s) = 
                if s <> isEval then //TODO create event instead of doing UI changes here?
                    async{  
                        do! Async.SwitchToContext Sync.syncContext
                        match s with
                        |Ready ->      UI.log.Background <- Appearance.logBackgroundFsiReady
                        |Evaluating -> UI.log.Background <- Appearance.logBackgroundFsiEvaluating
                        |HadError ->   UI.log.Background <- Appearance.logBackgroundFsiHadError
                        } |> Async.StartImmediate
                    isEval <-s


    let private timer = Util.Timer()

    let private startSession () =
        timer.tic()
        let inStream = new StringReader("")
        // first arg is ignored: https://github.com/fsharp/FSharp.Compiler.Service/issues/420 and https://github.com/fsharp/FSharp.Compiler.Service/issues/877
        let allArgs = [|"" ; "--langversion:preview" ; "--noninteractive"|] //;"--nologo";"--gui-"|] // --gui: Enables or disables the Windows Forms event loop. The default is enabled.
        //let fsiObj = FSharp.Compiler.Interactive.Shell.Settings.fsi // needed ?
        let fsiConfig = FsiEvaluationSession.GetDefaultConfiguration() //(fsiObj, false)
        let fsiSession = FsiEvaluationSession.Create(fsiConfig, allArgs, inStream, Log.textwriter, Log.textwriter)
        AppDomain.CurrentDomain.UnhandledException.Add(fun ex -> Log.printf "*** FSI background exception:\r\n %A" ex.ExceptionObject) 
  
        Log.printf "* Time for loading FSharp Interactive: %s"  timer.tocEx             
        //fsiSession.Run()// needed ?
        fsiSession    


    let private evaluate (thrO: Thread option, code:string, session:FsiEvaluationSession, inbox: MailboxProcessor<AgentMessage>)= 
        let eval () =             
            Console.SetOut  (Log.textwriter) //TODO only redidirect printfn ? //https://github.com/fsharp/FSharp.Compiler.Service/issues/201
            Console.SetError(Log.textwriter) //TODO or evaluate non throwing ?
            let thr = 
                new Thread(fun () ->
                    HostUndoRedo.undoIndex <- HostUndoRedo.beginUndo()                    
                    try
                        try
                            FsiStatus.Evaluation <- Evaluating
                            //Log.printf "* Evaluating code.."
                            timer.tic()                            
                            if Config.currentRunContext <> Config.RunContext.Standalone then  // TODO when hosted evaluate on UI thread only
                                // this sync switch does not work well for Rhino:
                                //async{  do! Async.SwitchToContext Sync.syncContext
                                //        session.EvalInteraction(code) } |> Async.RunSynchronously
                                session.EvalInteraction(code + Config.codeToAppendEvaluations)
                            else
                                session.EvalInteraction(code + Config.codeToAppendEvaluations)
                            //Log.printf "* Code evaluated in %s" timer.tocEx
                            FsiStatus.Evaluation <- Ready
                        
                        with 
                        | :? OperationCanceledException ->
                            Events.canceled.Trigger()
                            FsiStatus.Evaluation <- Ready
                            Log.printf "**FSI evaluation was cancelled**" //Thread aborted by user
                            
                        | e ->                               
                            Events.runtimeError.Trigger(e)
                            FsiStatus.Evaluation <- HadError
                            //Log.printf "*** Exception (caught in Evaluation): \r\n %A" e // TODO not needed because error stream is redirected to Log too ??                            
                    
                    finally                        
                        HostUndoRedo.endUndo(HostUndoRedo.undoIndex)
                        inbox.Post(Done) // to set thread to None. does thread need to be aborted too?                         
                    )
            thr.Start()
            Some thr
        
        // check for running evaluation sessions first:
        match thrO with 
        | Some thr ->
            match MessageBox.Show("Do you want to Cancel currently running code?","Cancel Current Evaluation?",MessageBoxButton.YesNo,MessageBoxImage.Exclamation,MessageBoxResult.No) with
            | MessageBoxResult.Yes -> thr.Abort(); eval ()
            | _ -> thrO        
        | _ -> eval ()

    let agent =        
        let mb = new MailboxProcessor<AgentMessage>(fun inbox -> //TODO why not make this all synchronous  without MBP?
            let rec running symbols thread session = async {
                let! msg = inbox.Receive()        
                match msg, (thread:Thread option), symbols with

                | Evaluate(code), thr, sy ->                    
                    let thr = evaluate (thr, code, session, inbox)
                    return! running None thr session 

                | Cancel, Some thr, _ ->
                    //session.Interrupt()
                    thr.Abort()                    
                    FsiStatus.Evaluation <- Ready
                    Log.printf "FSharp Interactive Session canceled ..."
                    return! running None None session 

                // Thread completed or cancelling but no thread is running
                | Done  ,  _   , _  ->
                    return! running None None session 
                | Cancel, None , _ ->
                    //FsiStatus.Evaluation <- Ready
                    return! running None None session 

                // Reset F# Interactive session
                | Restart, Some thr , _ ->
                    //session.Interrupt()  
                    thr.Abort()                                      
                    FsiStatus.Evaluation <- Ready
                    Log.printf "cancelling and restarting..."
                    return! running None None (startSession())
                | Restart, None , _ ->
                    //session.Interrupt() 
                    FsiStatus.Evaluation <- Ready
                    return! running None None (startSession())           
            }
            running None None (startSession()))
        mb.Error.Add (fun ex -> Log.printf "*** Exception raised in Fsi Mailboxprocessor: %A" ex)
        //mb.Start() //do later, after window loading for hosted context
        mb
    
    let clearLog() =         
        UI.log.Clear()
        match FsiStatus.Evaluation with
        |Ready -> ()
        |Evaluating -> ()
        |HadError ->   UI.log.Background <- Appearance.logBackgroundFsiReady // clearing log should remove red error color too.

    
    //based on http://www.ffconsultancy.com/products/fsharp_journal/subscribers/FSharpIDE.html
    //The following agent compiles source code in the background and posts errors back to the UI thread:
    //When a message is received the inbox is "drained" by receiving all messages in the inbox except the most recent. Compilation 
    //is done in our background worker agent by calling our compile function and a function is then run on the UI thread to update 
    //the contents of the error list and the mapping from index to source code position.

    //type AgentMessage = | Evaluate of string
    //let agent=
    //    MailboxProcessor.Start(fun inbox ->
    //        let rec drain msg =
    //            async { let! msg2 = inbox.TryReceive 0
    //                    match msg2 with
    //                    | None -> return msg
    //                    | Some msg2 -> return! drain msg2 }
    //        async { 
    //            while true do
    //                let! msg = inbox.Receive()
    //                let! msg = drain msg
    //                match msg with
    //                | Evaluate code -> ()
    //                }
    //    )



