namespace Seff

open System
open System.IO
open System.Windows
open Seff.Util
open Seff.Logging


module FileDialogs =
    let textForUnsavedFile =  "*unsaved*"
    let dialogCaption = "Seff | FSharp Scripting Editor"

    /// to put recent files at bottom of File menu
    let mutable updateRecentMenu = fun (fileinfo:FileInfo) -> Log.print "updateRecentMenu function not set" //will be set once menu is created
    let mutable updateHeader =     fun (tab:FsxTab) ->        Log.print "updateHeader function not set"     //will be set in CreatTab module
    

    let openFile (fi:FileInfo, newtab, makeCurrent) = // not need async?
        if fi.Exists then            
            match Tab.allTabs |> Seq.tryFindIndex ( fun t -> t.FileInfo.IsSome && sameFile t.FileInfo.Value fi) with
            | Some i -> 
                if makeCurrent then UI.tabControl.SelectedIndex <- i  // TODO  or remove it from recent list when open? add to recent list when closing?
            | None -> 
                let code = IO.File.ReadAllText fi.FullName
                let tab:FsxTab = newtab(code, Some fi ,makeCurrent) 
                Config.RecentlyUsedFiles.Add(fi) //.FullName.ToLowerInvariant()) // we dont care if it is alreday inside stack, stack is only for saving recent list 
                Config.CurrentlyOpenFiles.Save(tab.FileInfo , Tab.allTabs |> Seq.map(fun ta -> ta.FileInfo))
                Config.RecentlyUsedFiles.Save ()
                updateRecentMenu fi // this function checks if it is alreday Menu
        else
            Log.printIOErrorMsg "File not found:\r\n%s" fi.FullName
            MessageBox.Show("File not found:\r\n"+fi.FullName , dialogCaption, MessageBoxButton.OK, MessageBoxImage.Error) |> ignore
            //does this raises Application.Current.DispatcherUnhandledException: System.Windows.Threading.DispatcherUnhandledExceptionEventArgs??


    let openFileDlg (newtab) = 
        let dlg = new Microsoft.Win32.OpenFileDialog()
        dlg.Multiselect <- true
        match Tab.current with 
        | Some t -> if t.FileInfo.IsSome && t.FileInfo.Value.Directory.Exists then dlg.InitialDirectory <- t.FileInfo.Value.DirectoryName
        | None -> ()
        dlg.DefaultExt <- ".fsx"
        dlg.Title <- "Open file for " + dialogCaption
        dlg.Filter <- "FSharp Files(*.fsx, *.fs)|*.fsx;*.fs|Text Files(*.txt)|*.txt|All Files(*.*)|*"
        if isTrue (dlg.ShowDialog()) then
            for num,f in Seq.indexed dlg.FileNames do
                let fi = new FileInfo(f)
                if num = 0 then  openFile (fi, newtab, true) 
                else             openFile (fi, newtab, false)
                    
    
    let private saveAsPath (t:FsxTab,fi:FileInfo) =                   
        if not <| fi.Directory.Exists then 
            Log.printIOErrorMsg "saveAsPath: Directory does not exist:\r\n%s" fi.Directory.FullName 
            false
        else
            t.Editor.Save fi.FullName            
            if not <| fi.Exists then 
                Log.printIOErrorMsg "saveAsPath: File was not saved:\r\n%s" fi.FullName 
                false
            else
                t.FileInfo <- Some fi
                t.CodeAtLastSave <- t.Editor.Text
                ModifyUI.markTabSaved(t)  
                Config.RecentlyUsedFiles.Add (fi)
                Config.RecentlyUsedFiles.Save()
                Config.CurrentlyOpenFiles.Save(t.FileInfo , Tab.allTabs |> Seq.map(fun ta -> ta.FileInfo))
                updateHeader t
                updateRecentMenu fi            
                Log.printInfoMsg "File saved as:\r\n%s" fi.FullName // dlg.FileName
                true
    
        

    /// returns true if saving operation was not canceled
    let saveAs (t:FsxTab) = 
        let dlg = new Microsoft.Win32.SaveFileDialog()
        if t.FileInfo.IsSome && t.FileInfo.Value.Directory.Exists then dlg.InitialDirectory <- t.FileInfo.Value.DirectoryName
        if t.FileInfo.IsSome then dlg.FileName <- t.FileInfo.Value.Name        
        dlg.DefaultExt <- ".fsx"
        dlg.Title <- sprintf "Save File As for: %s" (if t.FileInfo.IsSome then  t.FileInfo.Value.FullName else textForUnsavedFile)
        dlg.Filter <- "FSharp Script Files(*.fsx)|*.fsx|Text Files(*.txt)|*.txt|All Files(*.*)|*"
        if isTrue (dlg.ShowDialog()) then                
            t.Editor.Save dlg.FileName 
            let fi = new FileInfo(dlg.FileName)   
            saveAsPath(t,fi)
        else
            false

    /// returns true if saving operation was not canceled
    let saveIncremental (t:FsxTab) = 
         if t.FileInfo.IsSome then            
            let fn = t.FileInfo.Value.FullName
            let last = fn.[fn.Length-5]
            if not <| Char.IsLetterOrDigit last then 
                Log.printIOErrorMsg "saveIncremental failed on last value: '%c' on: \r\n%s" last fn
                saveAs t
            elif last = 'z' || last = 'Z' || last = '9' then                
                Log.printIOErrorMsg "saveIncremental reached last value: '%c' on: \r\n%s" last fn
                saveAs t
            else
                let newLast = char(int(last)+1)
                let npath =
                    let letters = fn.ToCharArray()
                    letters.[fn.Length-5] <- newLast
                    String.Join("", letters)
                let fi = new FileInfo(npath)
                saveAsPath(t,fi)                
         else
            Log.printIOErrorMsg "cant incremented unsaved File"  
            saveAs t
     

    /// returns true if saving operation was not canceled
    let save(t:FsxTab) = 
        if t.FileInfo.IsSome && t.FileInfo.Value.Exists then
            if t.CodeAtLastSave <> t.Editor.Text then
                t.CodeAtLastSave <- t.Editor.Text //TODO add trimming of trailling white space: dropTrailingWhiteSpace in FsInteractiveService
                t.Editor.Save t.FileInfo.Value.FullName 
                ModifyUI.markTabSaved(t)
                Log.printInfoMsg "File saved at:\r\n%s" t.FileInfo.Value.FullName           
                true
            else
                Log.printInfoMsg "File already up to date:\r\n%s" t.FileInfo.Value.FullName  
                true
        else 
            if t.FileInfo.IsNone then Log.printIOErrorMsg "FileInfo.IsNone, File never saved before?"
            elif not <| t.FileInfo.Value.Exists then Log.printIOErrorMsg "File does not exist on drive:\r\n%s" t.FileInfo.Value.FullName  
            saveAs t
    
    /// returns true if closing operation was successful (not canceled by user)
    let closeTab(t:FsxTab) =  
        let cls() =            
            UI.tabControl.Items.Remove(t)
            if UI.tabControl.Items.Count = 0 then 
                Tab.current <- None
            Config.CurrentlyOpenFiles.Save (t.FileInfo , Tab.allTabs |> Seq.map(fun ta -> ta.FileInfo))
            true        
        if t.CodeAtLastSave = t.Editor.Text then cls()
        else 
            match MessageBox.Show("Do you want to \r\nsave the changes \r\nbefore closing this tab?", dialogCaption, MessageBoxButton.YesNoCancel, MessageBoxImage.Question) with
            | MessageBoxResult.Yes -> if saveAs t then cls() else false
            | MessageBoxResult.No -> cls()
            | _ -> false
    
    /// returns true if all files are saved or unsaved changeas ignored (closing not canceled by user).
    let askIfClosingWindowIsOk() =             
        let openFs = Tab.allTabs |> Seq.filter (fun t -> t.CodeAtLastSave <> t.Editor.Text) 
        if  Seq.isEmpty openFs then
            true
        else
            let msg = openFs  |> Seq.fold (fun m t -> 
                let name  = if t.FileInfo.IsSome then t.FileInfo.Value.Name else textForUnsavedFile
                sprintf "%s\r\n\r\n%s" m name) "Do you want to \r\nsave the changes to:" 
            match MessageBox.Show(msg, dialogCaption, MessageBoxButton.YesNoCancel, MessageBoxImage.Question) with
            | MessageBoxResult.Yes -> 
                let OKs = seq { for t in Tab.allTabs do if t.CodeAtLastSave <> t.Editor.Text then yield save t }// if saving was canceled cancel closing
                if Seq.exists ( fun OK -> OK = false) OKs then false else true // iterate unsafed files, if one file saving was canceled abort the closing of the main window                 
            | MessageBoxResult.No  -> true
            | _                    -> false 
            // no need to save the files that were on closing open.
    
    let altF4close () =
        match Tab.current with
        |Some t -> closeTab(t:FsxTab)
        |None -> askIfClosingWindowIsOk()
        |>ignore

    //Log:
    let saveLog (t:FsxTab) = 
        let dlg = new Microsoft.Win32.SaveFileDialog()
        if t.FileInfo.IsSome && t.FileInfo.Value.Directory.Exists then dlg.InitialDirectory <- t.FileInfo.Value.DirectoryName
        if t.FileInfo.IsSome then dlg.FileName <- t.FileInfo.Value.Name  + "_Log" 
        dlg.Title <- "SaveText from Log Window of " + dialogCaption
        dlg.DefaultExt <- ".txt"
        dlg.Filter <- "Text Files(*.txt)|*.txt|Text Files(*.csv)|*.csv|All Files(*.*)|*"
        if isTrue (dlg.ShowDialog()) then                
            UI.log.Save dlg.FileName
            Log.printInfoMsg "Log File saved as:\r\n%s" dlg.FileName
    
    let saveLogSelected (t:FsxTab) = 
        if UI.log.SelectedText.Length > 0 then // this check is done in "canexecute command"
            let dlg = new Microsoft.Win32.SaveFileDialog()
            if t.FileInfo.IsSome && t.FileInfo.Value.Directory.Exists then dlg.InitialDirectory <- t.FileInfo.Value.DirectoryName
            if t.FileInfo.IsSome then dlg.FileName <- t.FileInfo.Value.Name  + "_Log" 
            dlg.Title <- "Save Seleceted Text from Log Window of " + dialogCaption
            dlg.DefaultExt <- ".txt"
            dlg.Filter <- "Text Files(*.txt)|*.txt|Text Files(*.csv)|*.csv|All Files(*.*)|*"
            if isTrue (dlg.ShowDialog()) then                
                IO.File.WriteAllText(dlg.FileName, UI.log.SelectedText) 
                Log.printInfoMsg "Selected text from Log saved as:\r\n%s" dlg.FileName
    
