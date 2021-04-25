﻿namespace Seff

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
        
        let log    = Log.Create()       
        GlobalErrorHandeling.setup(log) // do as soon as log exists
        

        // not needed (yet)?
        //try 
        //    // so that wpf textboxes that are bound to floats can have a dot input too. see https://stackoverflow.com/a/35942615/969070
        //    // setting this might fails when a hosting WPF process is alread up and running (eg loaded in another WPF thread)  
        //    FrameworkCompatibilityPreferences.KeepTextBoxDisplaySynchronizedWithTextProperty <- false
        //with  _ ->
        //    if FrameworkCompatibilityPreferences.KeepTextBoxDisplaySynchronizedWithTextProperty then 
        //        log.PrintfnAppErrorMsg "could not set KeepTextBoxDisplaySynchronizedWithTextProperty to false "
        




        let config = new Config(log, mode, startupArgs)
        log.AdjustToSettingsInConfig(config)

        Seff( config, log)

        
        
        

        


        