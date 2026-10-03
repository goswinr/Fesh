namespace Fesh.Views

open System
open System.IO
open Avalonia

open Fesh.Editor
open Fesh.Model
open Fittings


type FileChangeTracker (editor:Editor, setCodeSavedStatus:bool->unit) =
    let nl = System.Environment.NewLine
    let ta = editor.AvaEdit.TextArea
    let watcher = new FileSystemWatcher()

    let mutable checkVersion = 0
    let mutable bufferVersion = 0
    let mutable isStopped = false
    let mutable gotFocusSubscription : IDisposable option = None
    let mutable activatedSubscription : IDisposable option = None

    let autoReloadIfClean () =
        editor.Config.Settings.GetBool ("AutoReloadExternalChangesIfClean", true)

    /// must be called on the WPF UI thread
    let editorHasUnsavedChanges () =
        // CodeAtLastSave is always normalized, the Document might have tabs or LF line endings via paste
        Fesh.Util.Str.normalizeCode editor.Config.Settings editor.AvaEdit.Document.Text <> editor.CodeAtLastSave

    /// TODO in case of renaming MessageBox is shown and file gets set to unsaved. But doesn't switch to new filename automatically.

    // to stop watching once the user clicks "no" to "load changes?"
    let mutable doWatch = true

    let mutable alwaysLoadChanges = false // if true, always load changes without asking

    let setCode(newCode,ed:Editor)=
        SyncContext.doSync ( fun () ->
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


    /// must be called on the WPF UI thread
    let check() =
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
                            do! Async.SwitchToContext SyncContext.context
                            if doWatch && version = checkVersion && codeAtStart = editor.CodeAtLastSave then
                                editor.FilePath <- SetTo fi
                                if fileCode = editor.CodeAtLastSave then
                                    setCodeSavedStatus (not <| editorHasUnsavedChanges())
                                else
                                    doWatch <- false // to not trigger new event from closing this window
                                    if alwaysLoadChanges || (autoReloadIfClean() && not (editorHasUnsavedChanges())) then
                                        setCode(fileCode, editor)
                                        setCodeSavedStatus true
                                        doWatch <- true
                                        IFeshLog.log.PrintfnInfoMsg $"Previously deleted file reloaded for {fi.Name}"
                                    else
                                        match! MessageBox.Show(
                                            IEditor.mainWindow,
                                            $"The File{nl}{nl}{fi.Name}{nl}{nl}was previously deleted.{nl}It exists again. Do you want to reload it?",
                                            "Fesh | Reload Changes?",
                                            MessageBoxButton.YesAlwaysNo,
                                            MessageBoxImage.Exclamation,
                                            MessageBoxResult.Yes) |> Async.AwaitTask with
                                        | MessageBoxResult.Always ->
                                            alwaysLoadChanges <- true
                                            setCode(fileCode, editor)
                                            setCodeSavedStatus true
                                            doWatch <- true
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
                            do! Async.SwitchToContext SyncContext.context
                            if doWatch && version = checkVersion && codeAtStart = editor.CodeAtLastSave then
                                if fileCode <> editor.CodeAtLastSave then
                                    doWatch <- false // to not trigger new event from closing this MessageBox window
                                    if autoReloadIfClean() && not (editorHasUnsavedChanges()) then
                                        setCode(fileCode, editor)
                                        setCodeSavedStatus true
                                        doWatch <- true
                                        IFeshLog.log.PrintfnInfoMsg $"External file changes loaded for {fi.Name}"
                                    else
                                        match! MessageBox.Show(
                                            IEditor.mainWindow,
                                            $"File{nl}{nl}{fi.Name}{nl}{nl}was changed.{nl}Do you want to reload it?",
                                            "Fesh | Reload Changes?",
                                            MessageBoxButton.YesNo,
                                            MessageBoxImage.Exclamation,
                                            MessageBoxResult.Yes) |> Async.AwaitTask with
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
                                    // the file on disk matches the last saved code, but the Document might still have unsaved edits
                                    setCodeSavedStatus (not <| editorHasUnsavedChanges())
                    else
                        do! Async.SwitchToContext SyncContext.context
                        if doWatch && version = checkVersion && codeAtStart = editor.CodeAtLastSave then
                            editor.FilePath <- Deleted fi
                            setCodeSavedStatus false
                            doWatch <- false // to not trigger new event from closing this window
                            MessageBox.ShowOK(
                                IEditor.mainWindow,
                                $"{fi.Name}{nl}{nl}was deleted or renamed.{nl}{nl}at {fi.DirectoryName}",
                                "Fesh | File deleted or renamed!",
                                MessageBoxImage.Exclamation)
                            doWatch <- true // to notice when the file exists again
            }
            |> Async.Start


    /// This will only check the file for diffs if focused and active.
    /// Several file system events in quick succession are collapsed into one check,
    /// the waiting restarts on every new event so that a slow external writer is not
    /// read while it is only half done.
    let bufferedCheck() =
        if doWatch then
            async{
                do! Async.SwitchToContext SyncContext.context // bufferVersion is only ever touched on the UI thread
                bufferVersion <- bufferVersion + 1
                let version = bufferVersion
                if ta.IsFocused && IEditor.mainWindow.IsActive then
                    do! Async.Sleep 200 // during this wait some other file watch events might happen
                    if version = bufferVersion && doWatch then // only the most recent event triggers the check
                        check()
                }
                |> Async.Start


    let setWatcher() =
        watcher.EnableRaisingEvents <- false
        if not isStopped then
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

        watcher.Renamed.Add (fun _ -> bufferedCheck() )
        watcher.Deleted.Add (fun _ -> bufferedCheck() )
        watcher.Changed.Add (fun _ -> bufferedCheck() )
        watcher.Created.Add (fun _ -> bufferedCheck() ) // recreated after deletion

        gotFocusSubscription <-
            ta.GotFocus.Subscribe (fun _ -> // this also gets triggered when one of the above message boxes closes
                if doWatch then
                    check())
            |> Some

        activatedSubscription <-
            IEditor.mainWindow.Activated.Subscribe (fun _ -> // this also gets triggered when one of the above message boxes closes
                if doWatch && IEditor.isCurrent editor.AvaEdit then
                    check())
            |> Some

        setWatcher()

    /// to update the location if file location changed
    /// does nothing after Stop() was called
    member _.ResetPath() =
        if not isStopped then
            checkVersion <- checkVersion + 1
            doWatch <- true
            setWatcher()

    /// Resumes watching the same file again.
    /// Needed because answering 'No' to a "reload changes?" dialog stops the watching of this file.
    /// Call this after the file was saved from within Fesh, so that later external changes are noticed again.
    /// Does nothing after Stop() was called.
    member _.Rearm() =
        if not isStopped then
            checkVersion <- checkVersion + 1 // discard checks that are still in flight from our own saving
            doWatch <- true

    /// stops watching, disposes the watcher and unsubscribes the window events
    member _.Stop()=
        if not isStopped then
            isStopped <- true
            checkVersion <- checkVersion + 1
            doWatch <- false
            watcher.EnableRaisingEvents <- false
            gotFocusSubscription |> Option.iter (fun subscription -> subscription.Dispose())
            activatedSubscription |> Option.iter (fun subscription -> subscription.Dispose())
            watcher.Dispose()

