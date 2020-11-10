namespace Seff.Views

open Seff

open Seff.Editor
open Seff.Model
open System
open System.IO
open System.Windows.Controls
open System.Windows
open Seff.Views.Util
open Seff.Config
open Seff.Style
open Seff.Editor
open System.Windows.Media


/// A class holding the Tab Control
/// Includes logic for saving and opening files
/// Window ref neded for closing after last Tab closed
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

    let workingDirectory () = 
        match current.FilePath with 
        |SetTo fi -> Some fi.Directory
        |NotSet ->
            match allFileInfos |> Seq.tryHead with
            |Some fi -> Some fi.Directory
            |None    -> None


    let saveAt (t:Tab, fi:FileInfo, updateTab) =                   
        fi.Refresh()
        if not <| fi.Directory.Exists then 
            log.PrintIOErrorMsg "saveAsPath: Directory does not exist:\r\n%s" fi.Directory.FullName 
            false
        else            
            try
                //t.AvaEdit.Save fi.FullName // fails, is it async ?
                IO.File.WriteAllText(fi.FullName, t.AvaEdit.Text)
                if updateTab then 
                    t.IsCodeSaved <- true 
                    t.FilePath <- SetTo fi //this also updates the Tab header and set file info on editor
                    config.RecentlyUsedFiles.AddAndSave(fi)          //TODO this fails if app closes afterward immideatly    
                    config.OpenTabs.Save(t.FilePath , allFileInfos)  //TODO this fails if app closes afterward immideatly              
                    log.PrintInfoMsg "File saved as:\r\n%s" fi.FullName
                else
                    log.PrintInfoMsg "File exported to:\r\n%s" fi.FullName
                true
            with e -> 
                log.PrintIOErrorMsg "saveAt: %s failed with %A" fi.FullName e
                false
                

    /// returns false if saving operation was canceled or had an error, true on sucessfull saving
    let saveAsDialog (t:Tab, updateTab) :bool=         
        let dlg = new Microsoft.Win32.SaveFileDialog()
        match t.FilePath with 
        |NotSet ->() 
        |SetTo fi -> 
            fi.Refresh()
            if fi.Directory.Exists then dlg.InitialDirectory <- fi.DirectoryName
            dlg.FileName <- fi.Name        
        dlg.DefaultExt <- ".fsx"
        dlg.Title <- sprintf "Save File As for: %s" (match t.FilePath with NotSet -> t.FormatedFileName |SetTo fi -> fi.FullName )
        dlg.Filter <- "FSharp Script Files(*.fsx)|*.fsx|Text Files(*.txt)|*.txt|All Files(*.*)|*"
        if isTrue (dlg.ShowDialog()) then
            let fi = new FileInfo(dlg.FileName) 
            if fi.Exists then 
                let msg = sprintf "Do you want to overwrite the existing file?\r\n%s\r\nwith\r\n%s"fi.FullName t.FormatedFileName
                match MessageBox.Show(msg, Style.dialogCaption, MessageBoxButton.YesNo, MessageBoxImage.Question) with
                | MessageBoxResult.Yes -> saveAt (t, fi,updateTab)
                | MessageBoxResult.No -> false
                | _ -> false 
            else
                saveAt (t, fi, updateTab)
        else
            false
    

    let export(t:Tab):bool= 
        saveAsDialog (t, false)

    /// returns false if saving operation was canceled or had an error, true on sucessfull saving
    let trySave (t:Tab)=        
        match t.FilePath with
        |SetTo fi ->         
            if  t.IsCodeSaved then 
                log.PrintInfoMsg "File already up to date:\r\n%s" fi.FullName
                true
            elif (fi.Refresh(); fi.Exists) then
                saveAt(t, fi, true)
            else
                log.PrintIOErrorMsg "File does not exist on drive anymore:\r\n%s" fi.FullName 
                saveAsDialog(t, true)
        |NotSet -> 
                saveAsDialog(t, true)

    /// returns true if file is saved or if closing ok (not canceled by user)
    let askIfClosingTabIsOk(t:Tab) :bool=  
        if t.IsCodeSaved then true
        else 
            let msg = sprintf "Do you want to save the changes to:\r\n%s\r\nbefore closing this tab?" t.FormatedFileName
            match MessageBox.Show(msg, Style.dialogCaption, MessageBoxButton.YesNoCancel, MessageBoxImage.Question) with
            | MessageBoxResult.Yes -> trySave t
            | MessageBoxResult.No -> true
            | _ -> false

    let closeTab(t:Tab)= 
        if askIfClosingTabIsOk(t) then 
            tabs.Items.Remove(t)            
            config.OpenTabs.Save (t.FilePath , allFileInfos)//saving removed file, not added 
    
    ///tab:Tab, makeCurrent, moreTabsToCome
    let addTab(tab:Tab, makeCurrent, moreTabsToCome) = 
        let ix = tabs.Items.Add tab        
        if makeCurrent then  
            tabs.SelectedIndex <- ix
            current <-  tab        
        match tab.FilePath with 
        |SetTo fi -> 
            if moreTabsToCome then 
                config.RecentlyUsedFiles.Add(fi)
            else
                config.RecentlyUsedFiles.AddAndSave(fi)
                config.OpenTabs.Save(tab.FilePath , allFileInfos)  // if makeCurrent this is done in tabs.SelectionChanged event handler below
        |NotSet -> ()
        
        tab.CloseButton.Click.Add (fun _ -> closeTab(tab))
        
    /// checks if file is open already then calls addTtab
    let tryAddFile(fi:FileInfo, makeCurrent, moreTabsToCome) :bool =            
        let areFilesSame (a:FileInfo) (b:FileInfo) = a.FullName.ToLowerInvariant() = b.FullName.ToLowerInvariant()
        
        let areFilePtahsSame (a:FileInfo ) (b:FilePath) = 
            match b with 
            |SetTo bb -> areFilesSame bb a
            |NotSet -> false
        
        fi.Refresh()
        if fi.Exists then            
            match allTabs |> Seq.indexed |> Seq.tryFind (fun (_,t) -> areFilePtahsSame fi t.FilePath) with // check if file is already open             
            | Some (i,t) -> 
                if makeCurrent && not t.IsCurrent then 
                    tabs.SelectedIndex <- i 
                    current <- t
                    config.RecentlyUsedFiles.AddAndSave(fi) // to move it up to top of stack
                    //config.OpenTabs.Save(t.FileInfo , allFileInfos) // done in SelectionChanged event below
                true
            | None -> // regular case, actually open file
                try
                    let code =  IO.File.ReadAllText fi.FullName 
                    let t = new Tab(Editor.SetUp(code, config, SetTo fi))
                    //log.PrintDebugMsg "adding Tab %A in %A " t.FilePath t.Editor.FileCheckState
                    addTab(t,makeCurrent, moreTabsToCome)
                    true
                with  e -> 
                    log.PrintIOErrorMsg "Error reading and adding :\r\n%s\r\n%A" fi.FullName e
                    false
        else
            log.PrintIOErrorMsg "File not found:\r\n%s" fi.FullName
            MessageBox.Show("File not found:\r\n"+fi.FullName , dialogCaption, MessageBoxButton.OK, MessageBoxImage.Error) |> ignore
            false

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
            dlg.FileNames
            |> Seq.indexed
            |> Seq.map (fun (num,f) ->
                let fi = new FileInfo(f)
                if num = 0 then  tryAddFile (fi, true,false) 
                else             tryAddFile (fi, false,false) )
            |> Seq.exists id //check if at least one file was opend OK, then true
        else
            false
    
    do
        
        // --------------first load tabs from last session including startup args--------------
        for f in config.OpenTabs.Get() do 
            tryAddFile( f.file, f.makeCurrent, true)  |> ignore 

        if tabs.Items.Count=0 then //Open default file if none found in recent files or args                
            let t = new Tab(Editor.New(config))
            addTab(t, true, true) |> ignore 
                
        if tabs.SelectedIndex = -1 then    //make one tab current if none yet , happens if current file on last closing was an unsaved file
            tabs.SelectedIndex <- 0
            current <- Seq.head allTabs
        
        // then start highligh errors on current only
        current.Editor.Checker.CkeckHighlightAndFold(current.Editor)  
        config.RecentlyUsedFiles.Save() 
        config.OpenTabs.Save(current.FilePath , allFileInfos)

        //then set up events
        tabs.SelectionChanged.Add( fun _-> // triggered an all tabs on startup ???// when closing, opening or changing tabs  attach first so it will be triggered below when adding files
            if tabs.Items.Count = 0 then //  happens when closing the last open tab
                let didOpen = openFile()
                if not didOpen  then 
                    win.Close() // exit App ? (chrome and edge also closes when closing the last tab, Visual Studio not)                
                    // in case closing gets canceled via an event handler:
                    if win.IsLoaded then 
                        let t = new Tab(Editor.New(config))
                        addTab(t, true, true) |> ignore 
                else
                    if tabs.Items.Count = 0 then log.PrintAppErrorMsg "If no tab is open Window should be closed !!"
            else
                let tab = 
                    if isNull tabs.SelectedItem then tabs.Items.[0] //log.PrintAppErrorMsg "Tabs SelectionChanged handler: there was no tab selected by default" //  does happen 
                    else                             tabs.SelectedItem                 
                let tab = tab :?> Tab
                current <- tab
                for t in allTabs do
                    t.IsCurrent <- false  // first set all false then one true              
                tab.IsCurrent <-true 
                currentTabChangedEv.Trigger(tab) // to update statusbar
                //log.PrintDebugMsg "New current Tab %A " tab.FilePath 
                if tab.Editor.FileCheckState = FileCheckState.NotStarted then 
                    //log.PrintDebugMsg "FileCheckState.NotStarted: starting: %A " tab.FilePath
                    tab.Editor.Checker.CkeckHighlightAndFold(tab.Editor)  // only actually highglights if editor has needsChecking=true              
                config.OpenTabs.Save(tab.FilePath , allFileInfos)
                
            )
       
        

    //--------------- Public members------------------   
    
       
    [<CLIEvent>]  member this.OnTabChanged = currentTabChangedEv.Publish 
       
    member this.Control = tabs
    
    member this.Fsi = fsi

    member this.Config = config

    member this.Current = current 

    member this.CurrAvaEdit = current.Editor.AvaEdit
    
    member this.AllFileInfos = allFileInfos 

    member this.AllTabs = allTabs
    
    member this.AddTab(tab:Tab, makeCurrent) = addTab(tab, makeCurrent,false)

    /// Checks if file is open already then calls addTtab
    member this.AddFile(fi:FileInfo, makeCurrent) =  tryAddFile(fi, makeCurrent,false)
    
    /// Gets the most recently used folder if possible
    member this.WorkingDirectory = workingDirectory()
     
    /// Shows a file opening dialog
    member this.OpenFile() = openFile()  |> ignore 
            
    /// Shows a file opening dialog
    member this.SaveAs (t:Tab) = saveAsDialog(t, true)
    
    /// also saves currently open files 
    member this.CloseTab(t) = closeTab(t) 
    
    /// returns true if saving operation was not canceled
    member this.Save(t:Tab) = trySave(t)    

    /// returns true if saving operation was not canceled
    member this.Export(t:Tab) = export(t)  
    
    /// returns true if saving operation was not canceled
    member this.SaveIncremental (t:Tab) = 
         match t.FilePath with           
         |SetTo fi ->
            let fn = fi.FullName
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
                    saveAt(t,fi, true)                
         |NotSet ->
            log.PrintIOErrorMsg "can't Save Incrementing unsaved file"  
            this.SaveAs(t)
   
    /// returns true if all files are saved or unsaved changes are ignored (closing not canceled by user).
    member this.AskIfClosingWindowIsOk()=             
        let openFs = allTabs |> Seq.filter (fun t -> not t.IsCodeSaved) 
        //log.PrintDebugMsg "Unsaved files %d" (Seq.length openFs)
        if  Seq.isEmpty openFs then
            true
        else
            let msg = openFs  |> Seq.fold (fun m t -> 
                let name  = match t.FilePath with NotSet -> t.FormatedFileName |SetTo fi ->fi.Name 
                sprintf "%s\r\n\r\n%s" m name) "Do you want to\r\nsave the changes to:" 
            match MessageBox.Show(msg, Style.dialogCaption, MessageBoxButton.YesNoCancel, MessageBoxImage.Question) with
            | MessageBoxResult.Yes -> 
                let OKs = seq { for t in allTabs do if not t.IsCodeSaved then yield this.Save t }// if saving was canceled cancel closing
                if Seq.exists ( fun OK -> OK = false) OKs then false else true // iterate unsafed files, if one file saving was canceled abort the closing of the main window                 
            | MessageBoxResult.No  -> true
            | _                    -> false 
    
                                                                                                                           

                           
    