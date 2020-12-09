namespace Seff

open System
open System.Windows
open Seff.Views
open Seff.Config
open Seff.Util.General
open Seff.Model


module Initialize =      
    

    let everything(mode:HostedStartUpData option, startupArgs:string[])=
        
        match mode with None ->  Timer.InstanceStartup.tic()   | _ -> ()  // optional timer for full init process
        
        Sync.installSynchronizationContext()    // do first

        let en_US = Globalization.CultureInfo.CreateSpecificCulture("en-US")        
        Globalization.CultureInfo.DefaultThreadCurrentCulture   <- en_US
        Globalization.CultureInfo.DefaultThreadCurrentUICulture <- en_US
        

        // to still show-tooltip-when a button(or menu item )  is disabled-by-command 
        //https://stackoverflow.com/questions/4153539/wpf-how-to-show-tooltip-when-button-disabled-by-command
        Controls.ToolTipService.ShowOnDisabledProperty.OverrideMetadata(  typeof<Controls.Control>, new FrameworkPropertyMetadata(true)) 
        Controls.ToolTipService.ShowDurationProperty.OverrideMetadata(    typeof<DependencyObject>, new FrameworkPropertyMetadata(Int32.MaxValue))
        Controls.ToolTipService.InitialShowDelayProperty.OverrideMetadata(typeof<DependencyObject>, new FrameworkPropertyMetadata(50))

        /// ------------------ Log and Config --------------------
        
        let log    = Log.Instance        
        GlobalErrorHandeling.setup(log) // do as soon as log exists

        let config = new Config(log, mode, startupArgs)
        log.AdjustToSettingsInConfig(config)

        Seff( config, log)

        
        
        

        


        