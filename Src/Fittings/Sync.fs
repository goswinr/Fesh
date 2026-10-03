namespace Fittings



open Avalonia.Threading

// https://docs.avaloniaui.net/docs/guides/development-guides/accessing-the-ui-thread
// https://reference.avaloniaui.net/api/Avalonia.Threading/AvaloniaSynchronizationContext/


module UiThread =

    // https://reference.avaloniaui.net/api/Avalonia.Threading/DispatcherPriority/
    // Dispatcher priority values
    // | Priority        | Value | Description                                                      |
    // |-----------------|-------|------------------------------------------------------------------|
    // | MinValue        | 1     | Minimum possible priority                                        |
    // | SystemIdle      | 1     | The job will be processed when the system is idle                |
    // | ApplicationIdle | 2     | The job will be processed when the application is idle           |
    // | ContextIdle     | 3     | The job will be processed after background operations completed  |
    // | Background      | 4     | The job will be processed after other non-idle operations        |
    // | Input           | 5     | The job will be processed with the same priority as input        |
    // | Loaded          | 6     | The job will be processed after layout and render before input   |
    // | Render          | 7     | The job will be processed with the same priority as render       |
    // | Layout          | 8     | The job will be processed with the same priority as render       |
    // | DataBind        | 9     | The job will be processed with the same priority as data binding |
    // | Normal          | 10    | The job will be processed with normal priority                   |
    // | Send            | 11    | The job will be processed before other asynchronous operations   |
    // | MaxValue        | 11    | Maximum possible priority                                        |

    /// Dispatcher.UIThread.Post
    let inline post (func:unit -> unit) =
        Dispatcher.UIThread.Post func

    /// Dispatcher.UIThread.Post with highest priority = DispatcherPriority.Send
    let inline send (func:unit -> unit) =
        Dispatcher.UIThread.Post(func,DispatcherPriority.Send)

    let inline invoke (func:unit -> 'T) =
        Dispatcher.UIThread.InvokeAsync(func)


    let inline awaitSend (t:System.Threading.Tasks.Task<'T>) =
        Dispatcher.UIThread.AwaitWithPriority(t, Avalonia.Threading.DispatcherPriority.Send )


open System
open System.Threading
open Avalonia.Controls



/// Threading Utils to setup and access the SynchronizationContext
/// and evaluate any function on UI thread (Sync.doSync(f))
type SyncContext private () =

    // This static class could be a module too but then the .context member could not do a check for initialization on every access.
    // It would be done when the module is loaded, that might be too early.

    static let mutable errorFileWrittenOnce = false // to not create more than one error file on Desktop per app run

    static let mutable ctx : SynchronizationContext = null  // will be set on first access

    /// To ensure SynchronizationContext is set up.
    /// Optionally writes a log file to the desktop if it fails, since these errors can be really hard to debug
    static member installSynchronizationContext (logErrorsOnDesktop) =
        if isNull ctx then
            if AvaloniaSynchronizationContext.Current = null then
                // https://stackoverflow.com/questions/10448987/dispatcher-currentdispatcher-vs-application-current-dispatcher
                // DispatcherSynchronizationContext(Windows.Application.Current.Dispatcher) |> SynchronizationContext.SetSynchronizationContext
                AvaloniaSynchronizationContext.InstallIfNeeded()
                // AvaloniaSynchronizationContext.Ensure(DispatcherPriority.Background) |> ignore
            ctx <- AvaloniaSynchronizationContext.Current

            if isNull ctx && logErrorsOnDesktop && not errorFileWrittenOnce then
                // reporting this to the UI instead would not work since there is no sync context for the UI
                let time = DateTime.UtcNow.ToString "yyyy-MM-dd_HH-mm-ss-fff" // to ensure unique file names
                let filename = sprintf "Fittings.SynchronizationContext setup failed-%s.txt" time
                let desktop = Environment.GetFolderPath Environment.SpecialFolder.Desktop
                let file = IO.Path.Combine(desktop,filename)
                try IO.File.WriteAllText(file, "Failed to get DispatcherSynchronizationContext") with _ -> () // file might be open or locked
                errorFileWrittenOnce <- true
                failwith ("installSynchronizationContext failed See: " + file)


    /// The UI SynchronizationContext to switch to inside async workflows
    /// Accessing this member from any thread will set up the sync context first if it is not there yet.
    /// If installSynchronizationContext fails an error file is written on the desktop since debugging this kind of errors can be hard
    static member context =
        if isNull ctx then SyncContext.installSynchronizationContext true
        ctx

    /// Runs function on UI thread via Async.SwitchToContext
    static member doSync(func:unit -> unit) =
        async {
            do! Async.SwitchToContext SyncContext.context
            func()
            } |> Async.StartImmediate

        // see also:
        // https://github.com/fsprojects/FsXaml/blob/c0979473eddf424f7df83e1b9222a8ca9707c45a/src/FsXaml.Wpf/Utilities.fs#L132
        // https://stackoverflow.com/questions/61227071/f-async-switchtocontext-vs-dispatcher-invoke


    /// dispatches an asynchronous message
    static member post (func:unit -> unit) =
        let act = new Action( func)
        Dispatcher.UIThread.Post(act)
        // if isNull ctx then SyncContext.installSynchronizationContext true
        // let f = SendOrPostCallback(fun _ -> func())
        // ctx.Post(f, null) // asynchronous

    /// dispatches a synchronous message
    static member send (func:unit -> unit) =
        if isNull ctx then SyncContext.installSynchronizationContext true
        let f = SendOrPostCallback(fun _ -> func())
        ctx.Send(f, null) // synchronous


    /// uses 'send'
    static member inline printAvaloniaLog f =
        // https://stackoverflow.com/a/19009579/969070
        SyncContext.send f
