namespace Seff

open System
open System.Windows
open Seff.Util
open Seff.Fsi
open Seff.Model
open Seff.Config
open Seff.Logging


module MainWindow =    
    
    
    let private setIcon (win:Window) = 
        // the Icon at the top left of the window and in the status bar,         
        // musst be function to be calld at later moment(eg. after loading). Build action : "Resource"; Copy to ouput Dir: "Do not copy" 
        // (for the exe file icon in explorer use <Win32Resource>Media\Logo15.res</Win32Resource>  in fsproj )
        let uri = new Uri("pack://application:,,,/Seff;component/Media/Logo15.ico", UriKind.RelativeOrAbsolute)
        try  win.Icon <-  Media.Imaging.BitmapFrame.Create(Application.GetResourceStream(uri).Stream)
        with ex -> Log.print  "Failed to load Media/Logo15.ico from Application.ResourceStream : %A" ex
    

    let create (args: string []) = 
        let timer = Timer()

        Environment.SetEnvironmentVariable ("FCS_ParseFileCacheSize", "5") 
        // http://fsharp.github.io/FSharp.Compiler.Service/caches.html
        // https://github.com/fsharp/FSharp.Compiler.Service/blob/71272426d0e554e0bac32ad349bbd9f5fa8a3be9/src/fsharp/service/service.fs#L35

        let win = new Window()
        win.Title       <-"Seff | FSharp Scripting Editor"
        win.Content     <- if Settings.getBool "isVertSplit" false then UI.gridVert() else UI.gridHor() 
        win.ResizeMode  <- ResizeMode.CanResize 
        win.Background  <- UI.menu.Background // otherwise space next to tab headers is in an odd color)
        
        EventHandlers.setUpForWindowSizing(win)
        win.InputBindings.AddRange Commands.allShortCutKeyGestures  
        Menu.setup()

        Controls.ToolTipService.ShowOnDisabledProperty.OverrideMetadata( typeof<Controls.Control>,  new FrameworkPropertyMetadata(true)) //still show-tooltip-when a button(or menu item )  is disabled-by-command //https://stackoverflow.com/questions/4153539/wpf-how-to-show-tooltip-when-button-disabled-by-command

        (* //TODO with this the app fails to start. why?
        Application.Current.DispatcherUnhandledException.Add(fun e ->  //exceptions generated on the UI thread
            if e <> null then 
                Log.print "Application.Current.DispatcherUnhandledException in main Thread: %A" e.Exception           
                e.Handled<- true) *)
               
       
        AppDomain.CurrentDomain.UnhandledException.AddHandler (//catching unhandled exceptions generated from all threads running under the context of a specific application domain. //https://dzone.com/articles/order-chaos-handling-unhandled
            new UnhandledExceptionEventHandler( Seff.ProcessCorruptedState.Handler)) //https://stackoverflow.com/questions/14711633/my-c-sharp-application-is-returning-0xe0434352-to-windows-task-scheduler-but-it
        
       
        win.Loaded.Add (fun _ ->
            Log.print "* Time for loading main window: %s"  timer.tocEx
            setIcon(win)             
            
            CreateTab.loadArgsAndOpenFilesOnLastAppClosing(args)
            RecentlyUsedFiles.loadRecentFilesMenu Menu.RecentFiles.updateRecentMenue
            
            //Log.print "** Time for loading recent files and recent menu: %s"  timer.tocEx
            
            //conig and start FSI
            match Config.currentRunContext with
            |Hosted _ ->     // allow sync execution only for hosted context
                if Settings.getBool "asyncFsi" (Fsi.mode=Async) then Fsi.setMode(Mode.Async) else Fsi.setMode(Mode.Sync) 
                StatusBar.addSwitchFforSyncchonisationMode()
            |Standalone -> 
                Settings.setBool "asyncFsi" true
                Fsi.setMode(Mode.Async)// always async
            
            
            //win.Activate() |> ignore // needed ?           
            //Tab.currEditor.Focus() |> ignore // can be null ? needed ?            
            )    
        
        
        win.Closing.Add( fun e ->
            match askIfCancellingIsOk () with 
            | NotEvaluating   -> ()
            | YesAsync        -> cancelIfAsync() 
            | Dont            -> e.Cancel <- true // dont close window   
            | NotPossibleSync -> () // still close despite running thread ??
            ) 
           
                            
        win.Closing.Add( fun e ->  
            // current tabs are already saved when opened
            e.Cancel <- not <| FileDialogs.closeWindow() )

        
        //win.Initialized.Add (fun _ ->()) // this event seems to be never triggered   // why ???
        



        win

