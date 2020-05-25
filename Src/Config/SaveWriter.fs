namespace Seff.Config

open Seff
open System
open System.Threading


/// Writes Async and with ReaderWriterLock, 
/// Optionally only once after a delay in wich it might be called several times
type SaveWriter  (log:ISeffLog)= 
    
    let counter = ref 0L // for atomic writing back to file
    
    let readerWriterLock = new ReaderWriterLockSlim()

    member this.Write (path, text) = 
        async{
            readerWriterLock.EnterWriteLock()
            try
                try
                    IO.File.WriteAllText(path,text)
                with ex ->            
                    log.PrintIOErrorMsg "Write.toFileAsyncLocked failed with: %A \r\n while writing:\r\n%s" ex text
            finally
                readerWriterLock.ExitWriteLock()
            } |> Async.Start

      
    /// getString will only be called (in sync) and file will only be written (async)
    ///if after the delay the counter value is the same as before. 
    ///( that means no more recent calls to this function have been made)
    member this.WriteDelayed (path, getText: unit->string, delay) =
        async{
            let k = Interlocked.Increment counter
            do! Async.Sleep(delay) // delay to see if this is the last of many events (otherwise there is a noticable lag in dragging window around)
            if !counter = k then //k > 2L &&   //do not save on startup && only save last event after a delay if there are many save events in a row ( eg from window size change)(ignore first two event from creating window)
                try 
                    let text = getText()               
                    this.Write (path, text) 
                with ex -> 
                    log.PrintAppErrorMsg "getText() in Write.toFileDelayed failed: %A" ex                 
            } |> Async.StartImmediate
        
    
