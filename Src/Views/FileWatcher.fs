namespace Seff.Views

open System
open System.IO
open System.Windows

open Seff
open Seff.Editor
open Seff.Model


type FileChange = 
    |Changed
    |Renamed
    |Deleted
    

type FileWatcher(editor:Editor, updateIsCodeSaved:bool->unit, setNewPath:FileWatcher*FilePath->unit) as this = 
    inherit FileSystemWatcher()

    let onFocusActions = ResizeArray<unit->unit>()

    let nl = System.Environment.NewLine

    let askToUpdate (path:string, newCode:string) = 
        let msg = $"File{nl}{path}{nl}was changed by some other process.{nl}Do you want to reload it?" 
        match MessageBox.Show(msg, "! File Changed !" , MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.Yes, MessageBoxOptions.DefaultDesktopOnly) with //https://stackoverflow.com/a/53009621
        | MessageBoxResult.Yes ->
            editor.Folds.SetToDoOneFullReload()     // to keep folding state
            //editor.AvaEdit.Text <- newCode        // this does NOT allows undo or redo
            editor.AvaEdit.Document.Text <- newCode // this allows undo and redo
            updateIsCodeSaved(true)
        | _  ->
            updateIsCodeSaved(false)
    
    let showChangedWindow =  editor.Config.Settings.GetBoolSaveDefault ("ShowFileChangedByOtherProcessWindow", true)

    let mutable deleted = false

    let change(kind:FileChange, path:string, oldPath:string) = 
        //ISeffLog.printError($"FileWatcher event {kind} {path}!")
        //this.EnableRaisingEvents <- false // pause watching
        async{
            do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context        
            match kind with
            |Renamed -> 
                let fi = SetTo(FileInfo(path))
                setNewPath(this,fi)
                //updateIsCodeSaved(false) // mark unsaved so that via saving the recent files list is updated too
                MessageBox.Show($"File{nl}{oldPath}{nl}was renamed to{nl}{path}.", "! File renamed !", MessageBoxButton.OK, MessageBoxImage.Exclamation, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly)|> ignore
            
            |Deleted -> 
                deleted <- true
                do! Async.Sleep 300 // wait first and only raise deleted event if there is no changed event in the meantime
                if deleted && IO.File.Exists(path) |> not then // double check file really doesn't exist, false alarms  happen wehen a file is deleted and the saved again from Seff
                    updateIsCodeSaved(false)
                    MessageBox.Show($"File{nl}{path}{nl}was deleted.", "! File deleted !",MessageBoxButton.OK, MessageBoxImage.Exclamation, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly)|> ignore
                    
            
            |Changed ->                 
                deleted <- false // to not raise deleted event  too
                if showChangedWindow then 
                    let doc = editor.AvaEdit.Document
                    do! Async.SwitchToThreadPool()
                    let uiCode = doc.CreateSnapshot().Text
                    do! Async.Sleep 100 // to be sure file access is not blocked by other app
                    try
                        let fileCode =  File.ReadAllText(path, Text.Encoding.UTF8)
                        if uiCode <> fileCode then
                            do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context
                            if editor.AvaEdit.IsFocused then
                                askToUpdate(path,fileCode)
                            else
                                onFocusActions.Add (fun () ->  askToUpdate(path,fileCode) )
                    with e ->
                        editor.Log.PrintfnAppErrorMsg "File changed but cant read changes from file system to compare if its the same as the currently shown file. %A " e
            
            //this.EnableRaisingEvents <- true // restart watching
            }
            |> Async.StartImmediate
    

    do
        this.NotifyFilter <-       NotifyFilters.LastWrite
                               ||| NotifyFilters.FileName
                               ||| NotifyFilters.DirectoryName
                            // ||| NotifyFilters.Attributes
                            // ||| NotifyFilters.CreationTime
                            // ||| NotifyFilters.LastAccess
                            // ||| NotifyFilters.Security
                            // ||| NotifyFilters.Size

        this.Renamed.Add (fun a -> change(Renamed,a.FullPath,a.OldFullPath) )        
        this.Deleted.Add (fun a -> change(Deleted,a.FullPath,"") )           
        this.Changed.Add (fun a -> change(Changed,a.FullPath,"") )
        //this.Created.Add (fun a -> ISeffLog.printError($"FileWatcher Created {a.FullPath}!"))
        
        
        editor.AvaEdit.GotFocus.Add ( fun a ->
            if onFocusActions.Count > 0 then
                let actions = ResizeArray(onFocusActions)
                onFocusActions.Clear() // clone and clear first
                for action in actions do action()
            )

        match editor.FilePath with
        |NotSet ->
            this.EnableRaisingEvents <- false
        |SetTo fi ->
            this.Path <- fi.DirectoryName
            this.Filter <- fi.Name
            this.EnableRaisingEvents <- true // must be after setting path

    /// to delay showing the changed message till either the editor or the window gets focus, if they are not focused yet
    member this.OnFocusActions = onFocusActions

