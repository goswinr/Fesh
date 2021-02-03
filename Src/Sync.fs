﻿namespace Seff

open System
open System.Threading
open System.Windows.Threading


type Sync private () =    
    
    static let mutable ctx : SynchronizationContext = null  // will be set in main UI STAThread    

    /// the UI SynchronizationContext to switch to inside async CEs
    static member syncContext = ctx

    /// to ensure SynchronizationContext is set up.
    static member  installSynchronizationContext () =         
        if SynchronizationContext.Current = null then 
            DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher) |> SynchronizationContext.SetSynchronizationContext
        ctx <- SynchronizationContext.Current
        
        if isNull ctx then 
            // reporting this to the UI instead would not work since there is no sync context for the UI
            let time = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss-fff") // to ensure unique file names  
            let filename = sprintf "Seff-SynchronizationContext failed-%s.txt" time
            let file = IO.Path.Combine(GlobalErrorHandeling.desktop,filename)
            try IO.File.WriteAllText(file, "Failed to get DispatcherSynchronizationContext") with _ -> () // file might be open or locked    


    
    static member doSync (f) = 
        async {
            do! Async.SwitchToContext ctx
            f()
            } |> Async.StartImmediate

        // see https://github.com/fsprojects/FsXaml/blob/c0979473eddf424f7df83e1b9222a8ca9707c45a/src/FsXaml.Wpf/Utilities.fs#L132

    


    (* not needed ?: https://stackoverflow.com/questions/61227071/f-async-switchtocontext-vs-dispatcher-invoke

    /// evaluates a function on UI thread
    let doSync f args = 
        async{  do! Async.SwitchToContext syncContext
                return f args
             } |> Async.RunSynchronously
    

    
    ///evaluates the given function application on the UI thread
    let postToUI : ('a -> 'b) -> 'a -> 'b =
        fun f x ->
          //app.Dispatcher.Invoke(new System.Func<_>(fun () -> f x), [||]) 
          Dispatcher.CurrentDispatcher.Invoke(new System.Func<_>(fun () -> f x), [||])  // from https://www.ffconsultancy.com/products/fsharp_journal/subscribers/FSharpIDE.html        
          |> unbox
    
    
    *)




