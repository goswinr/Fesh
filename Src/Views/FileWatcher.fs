namespace Seff.Views

open System
open System.IO
open System.Windows

open Seff.Editor
open Seff.Model
open FsEx.Wpf


type FileChangeTracker (editor:Editor, setCodeSavedStatus:bool->unit) =
    let nl = System.Environment.NewLine
    let ta = editor.AvaEdit.TextArea
    let watcher = new FileSystemWatcher()
    
    let mutable checkPending = false

    /// TODO in case of renaming MessageBox is shown and file gets set to unsaved. But doesn't switch to new filename automatically.

    let setCode(newCode,ed:Editor)=
        ed.AvaEdit.Dispatcher.Invoke ( fun () -> 
            let av = ed.AvaEdit
            let cOff = av.CaretOffset
            av.Document.Text <- newCode // this allows undo and redo, just setting AvaEdit.Text not
            editor.CodeAtLastSave <- newCode
            if av.Document.TextLength > cOff then 
                av.CaretOffset <- cOff //reset Caret to same position
            )

    let check(reason) = 
        match editor.FilePath with
        |NotSet _ ->() 
        |Deleted _  |SetTo _ ->        
            async{                
                do! Async.Sleep 500 // wait so that the new tab can be displayed first, ( on tab switches)
                // editor.FilePath might have change in the Async.Sleep wait if this check was just 
                // triggred from reactivatong the main window aftyer closing a saveAs dialog.
                // so get it again
                match editor.FilePath with
                |NotSet _ -> () // do nothing, no file watching on files without a path

                |Deleted fi -> 
                    fi.Refresh() 
                    if fi.Exists then //file was deleted and now exist again ??
                        editor.FilePath <- SetTo fi
                        let fileCode = IO.File.ReadAllText(fi.FullName)
                        if fileCode = editor.CodeAtLastSave then                            
                            setCodeSavedStatus(true)
                        else
                            do! Async.SwitchToContext SyncWpf.context                                                  
                            match MessageBox.Show(
                                IEditor.mainWindow, 
                                // $"{reason}: File{nl}{nl}{fi.Name}{nl}{nl}was changed.{nl}Do you want to reload it?", // Debug
                                $"File{nl}{nl}{fi.Name}{nl}{nl}was changed.{nl}Do you want to reload it?",
                                "Reload Changes?", 
                                MessageBoxButton.YesNo, 
                                MessageBoxImage.Exclamation, 
                                MessageBoxResult.Yes,// default result 
                                MessageBoxOptions.None) with // previously MessageBoxOptions.DefaultDesktopOnly
                        
                            | MessageBoxResult.Yes -> 
                                setCode(fileCode, editor)
                                setCodeSavedStatus(true)
                            | _  ->
                                setCodeSavedStatus(false)
                    else
                        // do nothing, the file is still deleted
                        ()                
                
                |SetTo fi ->                 
                    fi.Refresh() // with out this it would raise missing in case of save incrementing
                    if fi.Exists then
                        let fileCode = IO.File.ReadAllText(fi.FullName)
                        if fileCode <> editor.CodeAtLastSave then // this means that the last file saving was not done by Seff                            
                            do! Async.SwitchToContext SyncWpf.context                                                  
                            match MessageBox.Show(
                                IEditor.mainWindow, 
                                // $"{reason}: File{nl}{nl}{fi.Name}{nl}{nl}was changed.{nl}Do you want to reload it?", // Debug
                                $"File{nl}{nl}{fi.Name}{nl}{nl}was changed.{nl}Do you want to reload it?",
                                "Reload Changes?", 
                                MessageBoxButton.YesNo, 
                                MessageBoxImage.Exclamation, 
                                MessageBoxResult.Yes,// default result 
                                MessageBoxOptions.None) with // previously MessageBoxOptions.DefaultDesktopOnly
                        
                            | MessageBoxResult.Yes -> 
                                setCode(fileCode, editor)
                                setCodeSavedStatus(true)
                            | _  ->
                                setCodeSavedStatus(false)
                        else
                            setCodeSavedStatus(true)
                        
                    else
                        editor.FilePath <- Deleted fi                        
                        setCodeSavedStatus(false)
                        do! Async.SwitchToContext SyncWpf.context 
                        MessageBox.Show(
                            IEditor.mainWindow, 
                            //$"{reason}: {fi.Name}{nl}{nl}was deleted or renamed.{nl}{nl}at {fi.DirectoryName}", // Debug
                            $"{fi.Name}{nl}{nl}was deleted or renamed.{nl}{nl}at {fi.DirectoryName}",
                            "File deleted or renamed!", 
                            MessageBoxButton.OK, 
                            MessageBoxImage.Exclamation, 
                            MessageBoxResult.OK,// default result 
                            MessageBoxOptions.None // previously MessageBoxOptions.DefaultDesktopOnly
                            )
                            |> ignore                     
                    
                       
            } 
            |>Async.Start
    

    /// this will only check the file for diffs if focused and active
    let bufferedCheck(msg) =
        checkPending <- true        
        async{
            do! Async.SwitchToContext SyncWpf.context 
            if ta.IsFocused && IEditor.mainWindow.IsActive then
                do! Async.Sleep 1000 // during this wait some other file watch events might happen
                if checkPending then 
                    checkPending <- false                    
                    check(msg)
            }
            |> Async.Start


    let setWatcher() =
        match editor.FilePath with
        |NotSet _ ->
            watcher.EnableRaisingEvents <- false
        |SetTo fi 
        |Deleted fi -> 
            watcher.Path   <- fi.DirectoryName
            watcher.Filter <- fi.Name
            watcher.EnableRaisingEvents <- true // must be after setting path
            watcher.NotifyFilter <- NotifyFilters.LastWrite ||| NotifyFilters.FileName||| NotifyFilters.DirectoryName 
            watcher.Renamed.Add (fun _ -> bufferedCheck("buffered Renamed") ) 
            watcher.Deleted.Add (fun _ -> bufferedCheck("buffered Deleted") )
            watcher.Changed.Add (fun _ -> bufferedCheck("buffered Changed") )
            watcher.Created.Add (fun _ -> bufferedCheck("buffered Created") ) // recreated after deletion

    do
        // https://wpf.2000things.com/2012/07/30/613-window-event-sequence/

        ta.GotFocus.Add (fun _ -> check("ta.GotFocus")) // this also get triggered when one of the above message boxes gets closed

        IEditor.mainWindow.Activated.Add (fun _ -> if IEditor.isCurrent editor.AvaEdit then check("mainWindow.Activated") )           

        setWatcher()
    
    /// to update the location if file location changed
    member _.ResetPath() = 
        setWatcher()
    
    /// sets watcher.EnableRaisingEvents <- false 
    member _.Stop()=
       watcher.EnableRaisingEvents <- false 
            
