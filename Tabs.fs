namespace Seff

open System
open System.Environment
open System.IO
open System.Threading
open Seff.Model
open ICSharpCode
open System.Windows.Media // for color brushes
open System.Text
open System.Diagnostics
open System.Collections.Generic
open System.Windows.Controls
open System.Windows
open Seff.Util.WPF

/// A Static class holding the Tab Control
/// Includes logic saving and opening files
type Tabs private ()=

    static let tabs = new TabControl()

    static let allTabs:  seq<Tab> =  Seq.cast tabs.Items
    
    static let mutable current = None
    
    static let currentTabChangedEv = new Event<Tab>()  
    
    static let savedAsEv = new Event<FileInfo>()
    
    static let saveAs (t:Tab,fi:FileInfo) =                   
        if not <| fi.Directory.Exists then 
            Log.PrintIOErrorMsg "saveAsPath: Directory does not exist:\r\n%s" fi.Directory.FullName 
            false
        else
            t.Editor.Save fi.FullName            
            if not <| fi.Exists then 
                Log.PrintIOErrorMsg "saveAsPath: File was not saved:\r\n%s" t.FormatedFileName
                false
            else
                t.IsCodeSaved <- true 
                t.FileInfo <- Some fi
                Config.RecentlyUsedFiles.Save(fi)
                Config.CurrentlyOpenFiles.Save(t.FileInfo , Tabs.AllFileInfos)
                savedAsEv.Trigger(fi) // TODO updateRecentMenu fi   
                Log.PrintInfoMsg "File saved as:\r\n%s" t.FormatedFileName
                true
    
    static let closeTab(t:Tab)= 
        if FileDialogs.askIfClosingTabIsOk(t,saveAs) then 
            tabs.Items.Remove(t)            
            Config.CurrentlyOpenFiles.Save (t.FileInfo , Tabs.AllFileInfos)//saving removed file, not added 
    
    do
        tabs.SelectionChanged.Add( fun _-> // when closing, opening or changing tabs
            let ob = tabs.SelectedItem 
            if isNull ob then //  happens when closing the last open tab
                if Tabs.MainWindow<>null then Tabs.MainWindow.Close() // exit App ? (chrome and edge also closes when closing the last tab, Visual Studio not)
            else
                let tab = ob :?> Tab
                for t in allTabs do t.IsCurrent<- false  // first set all false then one true              
                tab.IsCurrent <-true
                current <- Some tab
                currentTabChangedEv.Trigger(tab) // to start fschecker
                Config.CurrentlyOpenFiles.Save(tab.FileInfo , Tabs.AllFileInfos)
            )
    
    
    //--------------- Public members------------------
    static member val MainWindow:Window = null with get,set // neded for closing afterlast tab set in Win.fs

    static member Control = tabs

    static member Current = match current with Some t -> t | None -> failwith "Tabs.Current shall never be None!"
    
    static member AllFileInfos = allTabs |> Seq.map(fun t -> t.FileInfo)

    static member AllTabs = allTabs

    static member WorkingDirectory = 
        match Tabs.Current.FileInfo with 
        |Some fi -> Some fi.Directory
        |None ->
            match Tabs.AllFileInfos |> Seq.tryFind Option.isSome with
            |Some fi -> Some fi.Value.Directory
            |None    -> None
 

    static member AddTab(tab:Tab, makeCurrent) = 
        let ix = tabs.Items.Add tab
        if makeCurrent then  tabs.SelectedIndex <- ix
        match tab.FileInfo with 
        |Some fi -> 
            Config.RecentlyUsedFiles.Save(fi)
            Config.CurrentlyOpenFiles.Save(tab.FileInfo , Tabs.AllFileInfos)            
            //updateRecentMenu fi // TODO this function checks if it is alreday Menu

        |None -> ()
        tab.CloseButton.Click.Add (fun _ -> closeTab(tab))
    
    /// checks if file is open alread 
    static member AddFile(fi:FileInfo, makeCurrent) =
        
        if fi.Exists then            
            match Tabs.AllFileInfos |> Seq.tryFindIndex (FileDialogs.areFilesOptionsSame fi)  with  // check if file is already open 
            | Some i -> 
                if makeCurrent then tabs.SelectedIndex <- i  
                // TODO  or remove it from recent list when open? add to recent list when closing?
            | None -> 
                try
                    let code = IO.File.ReadAllText fi.FullName
                    Tabs.AddTab(Tab(code,Some fi),makeCurrent)
                with  e -> 
                    Log.PrintIOErrorMsg "Error reading d:\r\n%s\r\n%A" fi.FullName e
          else
              Log.PrintIOErrorMsg "File not found:\r\n%s" fi.FullName
              MessageBox.Show("File not found:\r\n"+fi.FullName , FileDialogs.dialogCaption, MessageBoxButton.OK, MessageBoxImage.Error) |> ignore

    static member OpenFileDialog() = 
        FileDialogs.openFileDialog(Tabs.AddFile, Tabs.WorkingDirectory )

    static member SaveAs (t:Tab) =                   
        FileDialogs.saveAsDialog(t,saveAs)
    
    /// returns true if saving operation was not canceled
    static member Save(t:Tab) = 
        if t.FileInfo.IsSome && t.FileInfo.Value.Exists then
            if not t.IsCodeSaved then
                t.Editor.Save t.FileInfo.Value.FullName 
                t.IsCodeSaved <- true 
                Log.PrintInfoMsg "File saved at:\r\n%s" t.FileInfo.Value.FullName           
                true
            else
                Log.PrintInfoMsg "File already up to date:\r\n%s" t.FileInfo.Value.FullName  
                true
        else 
            if t.FileInfo.IsNone then () 
            elif not <| t.FileInfo.Value.Exists then 
                Log.PrintIOErrorMsg "File does not exist on drive anymore:\r\n%s" t.FileInfo.Value.FullName  
                MessageBox.Show("File does not exist on drive anymore:\r\n" + t.FileInfo.Value.FullName , FileDialogs.dialogCaption, MessageBoxButton.OK, MessageBoxImage.Error) |> ignore
            FileDialogs.saveAsDialog(t,saveAs)
    
    /// returns true if saving operation was not canceled
    static member SaveIncremental (t:Tab) = 
         if t.FileInfo.IsSome then            
            let fn = t.FileInfo.Value.FullName
            let last = fn.[fn.Length-5]
            if not <| Char.IsLetterOrDigit last then 
                Log.PrintIOErrorMsg "saveIncremental failed on last value: '%c' on: \r\n%s" last fn
                Tabs.Save(t)
            elif last = 'z' || last = 'Z' || last = '9' then                
                Log.PrintIOErrorMsg "saveIncremental reached last value: '%c' on: \r\n%s" last fn
                Tabs.SaveAs(t)
            else
                let newLast = char(int(last)+1)
                let npath =
                    let letters = fn.ToCharArray()
                    letters.[fn.Length-5] <- newLast
                    String.Join("", letters)
                let fi = new FileInfo(npath)
                saveAs(t,fi)                
         else
            Log.PrintIOErrorMsg "can't incremented unsaved File"  
            Tabs.SaveAs(t)

    [<CLIEvent>]
    static member OnSavedAs = savedAsEv.Publish

    /// also saves currently open files 
    static member CloseTab(t) = closeTab(t) 

    /// does not saves currently open files 
    static member TryCloseAll() = FileDialogs.askIfClosingWindowIsOk(allTabs, Tabs.Save)

    static member Initialize(startupArgs:string[]) = 
            
            let files,fiAsLowCaseStrings = Config.CurrentlyOpenFiles.GetFromLastSession()
            try
                for p in startupArgs do
                    let fi = FileInfo(p)
                    if fi.Exists then 
                        let lc = fi.FullName.ToLowerInvariant()
                        if not <| fiAsLowCaseStrings.Contains lc then //make sure to not open it twice
                            let code = File.ReadAllText fi.FullName
                            files.Add ((fi,true)) // make file from arguments current
            with e -> 
                Log.PrintAppErrorMsg "Error reading startup arguments: %A %A"  startupArgs e
            
            for fi,curr in files do
                Tabs.AddFile(fi,curr)  |> ignore 

            if files.Count=0 then //Open default file if none found in recent files or args                
                Tabs.AddTab(Tab(), true) |> ignore 
                
            if tabs.SelectedIndex = -1 then    //make one tab current  if none yet
                tabs.SelectedIndex <- 0
            
            if not <| Tabs.Current.Editor.Focus() then Log.PrintAppErrorMsg "Tabs.Current.Editor.Focus failed"
    
    