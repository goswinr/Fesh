namespace Seff

open System

()

// performance timer, unused currently


/// A performance timer that also measures Garbage Collection Generations.
/// includes nice formatting of ms , sec, and minutes
/// Similar to the #time;; statement built in to FSI
/// The timer starts immediately when created
type Timer() = 

    // taken from FsiTimeReporter at https://github.com/dotnet/fsharp/blob/master/src/fsharp/fsi/fsi.fs#L183

    let numGC = System.GC.MaxGeneration

    let formatGCs prevGC = 
        prevGC
        |> Array.fold (fun (i,txt) _ -> i+1, sprintf "%s  G%d: %d" txt i (System.GC.CollectionCount(i) - prevGC.[i]) ) (0," ; ") //"GC:")
        |> snd


    let formatMilliSeconds ms = 
        if ms < 0.001 then "less than 0.001 ms"
        elif ms < 1.0 then sprintf "%.3f ms" (ms)       //less than 1  millisec
        elif ms < 10. then sprintf "%.2f ms" (ms)       //less than 10 millisec
        elif ms < 1e3 then sprintf "%.1f ms" ms         //less than 1  sec
        elif ms < 1e4 then sprintf "%.2f sec" (ms/1e3)  //less than 10 sec
        elif ms < 6e4 then sprintf "%.1f sec" (ms/1e3)  //less than 1 min
        else      sprintf "%.0f min %.0f sec" (Math.Floor (ms/6e4)) ((ms % 6e4)/1e3)

    let ticWithGC (sw:Diagnostics.Stopwatch) (kGC:int[]) = 
        sw.Reset();  GC.Collect() ;  GC.WaitForPendingFinalizers()
        for i=0 to numGC do kGC.[i] <- GC.CollectionCount(i) // reset GC counter base
        sw.Start()

    let tocWithGC (sw:Diagnostics.Stopwatch) countGC = 
        sw.Stop()
        let txt = sprintf "%s, %s" (formatMilliSeconds sw.Elapsed.TotalMilliseconds) (formatGCs countGC)
        for i=0 to numGC do countGC.[i] <- GC.CollectionCount(i) // reset GC counter base
        sw.Reset()
        sw.Start()
        txt

    let tocNoGC (sw:Diagnostics.Stopwatch) = 
        sw.Stop()
        let txt = formatMilliSeconds sw.Elapsed.TotalMilliseconds
        sw.Reset()
        GC.Collect()
        GC.WaitForPendingFinalizers()
        sw.Start()
        txt

    let kGC = [| for i in 0 .. numGC -> GC.CollectionCount(i) |]


    let stopWatch = new Diagnostics.Stopwatch()

    do ticWithGC stopWatch kGC // start stopwatch immediately

    ///Returns time since last tic (or toc) as string, resets clock
    member this.tocEx = tocWithGC stopWatch kGC

    ///Returns time since last tic (or toc) as string, resets clock
    member this.toc = tocNoGC stopWatch

    ///Reset and start Timer
    member this.tic() =  ticWithGC stopWatch kGC

    ///Stops Timer
    member this.stop() =  stopWatch.Stop()

    /// an instance of a timer to be used to measure startup performance
    static member val InstanceStartup = Timer()

    /// an instance of a timer to be used to measure reddraw performance
    static member val InstanceRedraw = Timer()

