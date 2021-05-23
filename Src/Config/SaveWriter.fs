namespace Seff.Config

open Seff
open Seff.Model
open System
open System.IO
open System.Runtime.CompilerServices
open System.Threading


/// This module is set to auto open. 
/// Static Extension methods on Exceptions to cal Excn.Raise "%A" x with F# printf string formating
[<AutoOpen>] // so that extension become availale on opening FsEx
module ExtensionsExceptions =
                        
    type FileNotFoundException with
        /// Raise FileNotFoundException with F# printf string formating
        [<Extension>] static member inline Raise msg =  Printf.kprintf (fun s -> raise (FileNotFoundException(s))) msg        
    
/// Reads and Writes with Lock, 
/// Optionally only once after a delay in which it might be called several times
type SaveReadWriter (path:string)= 
    // similar class also exist in FsEx , FsEx.Wpf and Seff
       
    let counter = ref 0L // for atomic writing back to file
       
    let lockObj = new Object()
    
    /// TODO take this class via FsEx.WPF

    member this.FileExists() = File.Exists(path)

    /// Save reading
    /// Ensures that no writing happens while reading
    member this.ReadAllText () : string =
        // lock is using Monitor class : https://github.com/dotnet/fsharp/blob/6d91b3759affe3320e48f12becbbbca493574b22/src/fsharp/FSharp.Core/prim-types.fs#L4793
        lock lockObj (fun () -> File.ReadAllText(path))
            
    
    /// Save reading
    /// Ensures that no writing happens while reading
    member this.ReadAllLines () : string[] =
        // lock is using Monitor class : https://github.com/dotnet/fsharp/blob/6d91b3759affe3320e48f12becbbbca493574b22/src/fsharp/FSharp.Core/prim-types.fs#L4793
        lock lockObj (fun () -> File.ReadAllLines(path))


    /// File will be written async and with a Lock.
    /// If it fails an Error is printed to the Error stream via eprintfn
    /// Ensures that no reading happens while writing
    member this.WriteAsync (text) =        
        async{
            lock lockObj (fun () -> // lock is using Monitor class : https://github.com/dotnet/fsharp/blob/6d91b3759affe3320e48f12becbbbca493574b22/src/fsharp/FSharp.Core/prim-types.fs#L4793
                try  File.WriteAllText(path,text)
                // try & with is needed because exceptions on threadpool cannot be caught otherwise !!
                with ex ->  ISeffLog.log.PrintfnIOErrorMsg "SaveWriter.WriteAsync failed with: %A \r\n while writing to %s:\r\n%A" ex path text // use %A to trimm long text        
                )       
            } |> Async.Start
    
    /// File will be written async and with a Lock.
    /// If it fails an Error is printed to the Error stream via eprintfn
    /// Ensures that no reading happens while writing
    member this.WriteAllLinesAsync (texts) =        
        async{
            lock lockObj (fun () -> // lock is using Monitor class : https://github.com/dotnet/fsharp/blob/6d91b3759affe3320e48f12becbbbca493574b22/src/fsharp/FSharp.Core/prim-types.fs#L4793
                try  File.WriteAllLines(path,texts)
                // try & with is needed because exceptions on threadpool cannot be caught otherwise !!
                with ex ->  ISeffLog.log.PrintfnIOErrorMsg "SaveWriter.WriteAllLinesAsync failed with: %A \r\n while writing to %s:\r\n%A" ex path texts // use %A to trimm long text        
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
                    let text = getText() // this may fail though
                    this.WriteAsync (text) // this should never fail since exeptions are caught inside 
                with ex -> 
                    // try & with is needed because exceptions on threadpool cannot be caught otherwise !!
                    ISeffLog.log.PrintfnIOErrorMsg "SaveWriter.WriteIfLast: getText() for path (%s) failed with: %A" path ex                 
            } |> Async.StartImmediate            