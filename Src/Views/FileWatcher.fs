namespace Fesh.Views

open System
open System.IO
open System.Windows

open Fesh.Editor
open Fesh.Model
open Fittings


type FileChangeTracker (editor:Editor, setCodeSavedStatus:bool->unit) =
    let nl = System.Environment.NewLine
    let ta = editor.AvaEdit.TextArea
    let watcher = new FileSystemWatcher()

    let mutable checkPending = false
    let mutable checkVersion = 0
    let mutable gotFocusSubscription : IDisposable option = None
    let mutable activatedSubscription : IDisposable option = None

    let autoReloadIfClean () =
        editor.Config.Settings.GetBool ("AutoReloadExternalChangesIfClean", true)

    /// must be called on the WPF UI thread
    let editorHasUnsavedChanges () =
        editor.AvaEdit.Document.Text <> editor.CodeAtLastSave

    /// TODO in case of renaming MessageBox is shown and file gets set to unsaved. But doesn't switch to new filename automatically.

    // to stop watching once the user clicks "no" to "load changes?"
    let mutable doWatch = true

    let setCode(newCode,ed:Editor)=
        ed.AvaEdit.Dispatcher.Invoke ( fun () ->
            let av = ed.AvaEdit
            let cOff = av.CaretOffset
            av.Document.Text <- newCode // this allows undo and redo, just setting AvaEdit.Text not
            editor.CodeAtLastSave <- newCode
            if av.Document.TextLength > cOff then
                av.CaretOffset <- cOff //reset Caret to same position
            )


    let tryReadFile(fi:FileInfo) : string option=
        let rec loop i =
            if i > 5 then None
            else
                async{
                    try
                        return
                            IO.File.ReadAllText(fi.FullName, Text.Encoding.UTF8)
                            |> Fesh.Util.Str.normalizeCode editor.Config.Settings
                            |> Some
                    with _ ->
                        do! Async.Sleep 300
                        return loop (i+1)
                    } |> Async.RunSynchronously
        loop 0


    let check(_reason) =
        if doWatch then
            checkVersion <- checkVersion + 1
            let version = checkVersion
            let filePath = editor.FilePath
            let codeAtStart = editor.CodeAtLastSave
            async{
                do! Async.Sleep 200 // wait so that the new tab can be displayed first, ( on tab switches)
                match filePath with
                |NotSet _ -> () // do nothing, no file watching on files without a path

                |Deleted fi ->
                    fi.Refresh()
                    if fi.Exists then // file was deleted and now exists again
                        match tryReadFile fi with
                        |None -> IFeshLog.log.PrintfnIOErrorMsg "FileWatcher.fs: check: tryReadFile failed"
                        |Some fileCode ->
                            do! Async.SwitchToContext SyncWpf.context
                            if doWatch && version = checkVersion && codeAtStart = editor.CodeAtLastSave then
                                editor.FilePath <- SetTo fi
                                if fileCode = editor.CodeAtLastSave then
                                    setCodeSavedStatus (not <| editorHasUnsavedChanges())
                                else
                                    doWatch <- false // to not trigger new event from closing this window
                                    if autoReloadIfClean() && not (editorHasUnsavedChanges()) then
                                        setCode(fileCode, editor)
                                        setCodeSavedStatus true
                                        doWatch <- true
                                        IFeshLog.log.PrintfnInfoMsg $"Previously deleted file reloaded for {fi.Name}"
                                    else
                                        match MessageBox.Show(
                                            IEditor.mainWindow,
                                            $"The File{nl}{nl}{fi.Name}{nl}{nl}was previously deleted.{nl}It exists again. Do you want to reload it?",
                                            "Fesh | Reload Changes?",
                                            MessageBoxButton.YesNo,
                                            MessageBoxImage.Exclamation,
                                            MessageBoxResult.Yes,
                                            MessageBoxOptions.None) with
                                        | MessageBoxResult.Yes ->
                                            setCode(fileCode, editor)
                                            setCodeSavedStatus true
                                            doWatch <- true
                                            if not <| autoReloadIfClean() then
                                                IFeshLog.log.PrintfnInfoMsg "Previously deleted file reloaded. You can enable AutoReloadExternalChangesIfClean in Settings.txt to avoid this prompt."
                                        | _ ->
                                            setCodeSavedStatus false
                                            doWatch <- false

                |SetTo fi ->
                    fi.Refresh() // without this it would report missing during some save operations
                    if fi.Exists then
                        match tryReadFile fi with
                        |None -> IFeshLog.log.PrintfnIOErrorMsg "FileWatcher.fs: check: tryReadFile failed"
                        |Some fileCode ->
                            do! Async.SwitchToContext SyncWpf.context
                            if doWatch && version = checkVersion && codeAtStart = editor.CodeAtLastSave then
                                if fileCode <> editor.CodeAtLastSave then
                                    doWatch <- false // to not trigger new event from closing this MessageBox window
                                    if autoReloadIfClean() && not (editorHasUnsavedChanges()) then
                                        setCode(fileCode, editor)
                                        setCodeSavedStatus true
                                        doWatch <- true
                                        IFeshLog.log.PrintfnInfoMsg $"External file changes loaded for {fi.Name}"
                                    else
                                        match MessageBox.Show(
                                            IEditor.mainWindow,
                                            $"File{nl}{nl}{fi.Name}{nl}{nl}was changed.{nl}Do you want to reload it?",
                                            "Fesh | Reload Changes?",
                                            MessageBoxButton.YesNo,
                                            MessageBoxImage.Exclamation,
                                            MessageBoxResult.Yes,
                                            MessageBoxOptions.None) with
                                        | MessageBoxResult.Yes ->
                                            setCode(fileCode, editor)
                                            setCodeSavedStatus true
                                            doWatch <- true
                                            if not <| autoReloadIfClean() then
                                                IFeshLog.log.PrintfnInfoMsg "External file changes loaded. You can enable AutoReloadExternalChangesIfClean in Settings.txt to avoid this prompt."
                                        | _ ->
                                            setCodeSavedStatus false
                                            doWatch <- false
                    else
                        do! Async.SwitchToContext SyncWpf.context
                        if doWatch && version = checkVersion && codeAtStart = editor.CodeAtLastSave then
                            editor.FilePath <- Deleted fi
                            setCodeSavedStatus false
                            doWatch <- false // to not trigger new event from closing this window
                            MessageBox.Show(
                                IEditor.mainWindow,
                                $"{fi.Name}{nl}{nl}was deleted or renamed.{nl}{nl}at {fi.DirectoryName}",
                                "Fesh | File deleted or renamed!",
                                MessageBoxButton.OK,
                                MessageBoxImage.Exclamation,
                                MessageBoxResult.OK,
                                MessageBoxOptions.None)
                                |> ignore
                            doWatch <- false
            }
            |> Async.Start


    /// this will only check the file for diffs if focused and active
    let bufferedCheck(msg) =
        if doWatch then
            checkPending <- true
            async{
                do! Async.SwitchToContext SyncWpf.context
                if ta.IsFocused && IEditor.mainWindow.IsActive then
                    do! Async.Sleep 200 // during this wait some other file watch events might happen
                    if checkPending then
                        checkPending <- false
                        check(msg)
                }
                |> Async.Start


    let setWatcher() =
        watcher.EnableRaisingEvents <- false
        match editor.FilePath with
        |NotSet _ -> ()
        |SetTo fi
        |Deleted fi ->
            watcher.Path   <- fi.DirectoryName
            watcher.Filter <- fi.Name
            watcher.NotifyFilter <- NotifyFilters.LastWrite ||| NotifyFilters.FileName||| NotifyFilters.DirectoryName
            watcher.EnableRaisingEvents <- true // must be after setting path and filters

    do
        // https://wpf.2000things.com/2012/07/30/613-window-event-sequence/

        watcher.Renamed.Add (fun _ -> bufferedCheck("buffered Renamed") )
        watcher.Deleted.Add (fun _ -> bufferedCheck("buffered Deleted") )
        watcher.Changed.Add (fun _ -> bufferedCheck("buffered Changed") )
        watcher.Created.Add (fun _ -> bufferedCheck("buffered Created") ) // recreated after deletion

        gotFocusSubscription <-
            ta.GotFocus.Subscribe (fun _ -> // this also gets triggered when one of the above message boxes closes
                if doWatch then
                    check("ta.GotFocus"))
            |> Some

        activatedSubscription <-
            IEditor.mainWindow.Activated.Subscribe (fun _ -> // this also gets triggered when one of the above message boxes closes
                if doWatch && IEditor.isCurrent editor.AvaEdit then
                    check("mainWindow.Activated"))
            |> Some

        setWatcher()

    /// to update the location if file location changed
    member _.ResetPath() =
        checkVersion <- checkVersion + 1
        doWatch <- true
        setWatcher()

    /// sets watcher.EnableRaisingEvents <- false
    member _.Stop()=
        checkVersion <- checkVersion + 1
        doWatch <- false
        watcher.EnableRaisingEvents <- false
        gotFocusSubscription |> Option.iter (fun subscription -> subscription.Dispose())
        activatedSubscription |> Option.iter (fun subscription -> subscription.Dispose())
        watcher.Dispose()

