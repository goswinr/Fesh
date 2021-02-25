
namespace Seff

open System
open System.Windows
open Seff.Model
open Seff.Util.General
open Seff.Util

module GlobalErrorHandeling = 
    
    let maxThrowCount = 50    

    let mutable throwCount = 0

    let desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
    
    /// A class to provide an Error Handler that can catch currupted state or access violation errors frim FSI threads too
    type ProcessCorruptedState(log:ISeffLog) =  
    
        // TODO intgerate handler info FSI
        [< Security.SecurityCritical >]//to handle AccessViolationException too 
        [< Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions >] //https://stackoverflow.com/questions/3469368/how-to-handle-accessviolationexception/4759831
        member this.Handler (sender:obj) (e: UnhandledExceptionEventArgs) = 
                // Starting with the .NET Framework 4, this event is not raised for exceptions that corrupt the state of the process, 
                // such as stack overflows or access violations, unless the event handler is security-critical and has the HandleProcessCorruptedStateExceptionsAttribute attribute.
                // https://docs.microsoft.com/en-us/dotnet/api/system.appdomain.unhandledexception?redirectedfrom=MSDN&view=netframework-4.8
                // https://docs.microsoft.com/en-us/archive/msdn-magazine/2009/february/clr-inside-out-handling-corrupted-state-exceptions
                // https://stackoverflow.com/questions/39956163/gracefully-handling-corrupted-state-exceptions
                
                let time = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss-fff")// to ensure unique file names  
                let filename = sprintf "Seff-UnhandledException-%s.txt" time
                let file = IO.Path.Combine(desktop,filename)

                //let textInLog =  log.ReadOnlyDoc.CreateSnapshot().Text /// this takes too long "Seff-UnhandledException-%s.txt will not be written at all.
                //let err = sprintf "ProcessCorruptedState Special Handler: AppDomain.CurrentDomain.UnhandledException: \r\nisTerminating: %b : \r\ntime: %s\r\n%A\r\n\r\nText in Log:\r\n\r\n%s" e.IsTerminating time e.ExceptionObject textInLog

                let fsiErrorStream = log.FsiErrorStream.ToString() // to catch errors there too
                let err = sprintf "ProcessCorruptedState Special Handler: AppDomain.CurrentDomain.UnhandledException: \r\nisTerminating: %b : \r\ntime: %s\r\n\r\n%A\r\n\r\nFSI Error Stream:\r\n%s" e.IsTerminating time e.ExceptionObject fsiErrorStream
                
                //let err = sprintf "ProcessCorruptedState Special Handler: AppDomain.CurrentDomain.UnhandledException: \r\nisTerminating: %b : \r\ntime: %s\r\n%A" e.IsTerminating time e.ExceptionObject 
                
                //async { 
                try IO.File.WriteAllText(file, err) with _ -> () // file might be open and locked               
                //    } |> Async.Start

                log.PrintfnAppErrorMsg "%s" err
  

    let setup(log:ISeffLog) = 
        
        if notNull Application.Current then // null if application is not yet created, or no application in hoted context
            Application.Current.DispatcherUnhandledException.Add(fun e ->  
                let mutable print = true
                if print then 
                    if throwCount < maxThrowCount then // reduce printing to Log UI, it might crash from printing too much
                        throwCount <- throwCount + 1                
                        if e <> null then 
                            log.PrintfnAppErrorMsg "Application.Current.DispatcherUnhandledException in main Thread:\r\n%A" e.Exception           
                            e.Handled<- true
                        else
                            log.PrintfnAppErrorMsg "Application.Current.DispatcherUnhandledException in main Thread: *null* Exception Obejct"
                    else 
                        print <- false
                        log.PrintfnAppErrorMsg "\r\nMORE THAN %d Application.Current.DispatcherUnhandledExceptions"    maxThrowCount
                        log.PrintfnAppErrorMsg "\r\n\r\n   *** LOGGING STOPPED. CLEAR LOG FIRST TO START PRINTING AGAIN *** "    
                         )
        
        //catching unhandled exceptions generated from all threads running under the context of a specific application domain. 
        //https://dzone.com/articles/order-chaos-handling-unhandled
        //https://stackoverflow.com/questions/14711633/my-c-sharp-application-is-returning-0xe0434352-to-windows-task-scheduler-but-it  
        
        AppDomain.CurrentDomain.UnhandledException.AddHandler( new UnhandledExceptionEventHandler( ProcessCorruptedState(log).Handler))   
        
        (*

        // https://stackoverflow.com/questions/56105293/accessviolationexception-was-unhandled-how-do-i-implement-handleprocesscorrupt
        [STAThread]
        [HandleProcessCorruptedStateExceptions, SecurityCritical]
        static void Main()
        {
            try
            {
                // add UnhandledException handler
                // AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;
                // * in this particular case is not quite useful to handle this exceptions,
                //   because you already wrap your entire application in a try/catch block
        
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                // handle it somehow
            }
            *)