﻿namespace Seff.Views

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
open Seff.Style

type SavingKind = 
    | SaveInPlace
    | SaveExport 
    | SaveNewLocation 
    | SaveNewLocationSync // does not delay the update of recent file and current tabs, for when seff is closing immediately afterwards

/// A class holding the Tab Control.
/// Includes logic for saving and opening files.
/// Window is needed for closing after last Tab closed
type Tabs(config:Config, win:Window) = 

    let tabs = new TabControl(
                        Padding = Thickness(0.6),
                        Margin = Thickness( 0.6),
                        BorderThickness = Thickness(0.6),
                        BorderBrush = Brushes.Black
                        )



    let log = config.Log

    let fsi = Fsi.GetOrCreate(config)

    let allTabs:seq<Tab> =  Seq.cast tabs.Items

    let allFileInfos = seq{ for t in allTabs do match t.FilePath with NotSet ->() |SetTo fi -> yield fi } //TODO does this reevaluate every time?

    let currentTabChangedEv = new Event<Tab>() //to Trigger Fs Checker and status bar update 

    let mutable current =  Unchecked.defaultof<Tab>

    let enviroDefaultDir = Environment.CurrentDirectory 

    let workingDirectory () = 
        match current.FilePath with
        |SetTo fi -> Some fi.Directory
        |NotSet ->
            match allFileInfos |> Seq.tryHead with
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
                |SaveNewLocation ->
                    t.IsCodeSaved <- true
                    t.FilePath <- SetTo fi //this also updates the Tab header and set file info on editor    
                    Environment.CurrentDirectory <- fi.Directory.FullName 
                    config.RecentlyUsedFiles.AddAndSave(fi)          
                    config.OpenTabs.Save(t.FilePath , allFileInfos)   
                    config.FoldingStatus.Set(t.Editor) // otherwise no record would exist for the new file name
                    log.PrintfnInfoMsg "File saved as:\r\n\"%s\"" fi.FullName    
                |SaveNewLocationSync -> 
                    t.IsCodeSaved <- true
                    t.FilePath <- SetTo fi //this also updates the Tab header and set file info on editor
                    Environment.CurrentDirectory <- fi.Directory.FullName  
                    config.RecentlyUsedFiles.AddAndSaveSync(fi)         
                    config.OpenTabs.SaveSync(t.FilePath , allFileInfos)
                    config.FoldingStatus.Set(t.Editor) // otherwise no record would exist for the new file name
                    log.PrintfnInfoMsg "File saved as:\r\n\"%s\"" fi.FullName   
                |SaveInPlace -> // also called for Save-All command
                    t.IsCodeSaved <- true
                    log.PrintfnInfoMsg "File saved:\r\n\"%s\"" fi.FullName
                |SaveExport ->
                    config.FoldingStatus.Set(t.Editor) // otherwise no record would exist for the new file name
                    log.PrintfnInfoMsg "File exported to:\r\n\"%s\"" fi.FullName
                true
            with e ->
                log.PrintfnIOErrorMsg "saveAt failed for: %s failed with %A" fi.FullName e
                false



    /// Returns false if saving operation was canceled or had an error, true on successful saving
    let saveAsDialog (t:Tab, saveKind:SavingKind) :bool= 
        let dlg = new Microsoft.Win32.SaveFileDialog()
        match t.FilePath with
        |NotSet ->()
        |SetTo fi ->
            fi.Refresh()
            if fi.Directory.Exists then dlg.InitialDirectory <- fi.DirectoryName
            dlg.FileName <- fi.Name
        dlg.DefaultExt <- ".fsx"
        dlg.Title <- sprintf "Save File As for: %s" (match t.FilePath with NotSet -> t.FormattedFileName |SetTo fi -> fi.FullName )
        dlg.Filter <- "FSharp Script Files(*.fsx)|*.fsx|Text Files(*.txt)|*.txt|All Files(*.*)|*"
        if isTrue (dlg.ShowDialog()) then
            let fi = new FileInfo(dlg.FileName)
            if fi.Exists then
                let msg = sprintf "Do you want to overwrite the existing file?\r\n%s\r\nwith\r\n%s"fi.FullName t.FormattedFileName
                match MessageBox.Show(msg, Style.dialogCaption, MessageBoxButton.YesNo, MessageBoxImage.Question) with
                | MessageBoxResult.Yes -> saveAt (t, fi, saveKind)
                | MessageBoxResult.No -> false
                | _ -> false
            else
                saveAt (t, fi, saveKind)
        else
            false


    let saveAsync (t:Tab) = 
        match t.FilePath with
        | NotSet -> if not <| saveAsDialog(t,SaveNewLocation) then log.PrintfnIOErrorMsg "saveAsync and saveAsDialog: did not save previously unsaved file."
        | SetTo fi ->
            let txt = t.AvaEdit.Text
            async{
                try
                    fi.Refresh()
                    if not <| fi.Directory.Exists then
                        log.PrintfnIOErrorMsg "saveAsync: Directory does not exist:\r\n%s" fi.Directory.FullName
                    else
                        IO.File.WriteAllText(fi.FullName, txt,Text.Encoding.UTF8)
                        t.AvaEdit.Dispatcher.Invoke(fun ()->
                            t.IsCodeSaved <- true
                            log.PrintfnInfoMsg "File saved."
                            //log.PrintfnInfoMsg "File saved:\r\n\"%s\"" fi.FullName
                            )
                    with e ->
                        log.PrintfnIOErrorMsg "saveAsync failed for: %s failed with %A" fi.FullName e
                    } |> Async.Start

    let export(t:Tab):bool= 
        saveAsDialog (t, SaveExport)

    /// Returns false if saving operation was canceled or had an error, true on successfully saving
    let trySave (t:Tab)= 
        match t.FilePath with
        |SetTo fi ->
            if  t.IsCodeSaved then
                log.PrintfnInfoMsg "File already up to date:\r\n%s" fi.FullName
                true
            elif (fi.Refresh(); fi.Exists) then
                saveAt(t, fi, SaveInPlace)
            else
                log.PrintfnIOErrorMsg "File does not exist on drive anymore. Resaving it at:\r\n%s" fi.FullName
                saveAsDialog(t, SaveNewLocation)
        |NotSet ->
                saveAsDialog(t, SaveNewLocation)


    /// Returns false if saving operation was canceled or had an error, true on successfully saving
    let trySaveBeforeClosing (t:Tab)= 
        match t.FilePath with
        |SetTo fi ->
            if  t.IsCodeSaved then                
                true
            elif (fi.Refresh(); fi.Exists) then
                saveAt(t, fi, SaveInPlace)
            else                
                saveAsDialog(t, SaveNewLocationSync)
        |NotSet ->
                saveAsDialog(t, SaveNewLocationSync)


    /// Returns true if file is saved or if closing ok (not canceled by user)
    let askIfClosingTabIsOk(t:Tab) :bool= 
        if t.IsCodeSaved then true
        else
            let msg = sprintf "Do you want to save the changes to:\r\n%s\r\nbefore closing this tab?" t.FormattedFileName
            match MessageBox.Show(msg, Style.dialogCaption, MessageBoxButton.YesNoCancel, MessageBoxImage.Question) with
            | MessageBoxResult.Yes -> trySave t
            | MessageBoxResult.No -> true
            | _ -> false

    let closeTab(t:Tab)= 
        let wasWatching = t.FileWatcher.EnableRaisingEvents
        t.FileWatcher.EnableRaisingEvents <- false
        if askIfClosingTabIsOk(t) then
            tabs.Items.Remove(t)
            config.OpenTabs.Save (t.FilePath , allFileInfos) //saving removed file, not added
        else // closing cancelled, keep on watching file
            if wasWatching then
                t.FileWatcher.EnableRaisingEvents <- true

    /// addTab(Tab, makeCurrent, moreTabsToCome)
    let addTab(tab:Tab, makeCurrent, moreTabsToCome) = 
        let ix = tabs.Items.Add tab
        if makeCurrent then
            tabs.SelectedIndex <- ix
            current <-  tab
            IEditor.current <- Some (tab.Editor:>IEditor)
            // also close any tab that only has default code:
            if tab.FilePath <> NotSet then
                let rems = allTabs  |> Seq.filter ( fun (t:Tab) -> t.FilePath = NotSet && t.IsCodeSaved=true ) |> Array.ofSeq // force enumeration
                for rem in rems do tabs.Items.Remove rem

        match tab.FilePath with
        |SetTo fi ->
            if moreTabsToCome then
                config.RecentlyUsedFiles.Add(fi)
            else
                config.RecentlyUsedFiles.AddAndSave(fi)
                config.OpenTabs.Save(tab.FilePath , allFileInfos)  // if makeCurrent this is done in tabs.SelectionChanged event handler below
        |NotSet -> ()

        tab.CloseButton.Click.Add (fun _ -> closeTab(tab))

    /// Checks if file is open already then calls addTab.
    /// tryAddFile(fi:FileInfo, makeCurrent, moreTabsToCome)
    let tryAddFile(fi:FileInfo, makeCurrent, moreTabsToCome) :bool = 
        let areFilesSame (a:FileInfo) (b:FileInfo) = a.FullName.ToLowerInvariant() = b.FullName.ToLowerInvariant()

        let areFilePathsSame (a:FileInfo ) (b:FilePath) = 
            match b with
            |SetTo bb -> areFilesSame bb a
            |NotSet -> false

        fi.Refresh()
        if fi.Exists then
            match allTabs |> Seq.indexed |> Seq.tryFind (fun (_,t) -> areFilePathsSame fi t.FilePath) with // check if file is already open
            | Some (i,t) ->
                if makeCurrent && not t.IsCurrent then
                    tabs.SelectedIndex <- i
                    current <- t
                    IEditor.current <- Some (t.Editor:>IEditor)
                    config.RecentlyUsedFiles.AddAndSave(fi) // to move it up to top of stack
                    //config.OpenTabs.Save(t.FileInfo , allFileInfos) // done in SelectionChanged event below
                true
            | None -> // regular case, actually open file
                try
                    let code =  IO.File.ReadAllText (fi.FullName, Text.Encoding.UTF8)
                    let t = new Tab(Editor.SetUp(code, config, SetTo fi), config, allFileInfos)
                    //log.PrintfnDebugMsg "adding Tab %A in %A " t.FilePath t.Editor.FileCheckState
                    addTab(t,makeCurrent, moreTabsToCome)
                    true
                with  e ->
                    log.PrintfnIOErrorMsg "Error reading and adding (with Encoding.UTF8):\r\n%s\r\n%A" fi.FullName e
                    false
        else
            log.PrintfnIOErrorMsg "File not found:\r\n%s" fi.FullName
            MessageBox.Show("File not found:\r\n"+fi.FullName , dialogCaption, MessageBoxButton.OK, MessageBoxImage.Error) |> ignore
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
        dlg.Title <- "Open file for " + Style.dialogCaption
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
            let t = new Tab(Editor.New(config), config, allFileInfos)
            addTab(t, true, true) |> ignore

        if tabs.SelectedIndex = -1 then    //make one tab current if none yet , happens if current file on last closing was an unsaved file
            tabs.SelectedIndex <- 0
            let tab = Seq.head allTabs
            current <- tab
            IEditor.current <- Some (tab.Editor:>IEditor)

        Environment.CurrentDirectory <- match current.FilePath with |SetTo fi -> fi.Directory.FullName  |NotSet -> enviroDefaultDir // to use __SOURCE_DIRECTORY__

        // then start highlighting errors on current tab only
        current.Editor.GlobalChecker.CheckThenHighlightAndFold(current.Editor)
        config.OpenTabs.Save(current.FilePath , allFileInfos)
        // config.RecentlyUsedFiles.Save is called in addTab function

        // set up tab change events last so this doesn't get triggered on every tab while opening files initially
        tabs.SelectionChanged.Add( fun _->
            if tabs.Items.Count = 0 then //  happens when closing the last open tab
                //create new tab
                let tab = new Tab(Editor.New(config), config, allFileInfos)
                addTab(tab, true, false)

            else
                let tab = 
                    if isNull tabs.SelectedItem then tabs.Items.[0] //log.PrintfnAppErrorMsg "Tabs SelectionChanged handler: there was no tab selected by default" //  does happen
                    else                             tabs.SelectedItem
                let tab = tab :?> Tab
                current <- tab
                IEditor.current <- Some (tab.Editor:>IEditor)
                for t in allTabs do
                    t.IsCurrent <- false  // first set all false then one true
                tab.IsCurrent <- true
                Environment.CurrentDirectory <- match current.FilePath with |SetTo fi -> fi.Directory.FullName  |NotSet -> enviroDefaultDir // to use __SOURCE_DIRECTORY__
                currentTabChangedEv.Trigger(tab) // to update statusbar
                
                if tab.Editor.FileCheckState = FileCheckState.NotStarted then
                    //log.PrintfnDebugMsg "FileCheckState.NotStarted: starting: %A " tab.FilePath
                    tab.Editor.GlobalChecker.CheckThenHighlightAndFold(tab.Editor)  // only actually highlights if editor has needsChecking=true

                // TODO make sure to reset checker if it is currently still running from another file
                // even though the error highlighter is only called if the editor id is the same, see Editor.fs:  ed.GlobalChecker.OnChecked.Add(fun ...

                config.OpenTabs.Save(tab.FilePath , allFileInfos)
                // config.RecentlyUsedFiles.Save is called in addTab function
            )



    //--------------- Public members------------------


    [<CLIEvent>]  
    member this.OnTabChanged = currentTabChangedEv.Publish

    member this.Control = tabs

    member this.Fsi = fsi

    member this.Config = config

    member this.Current = current

    member this.CurrAvaEdit = current.Editor.AvaEdit

    member this.AllFileInfos = allFileInfos

    member this.AllTabs = allTabs

    member this.AddTab(tab:Tab, makeCurrent) = addTab(tab, makeCurrent, false)

    /// Checks if file is open already then calls addTtab
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

    /// prints errors to log
    member this.SaveAsync(t:Tab) = saveAsync(t)

    /// Returns true if saving operation was not canceled
    member this.Export(t:Tab) = export(t)

    /// Returns true if saving operation was not canceled
    member this.SaveIncremental (t:Tab) = 
        let incrC (c:Char)   = string( int c - 48 + 1) // 48 = int '0'
        let incrS (c:string) = string( int c      + 1)        
        match t.FilePath with
        |SetTo fi ->             
            if fi.Extension = ".fsx" then // just in case it is not??
                let save (nn:string) :bool = 
                    let p = Path.Combine(fi.DirectoryName, nn )
                    let ni = FileInfo(p)
                    if ni.Exists then
                        this.SaveAs(t)
                    else
                        saveAt(t,ni, SaveNewLocation)
            
                let replaceEnd       (e:string) = (Util.Str.removeAtEnd (4+e.Length) fi.Name) + e + ".fsx" |> save            
                let appendEnd        (e:string) = (Util.Str.removeAtEnd (4         ) fi.Name) + e + ".fsx" |> save
                let trimOneAppendEnd (e:string) = (Util.Str.removeAtEnd (5         ) fi.Name) + e + ".fsx" |> save
            
                let fn = fi.Name            
                let l = fn.[fn.Length-5] // last char           
                if  '0' <= l && l <= '8' then replaceEnd (incrC l)
                elif            l  = '9' then 
                    if fn.Length < 6 then trimOneAppendEnd ("10")
                    else
                   
                        let ll =  fn.[fn.Length-6] // second last char 
                        if  '0' <= ll && ll <= '8' then replaceEnd (incrC ll + "0")
                        elif             ll  = '9' then this.SaveAs(t) // reached 99
                        else appendEnd ("_01")
                else appendEnd ("_01")
            else
                this.SaveAs(t)

        |NotSet ->
            log.PrintfnIOErrorMsg "can't Save Incrementing unsaved file"
            this.SaveAs(t)

    /// will display a dialog if there are unsaved files.
    /// if user clicks yes it will attempt to save files.
    /// Returns true if all files are saved or unsaved changes are ignored (closing not canceled by user).
    member this.AskForFileSavingToKnowIfClosingWindowIsOk()= 
        let openFs = allTabs |> Seq.filter (fun t -> not t.IsCodeSaved)
        //log.PrintfnDebugMsg "Unsaved files %d" (Seq.length openFs)
        if  Seq.isEmpty openFs then
            true
        else
            let msg = openFs  |> Seq.fold (fun m t ->
                let name  = match t.FilePath with NotSet -> t.FormattedFileName |SetTo fi ->fi.Name
                sprintf "%s\r\n \r\n%s" m name) "Do you want to\r\nsave the changes to:"
            match MessageBox.Show(msg, Style.dialogCaption, MessageBoxButton.YesNoCancel, MessageBoxImage.Question) with
            | MessageBoxResult.Yes ->
                seq { for t in allTabs do if not t.IsCodeSaved then yield trySaveBeforeClosing t } // if saving was canceled ( eg, no filename picked) then cancel closing
                |> Seq.forall id // checks if all are true, if one file-saving was canceled return false,  so the closing of the main window can be aborted
                //if Seq.exists ( fun ok -> ok = false) oks then false else true 
            | MessageBoxResult.No  -> true
            | MessageBoxResult.Cancel  -> false
            | _  -> false // never happening





