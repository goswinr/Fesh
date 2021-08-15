namespace Seff.Views

open System
open System.IO
open System.Windows.Controls
open System.Windows
open System.Windows.Media

open Seff
open Seff.Editor
open Seff.Model
open Seff.Util
open AvalonLog.Brush


type FileChange = 
    |Changed 
    |Renamed 
    |Deleted 

type FileWatcher(editor:Editor,upadteIsCodeSaved:bool->unit) as this =
    inherit FileSystemWatcher() 

    let onFocusActions = ResizeArray<unit->unit>()
    
    

    let asktToUpdate (msg:string, newCode:string) =
        match MessageBox.Show(msg, "File Changed" , MessageBoxButton.YesNo, MessageBoxImage.Exclamation) with
        | MessageBoxResult.Yes ->             
            editor.Folds.SetToOneFullReload() // to keep folding state
            //editor.AvaEdit.Text <- newCode        // this does NOT allows undo or redo
            editor.AvaEdit.Document.Text <- newCode // this allows undo and redo 
            upadteIsCodeSaved(true)    
        | _  -> 
            upadteIsCodeSaved(false)



    let isDiffrent (fullPath:string) =        
        async{
            do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context
            let doc = editor.AvaEdit.Document
            do! Async.SwitchToThreadPool()
            let uiCode = doc.CreateSnapshot().Text
            do! Async.Sleep 100 // to be sure file access is not blocked by other app
            try
                let fileCode =  File.ReadAllText(fullPath, Text.Encoding.UTF8)
                if uiCode <> fileCode then 
                    let n = Path.GetFileName(fullPath)
                    let dir = Path.GetDirectoryName(fullPath)
                    //let msg = "at " + DateTime.nowStrMilli + " this file was changed externally."
                    //let msg = sprintf "File\r\n%s\r\nat\r\n%s\r\nchanged.\r\nDo you want to reload it?" n dir
                    let msg = sprintf "File '%s' changed. Do you want to reload it?" n 
                    do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context                   
                    if editor.AvaEdit.IsFocused then
                        asktToUpdate(msg,fileCode)
                    else                        
                        onFocusActions.Add (fun () ->  asktToUpdate(msg,fileCode) )                 
            with e -> 
                editor.Log.PrintfnAppErrorMsg "File changed but cant read changes from file system to compare if its the same as the currently shown file. %A " e
             }            
        |> Async.StartImmediate
    
    let mutable deleted : option<DateTime>= None

    let changed(kind:FileChange, path:string) =
        this.EnableRaisingEvents <- false // pause watching
        match kind with 
        |Renamed -> MessageBox.Show(sprintf "File %s was renamed." path )|> ignore
        |Deleted -> MessageBox.Show(sprintf "File %s was deleted." path )|> ignore
        |Changed -> isDiffrent path
        this.EnableRaisingEvents <- true // restart watching 
    
    
    do 
        this.NotifyFilter <-        NotifyFilters.LastWrite
                                ||| NotifyFilters.FileName
                                ||| NotifyFilters.DirectoryName
                            // ||| NotifyFilters.Attributes
                            // ||| NotifyFilters.CreationTime                                
                            // ||| NotifyFilters.LastAccess
                            // ||| NotifyFilters.Security
                            // ||| NotifyFilters.Size  
        
        this.Renamed.Add (fun a -> changed(Renamed,a.FullPath) )
        this.Changed.Add (fun a -> 
            deleted <- None // to not raise deleted event
            changed(Changed,a.FullPath) )
        this.Deleted.Add (fun a -> 
            deleted <- Some  DateTime.UtcNow
            async{
                do! Async.Sleep 500 // wait first and only raise deleted event if ther is no changed event in the meantime
                if deleted.IsSome then 
                    changed(Deleted,a.FullPath) 
                } |> Async.StartImmediate
            )           
        
        // to show massages of file change only when it gets focus again
        // editor.AvaEdit.MouseEnter.Add ( fun a ->  
        //     for msg in onFocusMsgs do  MessageBox.Show("MouseEnter " + msg) |> ignore  
        //     onFocusMsgs.Clear())
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
   