namespace Fesh

open System
open System.IO
open System.Windows

open Fesh.Model
open Fesh.Views
open Fesh.Config
open Fesh.Util

module Initialize =
    let mutable feshInstanceForDebug :Fesh = Unchecked.defaultof<Fesh>
    let saveBeforeFailing()=
        async{
            try
                match Model.IEditor.current with
                |None -> ()
                |Some ed ->
                    match ed.FilePath with
                    |NotSet _ -> ()
                    |Deleted _ -> ()
                    |SetTo fi ->
                        do! Async.SwitchToContext Fittings.SyncWpf.context
                        let doc = ed.AvaEdit.Document
                        do! Async.SwitchToThreadPool()
                        let txt = doc.CreateSnapshot().Text
                        let desk = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                        let p = Path.Combine(desk, Path.GetFileNameWithoutExtension(fi.Name) + " " + DateTime.nowStr + fi.Extension  )
                        File.WriteAllText(p,txt)
            with _ -> //saving might fail because another error might be writing to the same file already
                ()
        } |> Async.Start

    let everything(mode:HostedStartUpData option, startupArgs:string[]): Fesh =

        //match mode with None ->  Timer.InstanceStartup.tic()   | _ -> ()  // optional timer for full init process

        Fittings.SyncWpf.installSynchronizationContext(true)    // do first

        let en_US = Globalization.CultureInfo.CreateSpecificCulture("en-US")
        Threading.Thread.CurrentThread.CurrentCulture <- en_US
        Threading.Thread.CurrentThread.CurrentUICulture <- en_US
        Globalization.CultureInfo.DefaultThreadCurrentCulture   <- en_US
        Globalization.CultureInfo.DefaultThreadCurrentUICulture <- en_US

        // to still show-tooltip-when a button(or menu item ) is disabled-by-command
        // https://stackoverflow.com/questions/4153539/wpf-how-to-show-tooltip-when-button-disabled-by-command
        Controls.ToolTipService.ShowOnDisabledProperty.OverrideMetadata  (typeof<Controls.Control>, new FrameworkPropertyMetadata( true )            )
        Controls.ToolTipService.ShowDurationProperty.OverrideMetadata    (typeof<DependencyObject>, new FrameworkPropertyMetadata( Int32.MaxValue )  )
        Controls.ToolTipService.InitialShowDelayProperty.OverrideMetadata(typeof<DependencyObject>, new FrameworkPropertyMetadata( 50 )              )
        Controls.ToolTipService.InitialShowDelayProperty.OverrideMetadata(typeof<FrameworkElement>, new FrameworkPropertyMetadata( 50 )              ) // also set in Editor.fs

        /// ------------------ Log and Config --------------------


        let log  = Log.Create() // this should be done as early as possible so that logging works

        //try to fix missing line numbers in hosted context:but does not help, https://github.com/dotnet/fsharp/discussions/13293#discussioncomment-2949022
        //Directory.SetCurrentDirectory(Path.GetDirectoryName(Reflection.Assembly.GetAssembly([].GetType()).Location))
        //Model.IFeshLog.log.PrintfnDebugMsg $"Current directory set to: '{Environment.CurrentDirectory}'"

        let appName = match mode with Some n -> "Fesh." + n.hostName |None -> "Fesh"
        try
            // TODO attempt to save files before closing ?  or save anyway every 2 minutes to backup folder if less than 10k lines
            let errHandler = Fittings.ErrorHandling (
                appName,
                fun () -> saveBeforeFailing();  "FSI Error Stream:\r\n" + log.FsiErrorsStringBuilder.ToString()
                )
            errHandler.Setup()// do as soon as log exists
        with e ->
            log.PrintfnAppErrorMsg "Setting up Global Error Handling via Fittings.ErrorHandling failed. Or is done already? Is Fittings already loaded by another plug-in?\r\n%A" e

        let config = new Config(log, mode, startupArgs)
        log.FinishLogSetup(config)

        let f = Fesh(config, log)
        feshInstanceForDebug <- f
        f


        // not needed?
        //try
        //    // so that wpf textBoxes that are bound to floats can have a dot input too. see https://stackoverflow.com/a/35942615/969070
        //    // setting this might fails when a hosting WPF process is already up and running (eg loaded in another WPF thread)
        //    FrameworkCompatibilityPreferences.KeepTextBoxDisplaySynchronizedWithTextProperty <- false
        //with  _ ->
        //    if FrameworkCompatibilityPreferences.KeepTextBoxDisplaySynchronizedWithTextProperty then
        //        log.PrintfnAppErrorMsg "could not set KeepTextBoxDisplaySynchronizedWithTextProperty to false "



