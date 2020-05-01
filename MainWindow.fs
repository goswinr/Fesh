namespace Seff

open System
open System.Windows
open Seff.Model
open Seff.Config


module MainWindow =    

    let create (args: string [], startFsi:bool) = 

        let win = Win.Initialize()
               
        win.Content     <- if Settings.getBool "isVertSplit" false then UI.gridVert() else UI.gridHor() 
        win.Background  <- UI.menu.Background // call after setting up content, otherwise space next to tab headers is in an odd color
        
        EventHandlers.setUpForWindowSizing(win)
        win.InputBindings.AddRange Commands.allShortCutKeyGestures  
        Menu.setup()
               
        win.Loaded.Add (fun _ ->
            Log.PrintInfoMsg "* Time for loading main window: %s"  Timer.InstanceStartup.tocEx
                     
            
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

