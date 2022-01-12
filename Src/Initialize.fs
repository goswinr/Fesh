namespace Seff

open System
open System.Windows

open Seff.Views
open Seff.Config

module Initialize = 

    let everything(mode:HostedStartUpData option, startupArgs:string[])= 

        //match mode with None ->  Timer.InstanceStartup.tic()   | _ -> ()  // optional timer for full init process

        FsEx.Wpf.SyncWpf.installSynchronizationContext(true)    // do first

        let en_US = Globalization.CultureInfo.CreateSpecificCulture("en-US")
        Globalization.CultureInfo.DefaultThreadCurrentCulture   <- en_US
        Globalization.CultureInfo.DefaultThreadCurrentUICulture <- en_US

        // to still show-tooltip-when a button(or menu item )  is disabled-by-command
        //https://stackoverflow.com/questions/4153539/wpf-how-to-show-tooltip-when-button-disabled-by-command
        Controls.ToolTipService.ShowOnDisabledProperty.OverrideMetadata  (typeof<Controls.Control>, new FrameworkPropertyMetadata( true )             )
        Controls.ToolTipService.ShowDurationProperty.OverrideMetadata    (typeof<DependencyObject>, new FrameworkPropertyMetadata( Int32.MaxValue )   )
        Controls.ToolTipService.InitialShowDelayProperty.OverrideMetadata(typeof<DependencyObject>, new FrameworkPropertyMetadata( 50 )               )

        /// ------------------ Log and Config --------------------


        let log  = Log.Create() // this should be done as early as possibel so that logging works

        let appname = match mode with Some n -> "Seff." + n.hostName |None -> "Seff"
        try 
            // TODO attempt to save files before closing ?  or save anyway every 2 minutes to backup folder if less than 10k lines
            let errHandler = FsEx.Wpf.ErrorHandeling (appname, fun () -> "FSI Error Stream:\r\n" + log.FsiErrorsStringBuilder.ToString())
            errHandler.Setup()// do as soon as log exists 
        with e ->
            log.PrintfnAppErrorMsg "Setting up Global Error Handling via FsEx.Wpf.ErrorHandeling failed. Or is done already? Is FsEx.Wpf already loaded by another plug-in?\r\n%A" e 

             

        let config = new Config(log, mode, startupArgs)
        log.AdjustToSettingsInConfig(config)

        Seff(config, log)


        // not needed?
        //try
        //    // so that wpf textboxes that are bound to floats can have a dot input too. see https://stackoverflow.com/a/35942615/969070
        //    // setting this might fails when a hosting WPF process is alread up and running (eg loaded in another WPF thread)
        //    FrameworkCompatibilityPreferences.KeepTextBoxDisplaySynchronizedWithTextProperty <- false
        //with  _ ->
        //    if FrameworkCompatibilityPreferences.KeepTextBoxDisplaySynchronizedWithTextProperty then
        //        log.PrintfnAppErrorMsg "could not set KeepTextBoxDisplaySynchronizedWithTextProperty to false "









