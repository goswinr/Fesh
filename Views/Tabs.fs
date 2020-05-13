﻿namespace Seff.Views

open Seff

open Seff.Editor
open System
open System.IO
open System.Windows.Controls
open System.Windows
open Seff.Views.Util
open Seff.Config
open Seff.Appearance
open Seff.Editor

/// A class holding the Tab Control
/// Includes logic for saving and opening files
/// Window ref neded for closing after last Tab closed
type Tabs(config:Config, win:Window) = 

    let tabs = new TabControl()
    
    let log = config.Log 

    let fsi = Fsi.GetOrCreate(config)

    let allTabs:seq<Tab> =  Seq.cast tabs.Items
    
    let allFileInfos = seq{ for t in allTabs do if  t.FileInfo.IsSome then yield t.FileInfo.Value } //TODO does this reevaluate every time?
    
    //delete //let currentTabChangedEv = new Event<Tab>() //to Trigger Fs Checker

    let currentTabChangedEv = new Event<Tab>() //to Trigger Fs Checker 
    
    let mutable current =  Unchecked.defaultof<Tab>

    let saveAt (t:Tab, fi:FileInfo) =                   
        if not <| fi.Directory.Exists then 
            log.PrintIOErrorMsg "saveAsPath: Directory does not exist:\r\n%s" fi.Directory.FullName 
            false
        else
            t.AvaEdit.Save fi.FullName            
            if not <| fi.Exists then 
                log.PrintIOErrorMsg "saveAsPath: File was not saved:\r\n%s" t.FormatedFileName
                false
            else
                t.IsCodeSaved <- true 
                t.FileInfo <- Some fi //this also updates the Tab header and set file info on editor
                config.RecentlyUsedFiles.Save(fi)
                config.OpenTabs.Save(t.FileInfo , allFileInfos)                
                log.PrintInfoMsg "File saved as:\r\n%s" t.FormatedFileName
                true

    /// returns true if saving operation was not canceled
    let saveAsDialog (t:Tab) :bool= 
        let dlg = new Microsoft.Win32.SaveFileDialog()
        if t.FileInfo.IsSome && t.FileInfo.Value.Directory.Exists then dlg.InitialDirectory <- t.FileInfo.Value.DirectoryName
        if t.FileInfo.IsSome then dlg.FileName <- t.FileInfo.Value.Name        
        dlg.DefaultExt <- ".fsx"
        dlg.Title <- sprintf "Save File As for: %s" (if t.FileInfo.IsSome then  t.FileInfo.Value.FullName else t.FormatedFileName)
        dlg.Filter <- "FSharp Script Files(*.fsx)|*.fsx|Text Files(*.txt)|*.txt|All Files(*.*)|*"
        if isTrue (dlg.ShowDialog()) then
            let fi = new FileInfo(dlg.FileName) 
            if fi.Exists then 
                let msg = sprintf "Do you want to overwrite the existing file?\r\n%s" t.FormatedFileName
                match MessageBox.Show(msg, Appearance.dialogCaption, MessageBoxButton.YesNo, MessageBoxImage.Question) with
                | MessageBoxResult.Yes -> saveAt (t, fi)
                | MessageBoxResult.No -> false
                | _ -> false 
            else
                saveAt (t, fi)
        else
            false

    let trySave (t:Tab)=
        if t.FileInfo.IsSome && t.FileInfo.Value.Exists then
            if not t.IsCodeSaved then
                t.AvaEdit.Save t.FileInfo.Value.FullName 
                t.IsCodeSaved <- true 
                log.PrintInfoMsg "File saved at:\r\n%s" t.FileInfo.Value.FullName           
                true
            else
                log.PrintInfoMsg "File already up to date:\r\n%s" t.FileInfo.Value.FullName  
                true
        else 
            if t.FileInfo.IsNone then () 
            elif not <| t.FileInfo.Value.Exists then 
                log.PrintIOErrorMsg "File does not exist on drive anymore:\r\n%s" t.FileInfo.Value.FullName  
                MessageBox.Show("File does not exist on drive anymore:\r\n" + t.FileInfo.Value.FullName , dialogCaption, MessageBoxButton.OK, MessageBoxImage.Error) |> ignore
            saveAsDialog(t)

    /// returns true if file is saved or if closing ok (not canceled by user)
    let askIfClosingTabIsOk(t:Tab) :bool=  
        if t.IsCodeSaved then true
        else 
            let msg = sprintf "Do you want to save the changes to:\r\n%s\r\nbefore closing this tab?" t.FormatedFileName
            match MessageBox.Show(msg, Appearance.dialogCaption, MessageBoxButton.YesNoCancel, MessageBoxImage.Question) with
            | MessageBoxResult.Yes -> trySave t
            | MessageBoxResult.No -> false
            | _ -> false

    let closeTab(t:Tab)= 
        if askIfClosingTabIsOk(t) then 
            tabs.Items.Remove(t)            
            config.OpenTabs.Save (t.FileInfo , allFileInfos)//saving removed file, not added 

    let addTab(tab:Tab, makeCurrent) = 
        let ix = tabs.Items.Add tab        
        if makeCurrent then  
            tabs.SelectedIndex <- ix
            current <-  tab        
        match tab.FileInfo with 
        |Some fi -> 
            config.RecentlyUsedFiles.Save(fi)
            if not makeCurrent then config.OpenTabs.Save(tab.FileInfo , allFileInfos)  // if makeCurrent this is done in tabs.SelectionChanged event handler below
        |None -> ()
        
        tab.CloseButton.Click.Add (fun _ -> closeTab(tab))
       
        
    /// checks if file is open already then calls addTtab
    let tryAddFile(fi:FileInfo, makeCurrent) =            
        let areFilesSame (a:FileInfo) (b:FileInfo) = a.FullName.ToLowerInvariant() = b.FullName.ToLowerInvariant()
        
        let areFilesOptionsSame (a:FileInfo ) (b:FileInfo Option) = 
            match b with 
            |Some bb -> areFilesSame bb a
            |_ -> false

        if fi.Exists then            
            match allTabs |> Seq.indexed |> Seq.tryFind (fun (_,t) -> areFilesOptionsSame fi t.FileInfo) with // check if file is already open             
            | Some (i,t) -> 
                if makeCurrent && not t.IsCurrent then 
                    tabs.SelectedIndex <- i 
                    current <- t
                    config.RecentlyUsedFiles.Save(fi) // to move it up to top of stack
                    //config.OpenTabs.Save(t.FileInfo , allFileInfos) // done in SelectionChanged event below
            | None -> 
                try
                    let code = IO.File.ReadAllText fi.FullName
                    addTab(new Tab(new Editor(code, config, Some fi)),makeCurrent)
                with  e -> 
                    log.PrintIOErrorMsg "Error reading and adding :\r\n%s\r\n%A" fi.FullName e
        else
            log.PrintIOErrorMsg "File not found:\r\n%s" fi.FullName
            MessageBox.Show("File not found:\r\n"+fi.FullName , dialogCaption, MessageBoxButton.OK, MessageBoxImage.Error) |> ignore

    let setFontSize (newSize) = // on log and all tabs
        log.ReadOnlyEditor.FontSize    <- newSize
        for t in allTabs do                
            t.Editor.AvaEdit.FontSize  <- newSize        
        config.Settings.SetFloat "FontSize" newSize 
        Appearance.fontSize <- newSize
        config.Settings.Save ()
        log.PrintInfoMsg "new Fontsize: %.1f" newSize
    
    do
        tabs.SelectionChanged.Add( fun _-> // when closing, opening or changing tabs  attach first so it will be triggered below when adding files
            if tabs.Items.Count = 0 then //  happens when closing the last open tab
                win.Close() // exit App ? (chrome and edge also closes when closing the last tab, Visual Studio not)
            else
                let tab = 
                    if isNull tabs.SelectedItem then 
                        log.PrintAppErrorMsg "Tabs SelectionChanged handler: there was no tab selected by default" // should never happen
                        tabs.Items.[0]
                    else 
                        tabs.SelectedItem                 
                let tab = tab :?> Tab
                current <- tab
                for t in allTabs do
                    t.Editor.IsCurrent <- false 
                    t.IsCurrent <- false  // first set all false then one true              
                tab.IsCurrent <-true
                tab.Editor.IsCurrent <- true                
                tab.Editor.Checker.CkeckAndHighlight(tab.Editor.AvaEdit,tab.FileInfo)
                //delete //currentTabChangedEv.Trigger(tab) // checker is started above, does not need this event to start
                config.OpenTabs.Save(tab.FileInfo , allFileInfos)
                log.PrintDebugMsg "Current Tab changed to %s" tab.FormatedFileName
            )
        
        for f in config.OpenTabs.Get() do 
            tryAddFile( f.file, f.makeCurrent)  |> ignore 

        if tabs.Items.Count=0 then //Open default file if none found in recent files or args                
            addTab(new Tab(new Editor(config)), true) |> ignore 
                
        if tabs.SelectedIndex = -1 then    //make one tab current  if none yet
            log.PrintAppErrorMsg "Tabs Constructor 'do' block: there was no tab selected by default" // should never happen
            tabs.SelectedIndex <- 0
            current <- Seq.head allTabs
        
        
        current.Editor.Checker.CkeckAndHighlight(current.Editor.AvaEdit,current.FileInfo)
        //delete//currentTabChangedEv.Trigger(current)  //TODO check if triggerd here and in SelectionChanged
        log.PrintDebugMsg "Current Tab set from do block end %s" current.FormatedFileName
        

    //--------------- Public members------------------
   
    member this.Control = tabs
    
    member this.Fsi = fsi

    member this.Config = config

    member this.Current = current 

    member this.CurrAvaEdit = current.Editor.AvaEdit
    
    member this.AllFileInfos = allFileInfos 

    member this.AllTabs = allTabs
    
    member this.AddTab(tab:Tab, makeCurrent) = addTab(tab, makeCurrent)

    /// checks if file is open already then calls addTtab
    member this.AddFile(fi:FileInfo, makeCurrent) =  tryAddFile(fi, makeCurrent)
    
    member this.WorkingDirectory = 
        match current.FileInfo with 
        |Some fi -> Some fi.Directory
        |None ->
            match allFileInfos |> Seq.tryHead with
            |Some fi -> Some fi.Directory
            |None    -> None
     
    /// Shows a file opening dialog
    member this.OpenFile() =
        let dlg = new Microsoft.Win32.OpenFileDialog()
        dlg.Multiselect <- true
        match this.WorkingDirectory  with 
        | Some t when t.Exists -> dlg.InitialDirectory <- t.FullName
        | _ -> ()
        dlg.DefaultExt <- ".fsx"
        dlg.Title <- "Open file for " + Appearance.dialogCaption
        dlg.Filter <- "FSharp Files(*.fsx, *.fs)|*.fsx;*.fs|Text Files(*.txt)|*.txt|All Files(*.*)|*"
        if isTrue (dlg.ShowDialog()) then
            for num,f in Seq.indexed dlg.FileNames do
                let fi = new FileInfo(f)
                if num = 0 then  tryAddFile (fi, true) 
                else             tryAddFile (fi, false)
            
    /// Shows a file opening dialog
    member this.SaveAs (t:Tab) = saveAsDialog(t)
    
    /// also saves currently open files 
    member this.CloseTab(t) = closeTab(t) 
    
    /// returns true if saving operation was not canceled
    member this.Save(t:Tab) = trySave(t)
    
    
    /// returns true if saving operation was not canceled
    member this.SaveIncremental (t:Tab) = 
         if t.FileInfo.IsSome then            
            let fn = t.FileInfo.Value.FullName
            let last = fn.[fn.Length-5]
            if not <| Char.IsLetterOrDigit last then 
                log.PrintInfoMsg "Save Incrementing failed on last value: '%c' on: \r\n%s" last fn
                this.Save(t)
            elif last = 'z' || last = 'Z' || last = '9' then                
                log.PrintInfoMsg "Save Incrementing reached last value: '%c' on: \r\n%s" last fn
                this.SaveAs(t)
            else
                let newLast = char(int(last)+1)
                let npath =
                    let letters = fn.ToCharArray()
                    letters.[fn.Length-5] <- newLast
                    String.Join("", letters)
                let fi = new FileInfo(npath)
                if fi.Exists then 
                    this.SaveAs(t)
                else
                    saveAt(t,fi)                
         else
            log.PrintIOErrorMsg "can't Save Incrementing unsaved file"  
            this.SaveAs(t)
    
    [<CLIEvent>]
    member this.OnTabChanged = currentTabChangedEv.Publish 
    
    /// returns true if all files are saved or unsaved changes are ignored (closing not canceled by user).
    member this.AskIfClosingWindowIsOk()=             
        let openFs = allTabs |> Seq.filter (fun t -> not t.IsCodeSaved) 
        if  Seq.isEmpty openFs then
            true
        else
            let msg = openFs  |> Seq.fold (fun m t -> 
                let name  = if t.FileInfo.IsSome then t.FileInfo.Value.Name else t.FormatedFileName
                sprintf "%s\r\n\r\n%s" m name) "Do you want to\r\nsave the changes to:" 
            match MessageBox.Show(msg, Appearance.dialogCaption, MessageBoxButton.YesNoCancel, MessageBoxImage.Question) with
            | MessageBoxResult.Yes -> 
                let OKs = seq { for t in allTabs do if not t.IsCodeSaved then yield this.Save t }// if saving was canceled cancel closing
                if Seq.exists ( fun OK -> OK = false) OKs then false else true // iterate unsafed files, if one file saving was canceled abort the closing of the main window                 
            | MessageBoxResult.No  -> true
            | _                    -> false 
    
    /// affects Editor and Log    
    member this.FontsBigger()= 
        let cs = current.Editor.AvaEdit.FontSize
        //let step = 
        //    if   cs >= 36. then 4. 
        //    elif cs >= 20. then 2. 
        //    else                1.
        //if cs < 112. then setFontSize(cs+step)
        if cs < 250. then setFontSize(cs* 1.03) // 3% steps
    
    /// affects Editor and Log
    member this.FontsSmaller()=
        let cs = current.Editor.AvaEdit.FontSize
        //let step = 
        //    if   cs >= 36. then 4. 
        //    elif cs >= 20. then 2. 
        //    else                1.
        //if cs > 5. then setFontSize(cs-step)
        if cs > 3. then setFontSize(cs * 0.97) // 3% steps       
    
      
                                                                                                                           
    member this.EvalAllText()        =  fsi.Evaluate                            {code=current.Editor.AvaEdit.Text                             ; file=current.FileInfo; allOfFile=true}                               
    member this.EvalAllTextSave()    =  if this.Save(current) then fsi.Evaluate {code=current.Editor.AvaEdit.Text                             ; file=current.FileInfo; allOfFile=true} 
    member this.EvalSelectedLines()  =  fsi.Evaluate                            {code = Selection.expandSelectionToFullLines(current.AvaEdit) ; file=current.FileInfo; allOfFile=false} 
    member this.EvalSelectedText()   =  fsi.Evaluate                            {code = current.AvaEdit.SelectedText                          ; file=current.FileInfo; allOfFile=false} 
                           
    