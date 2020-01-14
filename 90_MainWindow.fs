namespace Seff

open System
open System.Windows
open Seff.Util
open Seff.Fsi

module MainWindow =    

    let private setIcon (win:Window) = 
        // the Icon at the top left of the window and in the status bar, 
        // for the exe file icon use <Win32Resource>Media\AppIcon.res</Win32Resource>  in fsproj 
        // musst be function to be calld at later moment(eg. after loading). Build action : "Resource"; Copy to ouput Dir: "Do not copy" 
        let uri = new Uri("pack://application:,,,/Seff;component/Media/LogoFSharp.ico", UriKind.RelativeOrAbsolute)
        try  win.Icon <-  Media.Imaging.BitmapFrame.Create(Application.GetResourceStream(uri).Stream)
        with ex -> Log.dlog  "*** Failed to load application icon."
    

    let create (args: string []) = 
        let timer = Timer()
        let win = new Window()
        
        win.Title       <-"Seff | FSharp Scripting Editor"
        win.Content     <- if Config.getBool "isVertSplit" false then UI.gridVert() else UI.gridHor() 
        win.ResizeMode  <- ResizeMode.CanResize 
        win.Background  <- UI.menu.Background // otherwise space next to tabs is in an odd color
        
        EventHandlers.setUpForWindow(win)
        win.InputBindings.AddRange Commands.AllInputBindings  
        Menu.setup()

        win.Loaded.Add (fun _ ->
            Log.printf "* Time for loading main window: %s"  timer.tocEx
            setIcon(win) 
            Config.loadOpenFilesOnLastAppClosing (CreateTab.newTab >> ignore, Log.dlog) 
            Config.loadRecentFilesMenu Menu.RecentFiles.updateRecentMenue
            //Log.printf "** Time for loading recent files and recent menu: %s"  timer.tocEx
            Fsi.agent.Start()
            
            for p in args do
                Log.printf "received argument: '%s'" p
                //FileDialogs.openFile (IO.FileInfo(p), CreateTab.newTab )
            //win.Activate() |> ignore // needed ?           
            //Tab.currEditor.Focus() |> ignore // can be null ? needed ?            
            )    
                
        win.Closing.Add( fun e ->
            match FsiStatus.Evaluation with
            |Ready |HadError -> ()
            |Evaluating ->  
                let msg = sprintf "Do you want to Cancel currently running code evaluation?" 
                match MessageBox.Show(msg,"Cancel Evaluation?",MessageBoxButton.YesNoCancel,MessageBoxImage.Exclamation,MessageBoxResult.Yes) with
                | MessageBoxResult.Yes -> Fsi.agent.Post Fsi.AgentMessage.Cancel
                | _ -> e.Cancel <- true ) 
                
        win.Closing.Add( fun e ->  
            // currnet tabs are already saved when opened
            e.Cancel <- not <| FileDialogs.closeWindow() )

        //win.Initialized.Add (fun _ ->()) // this event seems to be never triggered   // why ???
        
        win

