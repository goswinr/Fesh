
namespace Seff

open System
open System.Windows
open Seff.Model
open Seff.Util.General
open Seff.Util

module GlobalErrorHandeling = 
    
    let maxThrowCount = 50    

    let mutable throwCount = 0

    
    /// A class to provide an Error Handler that can catch currupted state or access violation errors frim FSI threads too
    type ProcessCorruptedState(log:ISeffLog) =      
        // TODO ingerate handler info FSI
        [< Security.SecurityCritical >]//to handle AccessViolationException too 
        [< Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions >] //https://stackoverflow.com/questions/3469368/how-to-handle-accessviolationexception/4759831
        member this.Handler (sender:obj) (e: UnhandledExceptionEventArgs) = 
                // Starting with the .NET Framework 4, this event is not raised for exceptions that corrupt the state of the process, 
                // such as stack overflows or access violations, unless the event handler is security-critical and has the HandleProcessCorruptedStateExceptionsAttribute attribute.
                // https://docs.microsoft.com/en-us/dotnet/api/system.appdomain.unhandledexception?redirectedfrom=MSDN&view=netframework-4.8
                // https://docs.microsoft.com/en-us/archive/msdn-magazine/2009/february/clr-inside-out-handling-corrupted-state-exceptions
                let time = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss-fff")
                let err = sprintf "ProcessCorruptedState Special Handler: AppDomain.CurrentDomain.UnhandledException: isTerminating: %b : time: %s\r\n%A" e.IsTerminating time e.ExceptionObject
                async {                
                    let filename = sprintf "Seff-UnhandledException-%s.txt" time
                    let file = IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),filename)
                    try  IO.File.WriteAllText(file, err) with _ -> () // file might be open and locked
                    } |> Async.Start
                log.PrintAppErrorMsg "%s" err



    let setup(log:ISeffLog) = 
        
        if notNull Application.Current then // null if application is not yet created, or no application in hoted context
            Application.Current.DispatcherUnhandledException.Add(fun e ->  
                let mutable print = true
                if print then 
                    if throwCount < maxThrowCount then // reduce printing to Log UI, it might crash from printing too much
                        throwCount <- throwCount + 1                
                        if e <> null then 
                            log.PrintAppErrorMsg "Application.Current.DispatcherUnhandledException in main Thread: %A" e.Exception           
                            e.Handled<- true
                        else
                            log.PrintAppErrorMsg "Application.Current.DispatcherUnhandledException in main Thread: *null* Exception Obejct"
                    else 
                        print <- false
                        log.PrintAppErrorMsg "\r\nMORE THAN %d Application.Current.DispatcherUnhandledExceptions"    maxThrowCount
                        log.PrintAppErrorMsg "\r\n\r\n   *** LOGGING STOPPED. CLEAR LOG FIRST TO START PRINTING AGAIN *** "    
                         )
        
        //catching unhandled exceptions generated from all threads running under the context of a specific application domain. 
        //https://dzone.com/articles/order-chaos-handling-unhandled
        //https://stackoverflow.com/questions/14711633/my-c-sharp-application-is-returning-0xe0434352-to-windows-task-scheduler-but-it  
        
        AppDomain.CurrentDomain.UnhandledException.AddHandler( new UnhandledExceptionEventHandler( ProcessCorruptedState(log).Handler))   

