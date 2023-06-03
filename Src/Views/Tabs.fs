namespace Seff.Views

open System
open System.IO
open System.Windows.Controls
open System.Windows
open System.Windows.Media

open Seff
open Seff.Editor
open Seff.Model
open Seff.Util.General
open Seff.Config

type SavingKind = 
    | SaveInPlace
    | SaveExport 
    | SaveNewLocation 
    | SaveNewLocationSync // does not delay the update of recent file and current tabs, for when seff is closing immediately afterwards

/// A class holding the Tab Control.
/// Includes logic for saving and opening files.
/// Window is needed for closing after last Tab closed
type Tabs(config:Config, log:Log,seffWin:SeffWindow) = 

    let tabs = 
        new TabControl(
            Padding = Thickness(0.6),
            Margin = Thickness( 0.6),
            BorderThickness = Thickness(0.6),
            BorderBrush = Brushes.Black
            )
    
    let win = seffWin.Window


    let fsi = 
        let f = Fsi.GetOrCreate(config)
        f.Initialize()
        f

    let allTabs:seq<Tab> =  Seq.cast tabs.Items

    // excludes deleted files
    let allExistingFileInfos = seq{ for t in allTabs do match t.Editor.FilePath with NotSet _  |Deleted _-> () |SetTo fi -> yield fi } //TODO does this re-evaluate every time?

    let currentTabChangedEv = new Event<Tab>() //to Trigger Fs Checker and status bar update 

    let mutable current =  Unchecked.defaultof<Tab>

    let enviroDefaultDir = Environment.CurrentDirectory 

    let setCurrentTab(idx) =        
        if not <| Object.ReferenceEquals(null, current) then 
            current.Editor.State.Increment()  |> ignore<int64> // to cancel any running checkers
        
        tabs.SelectedIndex <- idx
        let t = tabs.Items[idx] :?> Tab
        current <- t
        IEditor.current <- Some (t.Editor:>IEditor)   

        seffWin.SetFileNameInTitle(t.Editor.FilePath)

        currentTabChangedEv.Trigger(t) // to update statusbar
        
        let dir =             
            match current.Editor.FilePath with 
            |SetTo fi  
            |Deleted fi -> 
                fi.Refresh()
                if fi.Directory.Exists then // the directory might be deleted too
                    fi.Directory.FullName
                else
                    enviroDefaultDir
            |NotSet _ -> 
                enviroDefaultDir 
        
        Environment.CurrentDirectory <- dir // to be able to use __SOURCE_DIRECTORY__  

        let ed = t.Editor
        DocChangeMark.markFoldCheckHighlightAsync(ed, ed.Services, ed.State, ed.State.Increment())
        
        // TODO make sure to reset checker if it is currently still running from another file ??
        // even though the error highlighter is only called if changeId is still the same          

        config.OpenTabs.Save(t.Editor.FilePath , allExistingFileInfos)
    
    let fileWasSavedAs(savedCode, t:Tab, fi:FileInfo, sync) = 
        let ed = t.Editor
        t.FileTracker.ResetPath()
        t.IsCodeSaved <- true
        ed.FilePath <- SetTo fi //this also updates the Tab header and set file info on editor
        ed.CodeAtLastSave <- savedCode 
        t.UpdateTabHeader()
        seffWin.SetFileNameInTitle(ed.FilePath)
        Environment.CurrentDirectory <- fi.Directory.FullName
        config.FoldingStatus.Set(ed.FilePath , ed.Folds.Manager) // otherwise no record would exist for the new file name
        if sync then 
            config.OpenTabs.SaveSync(ed.FilePath , allExistingFileInfos)
            config.RecentlyUsedFiles.AddAndSaveSync(fi)         
        else
            config.OpenTabs.Save(ed.FilePath , allExistingFileInfos)
            config.RecentlyUsedFiles.AddAndSave(fi)   
        log.PrintfnInfoMsg "File saved as:\r\n\"%s\"" fi.FullName  

    let workingDirectory () = 
        match current.Editor.FilePath with
        |SetTo fi -> Some fi.Directory
        |Deleted fi -> 
            if fi.Directory.Exists then // the directory might be deleted too
                Some fi.Directory
            else
                match allExistingFileInfos |> Seq.tryHead with
                |Some fi -> Some fi.Directory
                |None    -> config.RecentlyUsedFiles.MostRecentPath
        |NotSet _ ->
            match allExistingFileInfos |> Seq.tryHead with
            |Some fi -> Some fi.Directory
            |None    -> config.RecentlyUsedFiles.MostRecentPath


    let saveAt (t:Tab, fi:FileInfo, saveKind:SavingKind) = 
        fi.Refresh()
        if not <| fi.Directory.Exists then
            log.PrintfnIOErrorMsg "saveAt: Directory does not exist:\r\n%s" fi.Directory.FullName
            false
        else
            try
                let txt = t.AvaEdit.Text
                IO.File.WriteAllText(fi.FullName, txt, Text.Encoding.UTF8)                
                match saveKind with
                |SaveNewLocation     -> fileWasSavedAs(txt,t,fi,false)   
                |SaveNewLocationSync -> fileWasSavedAs(txt,t,fi,true)    
                |SaveInPlace -> // also called for Save-All command
                    t.IsCodeSaved <- true
                    t.Editor.CodeAtLastSave <- txt
                    log.PrintfnInfoMsg "File saved:\r\n\"%s\"" fi.FullName
                |SaveExport ->
                    config.FoldingStatus.Set(t.Editor.FilePath , t.Editor.Folds.Manager) // otherwise no record would exist for the new file name
                    log.PrintfnInfoMsg "File exported to:\r\n\"%s\"" fi.FullName
                true
            with e ->
                log.PrintfnIOErrorMsg "saveAt failed for: %s failed with %A" fi.FullName e
                false

    /// Returns false if saving operation was canceled or had an error, true on successful saving
    let saveAsDialog (t:Tab, saveKind:SavingKind) :bool= 
        let dlg = new Microsoft.Win32.SaveFileDialog()
        match t.Editor.FilePath with
        |NotSet _ ->()
        |Deleted fi |SetTo fi ->
            fi.Refresh()
            if fi.Directory.Exists then 
                dlg.InitialDirectory <- fi.DirectoryName
            dlg.FileName <- fi.Name
        dlg.DefaultExt <- ".fsx"
        dlg.Title <- sprintf "Save File As for: %s" (match t.Editor.FilePath with NotSet dummyName -> dummyName  |Deleted fi |SetTo fi -> fi.FullName )
        dlg.Filter <- "FSharp Files(*.fsx, *.fs)|*.fsx;*.fs|Text Files(*.txt)|*.txt|All Files(*.*)|*"
        if isTrue (dlg.ShowDialog()) then
            let fi = new FileInfo(dlg.FileName)
            //this check is not needed, it is done by SaveFileDialog already:
            //if fi.Exists then                
            //    match MessageBox.Show(
            //        IEditor.mainWindow, 
            //        $"Do you want to overwrite the existing file?\r\n{fi.FullName}" , 
            //        "Overwrite file?", 
            //        MessageBoxButton.YesNo, 
            //        MessageBoxImage.Question, 
            //        MessageBoxResult.No,// default result 
            //        MessageBoxOptions.None) with
            //    | MessageBoxResult.Yes -> saveAt (t, fi, saveKind)
            //    | MessageBoxResult.No -> false
            //    | _ -> false
            //else
            
            saveAt (t, fi, saveKind)
        else
            false

    let saveAsync (t:Tab) =  // gets called from evalAllText(),  evalAllTextSave()  and  evalAllTextSaveClear() only
        match t.Editor.FilePath with
        | NotSet _ -> 
            let saved = saveAsDialog(t,SaveNewLocation)
            //if not saved  then log.PrintfnIOErrorMsg "saveAsync and saveAsDialog: did not save previously unsaved file."
            ()
        | SetTo fi ->
            let txt = t.AvaEdit.Text
            async{
                try
                    fi.Refresh()
                    if not <| fi.Directory.Exists then
                        log.PrintfnIOErrorMsg "saveAsync: Directory does not exist, file not saved :\r\n%s" fi.Directory.FullName
                    else
                        IO.File.WriteAllText(fi.FullName, txt,Text.Encoding.UTF8)
                        t.Editor.CodeAtLastSave <- txt
                        t.AvaEdit.Dispatcher.Invoke(fun ()->
                            t.IsCodeSaved <- true
                            log.PrintfnInfoMsg "File saved."
                            //log.PrintfnInfoMsg "File saved:\r\n\"%s\"" fi.FullName
                            )
                    with e ->
                        log.PrintfnIOErrorMsg "saveAsync failed for: %s failed with %A" fi.FullName e
                    } |> Async.Start
         |Deleted _ -> 
            saveAsDialog(t, SaveNewLocation)
            |> ignore 

    let export(t:Tab):bool= 
        saveAsDialog (t, SaveExport)

    /// Returns false if saving operation was canceled or had an error, true on successfully saving
    let trySave (t:Tab)= 
        match t.Editor.FilePath with
        |SetTo fi ->
            if  t.IsCodeSaved then
                log.PrintfnInfoMsg "File already up to date:\r\n%s" fi.FullName
                true
            elif (fi.Refresh(); fi.Exists) then
                saveAt(t, fi, SaveInPlace)
            else
                log.PrintfnIOErrorMsg "File does not exist on drive anymore. Re-saving it at:\r\n%s" fi.FullName
                saveAsDialog(t, SaveNewLocation)
        |Deleted _
        |NotSet _ ->
                saveAsDialog(t, SaveNewLocation)


    /// Returns false if saving operation was canceled or had an error, true on successfully saving
    let trySaveBeforeClosing (t:Tab)= 
        match t.Editor.FilePath with
        |SetTo fi ->
            if  t.IsCodeSaved then                
                true
            elif (fi.Refresh(); fi.Exists) then
                saveAt(t, fi, SaveInPlace)
            else                
                saveAsDialog(t, SaveNewLocationSync)
        |Deleted _
        |NotSet _ ->
                saveAsDialog(t, SaveNewLocationSync)


    /// Returns true if file is saved or if closing ok (not canceled by user)
    let askIfClosingTabIsOk(t:Tab) :bool= 
        if t.IsCodeSaved then 
            true
        else            
            match t.Editor.FilePath with
            |Deleted _ ->  true // don't ask for saving a file that is already deleted
            |SetTo _ 
            |NotSet _ -> 
                match MessageBox.Show(
                    win, 
                    $"Do you want to save the changes to:\r\n{t.FormattedFileName}\r\nbefore closing this tab?" , 
                    "Save Changes?", 
                    MessageBoxButton.YesNoCancel, 
                    MessageBoxImage.Question, 
                    MessageBoxResult.Yes,// default result 
                    MessageBoxOptions.None) with
                | MessageBoxResult.Yes -> trySave t
                | MessageBoxResult.No -> true
                | MessageBoxResult.Cancel -> false
                | _ -> false

    let closeTab(t:Tab)=
        if askIfClosingTabIsOk(t) then
            t.FileTracker.Stop()
            tabs.Items.Remove(t)
            config.OpenTabs.Save (t.Editor.FilePath , allExistingFileInfos) //saving removed file, not added
        

    /// addTab(Tab, makeCurrent, moreTabsToCome)
    let addTab(tab:Tab, makeCurrent, moreTabsToCome) = 
        let idx = tabs.Items.Add tab
        if makeCurrent then
            setCurrentTab(idx)

        tab.CloseButton.Click.Add (fun _ -> closeTab(tab))
        
        match tab.Editor.FilePath with
        |SetTo fi ->            
            if moreTabsToCome then
                config.RecentlyUsedFiles.Add(fi)
            else
                // also close any tab that only has default code:            
                allTabs  
                |> Seq.filter ( fun (t:Tab) -> t.Editor.FilePath.DoesNotExistsAsFile && t.IsCodeSaved=true ) // no explicit criteria for being the default code!
                |> Array.ofSeq // force enumeration and cache
                |> Seq.iter ( fun t -> tabs.Items.Remove t)
                
                config.RecentlyUsedFiles.AddAndSave(fi)                
        |NotSet _ -> 
            ()        
        |Deleted _ -> 
            ISeffLog.log.PrintfnAppErrorMsg "addTab for a deleted file should never happen !"

    /// Checks if file is open already then calls addTab.
    /// tryAddFile(fi:FileInfo, makeCurrent, moreTabsToCome)
    let tryAddFile(fi:FileInfo, makeCurrent, moreTabsToCome) :bool = 
        let areFilesSame (a:FileInfo) (b:FileInfo) = 
            a.FullName.ToLowerInvariant() = b.FullName.ToLowerInvariant()
        let areFilePathsSame (a:FileInfo ) (b:FilePath) = 
            match b with
            |SetTo bb -> areFilesSame bb a
            |NotSet _ -> false
            |Deleted _ -> 
                ISeffLog.log.PrintfnAppErrorMsg "tryAddFile for a deleted file should never happen !"
                false

        fi.Refresh()
        if fi.Exists then
            match allTabs |> Seq.indexed |> Seq.tryFind (fun (_,t) -> areFilePathsSame fi t.Editor.FilePath) with // check if file is already open
            | Some (i,t) ->
                if makeCurrent then // && not t.IsCurrent then
                    setCurrentTab(i)                    
                    config.RecentlyUsedFiles.AddAndSave(fi) // to move it up to top of stack                    
                true
            | None -> // regular case, actually open file
                try
                    let code =  
                        IO.File.ReadAllText (fi.FullName, Text.Encoding.UTF8) 
                        |> Util.Str.unifyLineEndings 
                        |> Util.Str.tabsToSpaces (config.Settings.GetInt("IndentationSize",4))
                    let ed = Editor.SetUp(code, config, SetTo fi)
                    let t = new Tab(ed)
                    t.Editor.CodeAtLastSave <- code
                    //log.PrintfnDebugMsg "adding Tab %A in %A " t.Editor.FilePath t.Editor.FileCheckState
                    addTab(t,makeCurrent, moreTabsToCome)
                    true
                with  e ->
                    log.PrintfnIOErrorMsg "Error reading and adding (with Encoding.UTF8):\r\n%s\r\n%A" fi.FullName e
                    false
        else
            log.PrintfnIOErrorMsg "File not found:\r\n%s" fi.FullName
            MessageBox.Show(
                win, 
                $"File not found:\r\n\r\n{fi.FullName}" , 
                "File not found !", 
                MessageBoxButton.OK, 
                MessageBoxImage.Error, 
                MessageBoxResult.OK ,// default result 
                MessageBoxOptions.None) |> ignore
            false

    /// Return true if at least one file was opened correctly
    let tryAddFiles(paths:string[]) = 
        let last = paths.Length - 1
        paths
        |> Array.indexed
        |> Array.map (fun (num,f) ->
            if num = last then  tryAddFile (FileInfo f, true,  false)
            else                tryAddFile (FileInfo f, false, true ) )
        |> Array.exists id //check if at least one file was opened OK, then true


    /// Shows a file opening dialog
    let openFile() : bool = 
        let dlg = new Microsoft.Win32.OpenFileDialog()
        dlg.Multiselect <- true
        match workingDirectory()  with
        | Some t -> t.Refresh(); if  t.Exists then  dlg.InitialDirectory <- t.FullName
        | _ -> ()
        dlg.DefaultExt <- ".fsx"
        dlg.Title <- "Seff | Open file"
        dlg.Filter <- "FSharp Files(*.fsx, *.fs)|*.fsx;*.fs|Text Files(*.txt)|*.txt|All Files(*.*)|*"
        if isTrue (dlg.ShowDialog()) then
            tryAddFiles dlg.FileNames
        else
            false
    
    
    do
        // --------------first load tabs from last session including startup args--------------
        for f in config.OpenTabs.Get() do
            tryAddFile( f.file, f.makeCurrent, true)  |> ignore

        if tabs.Items.Count=0 then //Open default file if none found in recent files or args
            let t = new Tab(Editor.New(config))
            addTab(t, true, true) |> ignore
        
        if tabs.SelectedIndex = -1 then  //make one tab current if none yet , happens if current file on last closing was an unsaved file                    
            setCurrentTab(0)   
     

        // set up tab change events last so this doesn't get triggered on every tab while opening files initially
        tabs.SelectionChanged.Add( fun _->
            if tabs.Items.Count = 0 then //  happens when closing the last open tab
                //create new tab
                let tab = new Tab(Editor.New(config))
                addTab(tab, true, false)

            else
                let idx = max 0 tabs.SelectedIndex // might be -1 too , there was no tab selected by default" //  does happen                                  
                setCurrentTab(idx)               
                               
            )



    //--------------- Public members------------------


    [<CLIEvent>]  
    member this.OnTabChanged = currentTabChangedEv.Publish

    member this.Control = tabs

    member this.Fsi = fsi

    member this.Config = config

    member this.Current = current

    member this.CurrAvaEdit = current.Editor.AvaEdit

    // excludes files that are deleted but still open in editor
    member this.AllExistingFileInfos = allExistingFileInfos

    member this.AllTabs = allTabs

    member this.AddTab(tab:Tab, makeCurrent) = addTab(tab, makeCurrent, false)

    /// Checks if file is open already then calls addTab
    member this.AddFile(fi:FileInfo, makeCurrent) =  tryAddFile(fi, makeCurrent,false)

    /// Checks if file is open already
    /// last file will be set current
    /// true if at least one opened
    member this.AddFiles(paths: string []) =  tryAddFiles(paths)

    /// Gets the most recently used folder if possible
    member this.WorkingDirectory = workingDirectory()

    /// Shows a file opening dialog
    member this.OpenFile() = openFile()  |> ignore

    /// Shows a file opening dialog
    member this.SaveAs (t:Tab) = saveAsDialog(t, SaveNewLocation)

    /// also saves currently open files
    member this.CloseTab(t) = closeTab(t)

    /// Returns true if saving operation was not canceled
    member this.Save(t:Tab) = trySave(t)

    /// Prints errors to log
    member this.SaveAsync(t:Tab) = saveAsync(t)

    /// Returns true if saving operation was not canceled
    member this.Export(t:Tab) = export(t)

    /// Returns true if saving operation was not canceled
    member this.SaveIncremental (t:Tab) = 
        let isNum c = c >= '0' && c <= '9'
        let incrC (c:Char)   = string( int c - 48 + 1) // 48 = int '0'            
        match t.Editor.FilePath with
        |Deleted fi
        |SetTo fi ->  
            let ne = fi.Name
            let ex = fi.Extension
            let n  = ne.Substring(0,ne.Length-ex.Length)
            let save (nn:string) :bool =                 
                let ni = FileInfo(Path.Combine(fi.DirectoryName, nn + ex ))
                if ni.Directory.Exists then                 
                    if ni.Exists then
                        this.SaveAs(t)
                    else
                        saveAt(t,ni, SaveNewLocation)
                else // directory was deleted too save a new path:
                    saveAsDialog(t, SaveNewLocation)
            
            let l = n[n.Length-1]
            let nn = // the new suffix
                if isNum l then 
                    if n.Length = 1 then 
                        match l with 
                        | '9' -> "10"
                        |  i  -> incrC i
                    else                    
                        let ll = n[n.Length-2]
                        let abc = n.Substring(0,n.Length-2)
                        if isNum ll then
                            match ll,l with 
                            | '9','9' -> "" // null sentinel
                            |  i ,'9' -> abc + incrC  i + "0"
                            |  i , j  -> abc + string i + incrC j
                        else
                            match l with 
                            | '9' -> abc + "10"
                            |  i   -> abc + incrC i
                else
                    ne + "_01"
                
            if nn<>"" then 
                save nn
            else
                this.SaveAs(t)

        |NotSet _ ->
            //log.PrintfnIOErrorMsg "Can't Save Incrementing unsaved file."
            this.SaveAs(t)

    /// Will display a dialog if there are unsaved files.
    /// if user clicks yes it will attempt to save files.
    /// Returns true if all files are saved or unsaved changes are ignored (closing not canceled by user).
    member this.AskForFileSavingToKnowIfClosingWindowIsOk()= 
        let openFs = allTabs |> Seq.filter (fun t -> not t.IsCodeSaved)
        //log.PrintfnDebugMsg "Unsaved files %d" (Seq.length openFs)
        if  Seq.isEmpty openFs then
            true
        else
            let msg = 
                openFs  |> Seq.fold (fun m t ->
                    let name  = match t.Editor.FilePath with NotSet dummyName -> dummyName  |Deleted fi |SetTo fi -> fi.Name
                    sprintf "%s\r\n \r\n%s" m name) "Do you want to\r\nsave the changes to:"
            
            match MessageBox.Show(
                win, 
                msg, 
                "Save Changes?", 
                MessageBoxButton.YesNoCancel, 
                MessageBoxImage.Question, 
                MessageBoxResult.Yes,// default result 
                MessageBoxOptions.None) with
            | MessageBoxResult.Yes ->
                seq { for t in allTabs do if not t.IsCodeSaved then yield trySaveBeforeClosing t } // if saving was canceled ( eg, no filename picked) then cancel closing
                |> Seq.forall id // checks if all are true, if one file-saving was canceled return false,  so the closing of the main window can be aborted
                //if Seq.exists ( fun ok -> ok = false) oks then false else true 
            | MessageBoxResult.No  -> true
            | MessageBoxResult.Cancel  -> false
            | _  -> false // never happening





