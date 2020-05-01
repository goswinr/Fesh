namespace Seff

open System
open System.Windows
open Seff.Model
open Seff.Config

module MainWindow =    
    
    
    let private setIcon (win:Window) = 
        // the Icon at the top left of the window and in the status bar,         
        // musst be function to be calld at later moment(eg. after loading). Build action : "Resource"; Copy to ouput Dir: "Do not copy" 
        // (for the exe file icon in explorer use <Win32Resource>Media\Logo15.res</Win32Resource>  in fsproj )
        let uri = new Uri("pack://application:,,,/Seff;component/Media/Logo15.ico", UriKind.RelativeOrAbsolute)
        try  win.Icon <-  Media.Imaging.BitmapFrame.Create(Application.GetResourceStream(uri).Stream)
        with ex -> Log.printAppErrorMsg  "Failed to load Media/Logo15.ico from Application.ResourceStream : %A" ex
    

    let create (args: string [], startFsi:bool) = 
        


        let win = new Window()
        win.Title       <- match Context.Mode with Standalone -> "Seff | Scripting editor for fsharp"  | Hosted n ->  "Seff | Scripting editor for fsharp in " + n
        win.ResizeMode  <- ResizeMode.CanResize         
        win.Content     <- if Settings.getBool "isVertSplit" false then UI.gridVert() else UI.gridHor() 
        win.Background  <- UI.menu.Background // otherwise space next to tab headers is in an odd color, call afte setting up content
        




        EventHandlers.setUpForWindowSizing(win)
        win.InputBindings.AddRange Commands.allShortCutKeyGestures  
        Menu.setup()


       
        win.Loaded.Add (fun _ ->
            setIcon(win) // call only after loading   
            Log.printInfoMsg "* Time for loading main window: %s"  Timer.InstanceStartup.tocEx
                     
            
            CreateTab.loadArgsAndOpenFilesOnLastAppClosing(args)
            RecentlyUsedFiles.loadRecentFilesMenu Menu.RecentFiles.updateRecentMenue
            if startFsi then Fsi.Initalize()
            
            //win.Activate() |> ignore // needed ?           
            //Tab.currEditor.Focus() |> ignore // fails!           
            )    
        
        
        win.Closing.Add( fun e ->
            match Fsi.AskIfCancellingIsOk () with 
            | NotEvaluating   -> ()
            | YesAsync        -> Fsi.CancelIfAsync() 
            | Dont            -> e.Cancel <- true // dont close window   
            | NotPossibleSync -> () // still close despite running thread ??
            ) 
           
                            
        win.Closing.Add( fun e -> //maybe cancel closing if files are unsaved
            e.Cancel <- not <| FileDialogs.askIfClosingWindowIsOk() )
        
        //win.Initialized.Add (fun _ ->()) // this event seems to be never triggered, why ???
        
        win

