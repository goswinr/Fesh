namespace Seff.Config

open Seff
open Seff.Model
open System
open System.Threading


/// Writes Async and with ReaderWriterLock, 
/// Optionally only once after a delay in wich it might be called several times
type SaveWriter  (log:ISeffLog)= 
    
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
        
    
