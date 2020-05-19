namespace Seff.Config

open System
open Seff



type Config (log:ISeffLog, context:HostingMode, startupArgs:string[]) =
    
    let hostInfo = HostingInfo(context)

    member val Settings                   = Settings                    (log, hostInfo)
    member val RecentlyUsedFiles          = RecentlyUsedFiles           (log, hostInfo)
    member val OpenTabs                   = OpenTabs                    (log, hostInfo, startupArgs)
    member val DefaultCode                = DefaultCode                 (log, hostInfo)
    member val AutoCompleteStatistic      = AutoCompleteStatistic       (log, hostInfo)
    member val AssemblyReferenceStatistic = AssemblyReferenceStatistic  (log, hostInfo)
    member val HostingInfo                = hostInfo      
    member val Log                        = log 

    
    
/// A class to provide an Error Handler that can catch currupted state or access violation errors frim FSI threads too
type ProcessCorruptedState(config:Config) =  
        
    
    // TODO ingerate handler info FSI
    [< Security.SecurityCritical; Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions >] //to handle AccessViolationException too //https://stackoverflow.com/questions/3469368/how-to-handle-accessviolationexception/4759831
    member this.Handler (sender:obj) (e: UnhandledExceptionEventArgs) = 
            // Starting with the .NET Framework 4, this event is not raised for exceptions that corrupt the state of the process, 
            // such as stack overflows or access violations, unless the event handler is security-critical and has the HandleProcessCorruptedStateExceptionsAttribute attribute.
            // https://docs.microsoft.com/en-us/dotnet/api/system.appdomain.unhandledexception?redirectedfrom=MSDN&view=netframework-4.8
            // https://docs.microsoft.com/en-us/archive/msdn-magazine/2009/february/clr-inside-out-handling-corrupted-state-exceptions
            let err = sprintf "ProcessCorruptedState Special Handler: AppDomain.CurrentDomain.UnhandledException: isTerminating: %b : %A" e.IsTerminating e.ExceptionObject
            let file = IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),"Seff-AppDomain.CurrentDomain.UnhandledException.txt")
            async { IO.File.WriteAllText(file, err)} |> Async.Start
            config.Log.PrintAppErrorMsg "%s" err
       


