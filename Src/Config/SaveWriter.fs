namespace Seff.Config

open Seff
open Seff.Model
open System
open System.Threading


/// Writes Async and with ReaderWriterLock, 
/// Optionally only once after a delay in wich it might be called several times
type SaveWriterOLD  (log:ISeffLog)= /// TODO delete
    
    let counter = ref 0L // for atomic writing back to file
    
    let readerWriterLock = new ReaderWriterLockSlim()

    /// File will  be written async.
    member this.Write (path, text) = 
        async{
            readerWriterLock.EnterWriteLock()
            try
                try
                    IO.File.WriteAllText(path,text)
                with ex ->            
                    log.PrintfnIOErrorMsg "Write.toFileAsyncLocked failed with: %A \r\n while writing:\r\n%s" ex text
            finally
                readerWriterLock.ExitWriteLock()
            } |> Async.Start

      
    /// GetString will be called in sync on calling thread, but file will be written async.
    /// Only if after the delay the counter value is the same as before. 
    /// (that means no more recent calls to this function have been made during the delay)
    member this.WriteDelayed (path, getText: unit->string, delayMillisSeconds:int) =
        async{
            let k = Interlocked.Increment counter
            do! Async.Sleep(delayMillisSeconds) // delay to see if this is the last of many events (otherwise there is a noticable lag in dragging window around, for example, when saving window position)
            if !counter = k then //k > 2L &&   //do not save on startup && only save last event after a delay if there are many save events in a row ( eg from window size change)(ignore first two event from creating window)
                try 
                    let text = getText()               
                    this.Write (path, text) 
                with ex -> 
                    log.PrintfnAppErrorMsg "getText() or Write to (%s) in Write.toFileDelayed failed: %A" path ex                 
            } |> Async.StartImmediate
        
    
/// Reads and Writes with Lock, 
/// Optionally only once after a delay in which it might be called several times
type SaveReadWriter (path:string)= 
    // simiar class also exist in FsEx.Wpf
       
    let counter = ref 0L // for atomic writing back to file
       
    let lockObj = new Object()
    
    member this.FileExists() = IO.File.Exists(path)

    /// Save reading
    /// Ensures that no writing happens while reading
    member this.ReadAllText () : string =
            // lock is using Monitor class : https://github.com/dotnet/fsharp/blob/6d91b3759affe3320e48f12becbbbca493574b22/src/fsharp/FSharp.Core/prim-types.fs#L4793
            try  
                lock lockObj (fun () -> IO.File.ReadAllText(path))
            with ex ->  
                failwithf "SaveWriter.Read failed while reading:\r\n%s\r\n with: %A" path ex // use %A to trimm long text 
    
    /// Save reading
    /// Ensures that no writing happens while reading
    member this.ReadAllLines () : string[] =
            // lock is using Monitor class : https://github.com/dotnet/fsharp/blob/6d91b3759affe3320e48f12becbbbca493574b22/src/fsharp/FSharp.Core/prim-types.fs#L4793
            try  
                lock lockObj (fun () -> IO.File.ReadAllLines(path))
            with ex ->  
                failwithf "SaveWriter.Read failed while reading:\r\n%s\r\n with: %A" path ex // use %A to trimm long text 

    /// File will be written async and with a Lock.
    /// If it fails an Error is printed to the Error stream via eprintfn
    /// Ensures that no reading happens while writing
    member this.WriteAsync (text) =        
        async{
            lock lockObj (fun () -> // lock is using Monitor class : https://github.com/dotnet/fsharp/blob/6d91b3759affe3320e48f12becbbbca493574b22/src/fsharp/FSharp.Core/prim-types.fs#L4793
                try  IO.File.WriteAllText(path,text)
                with ex ->  eprintfn "SaveWriter.WriteAsync failed with: %A \r\n while writing to %s:\r\n%A" ex path text // use %A to trimm long text        
                )       
            } |> Async.Start
    
    /// File will be written async and with a Lock.
    /// If it fails an Error is printed to the Error stream via eprintfn
    /// Ensures that no reading happens while writing
    member this.WriteAllLinesAsync (texts) =        
        async{
            lock lockObj (fun () -> // lock is using Monitor class : https://github.com/dotnet/fsharp/blob/6d91b3759affe3320e48f12becbbbca493574b22/src/fsharp/FSharp.Core/prim-types.fs#L4793
                try  IO.File.WriteAllLines(path,texts)
                with ex ->  eprintfn "SaveWriter.WriteAllLinesAsync failed with: %A \r\n while writing to %s:\r\n%A" ex path texts // use %A to trimm long text        
                )       
            } |> Async.Start
         
    /// GetString will be called in sync on calling thread, but file will be written async.
    /// Only if after the delay the counter value is the same as before. 
    /// That means no more recent calls to this function have been made during the delay.
    /// If other calls to this function have been made then only the last call will be written as file
    /// If it fails an Error is printed to the Error stream via eprintfn
    /// Also ensures that no reading happens while writing
    member this.WriteIfLast ( getText: unit->string, delayMillisSeconds:int) =
        async{
            let k = Interlocked.Increment counter
            do! Async.Sleep(delayMillisSeconds) // delay to see if this is the last of many events (otherwise there is a noticable lag in dragging window around, for example, when saving window position)
            if !counter = k then //k > 2L &&   //do not save on startup && only save last event after a delay if there are many save events in a row ( eg from window size change)(ignore first two event from creating window)
                try 
                    let text = getText()               
                    this.WriteAsync (text) // this should never fail since exeptions are caught inside 
                with ex -> 
                    eprintfn "SaveWriter.WriteIfLast: getText() for path (%s) failed with: %A" path ex                 
            } |> Async.StartImmediate            