namespace Seff

open System
open System.IO
open System.Windows
open Seff.Util


module FileDialogs =
    
    
    
    let private isTrue (nb:Nullable<bool>) = nb.HasValue && nb.Value
    
    let dialogCaption = "Seff | Scripting editor for fsharp"    

    let areFilesSame (a:FileInfo) (b:FileInfo) = a.FullName.ToLowerInvariant() = b.FullName.ToLowerInvariant()
    
    let areFilesOptionsSame (a:FileInfo ) (b:FileInfo Option) = 
        match b with 
        |Some bb -> areFilesSame bb a
        |_ -> false

    let openFileDialog (addFile: FileInfo*bool -> unit, dir:DirectoryInfo option) = 
        let dlg = new Microsoft.Win32.OpenFileDialog()
        dlg.Multiselect <- true
        match dir  with 
        | Some t when t.Exists -> dlg.InitialDirectory <- t.FullName
        | _ -> ()
        dlg.DefaultExt <- ".fsx"
        dlg.Title <- "Open file for " + dialogCaption
        dlg.Filter <- "FSharp Files(*.fsx, *.fs)|*.fsx;*.fs|Text Files(*.txt)|*.txt|All Files(*.*)|*"
        if isTrue (dlg.ShowDialog()) then
            for num,f in Seq.indexed dlg.FileNames do
                let fi = new FileInfo(f)
                if num = 0 then  addFile (fi, true) 
                else             addFile (fi, false)

    /// returns true if saving operation was not canceled
    let saveAsDialog (t:Tab,doSaveAs:Tab*FileInfo->bool) :bool= 
        let dlg = new Microsoft.Win32.SaveFileDialog()
        if t.FileInfo.IsSome && t.FileInfo.Value.Directory.Exists then dlg.InitialDirectory <- t.FileInfo.Value.DirectoryName
        if t.FileInfo.IsSome then dlg.FileName <- t.FileInfo.Value.Name        
        dlg.DefaultExt <- ".fsx"
        dlg.Title <- sprintf "Save File As for: %s" (if t.FileInfo.IsSome then  t.FileInfo.Value.FullName else t.FormatedFileName)
        dlg.Filter <- "FSharp Script Files(*.fsx)|*.fsx|Text Files(*.txt)|*.txt|All Files(*.*)|*"
        if isTrue (dlg.ShowDialog()) then                
            try t.Editor.Save dlg.FileName 
            with e -> Log.PrintIOErrorMsg "Failed to save at :\r\n%s\r\n%A" dlg.FileName e
            let fi = new FileInfo(dlg.FileName) 
            doSaveAs (t, fi)
        else
            false

    /// returns true if file is saved or if closing ok (not canceled by user)
    let askIfClosingTabIsOk(t:Tab, doSaveAs: Tab->bool) :bool=  
        if t.IsCodeSaved then true
        else 
            let msg = sprintf "Do you want to save the changes to:\r\n%s\r\nbefore closing this tab?" t.FormatedFileName
            match MessageBox.Show(msg, dialogCaption, MessageBoxButton.YesNoCancel, MessageBoxImage.Question) with
            | MessageBoxResult.Yes -> doSaveAs t
            | MessageBoxResult.No -> false
            | _ -> false

    /// returns true if all files are saved or unsaved changes are ignored (closing not canceled by user).
    let askIfClosingWindowIsOk(tabs:Tab seq, saveTab:Tab-> bool) :bool=             
        let openFs = tabs |> Seq.filter (fun t -> not t.IsCodeSaved) 
        if  Seq.isEmpty openFs then
            true
        else
            let msg = openFs  |> Seq.fold (fun m t -> 
                let name  = if t.FileInfo.IsSome then t.FileInfo.Value.Name else t.FormatedFileName
                sprintf "%s\r\n\r\n%s" m name) "Do you want to\r\nsave the changes to:" 
            match MessageBox.Show(msg, dialogCaption, MessageBoxButton.YesNoCancel, MessageBoxImage.Question) with
            | MessageBoxResult.Yes -> 
                let OKs = seq { for t in tabs do if not t.IsCodeSaved then yield saveTab t }// if saving was canceled cancel closing
                if Seq.exists ( fun OK -> OK = false) OKs then false else true // iterate unsafed files, if one file saving was canceled abort the closing of the main window                 
            | MessageBoxResult.No  -> true
            | _                    -> false 
   
   
    //Log:
    let saveLog (t:Tab) = 
        let dlg = new Microsoft.Win32.SaveFileDialog()
        if t.FileInfo.IsSome && t.FileInfo.Value.Directory.Exists then dlg.InitialDirectory <- t.FileInfo.Value.DirectoryName
        if t.FileInfo.IsSome then dlg.FileName <- t.FileInfo.Value.Name  + "_Log" 
        dlg.Title <- "SaveText from Log Window of " + dialogCaption
        dlg.DefaultExt <- ".txt"
        dlg.Filter <- "Text Files(*.txt)|*.txt|Text Files(*.csv)|*.csv|All Files(*.*)|*"
        if isTrue (dlg.ShowDialog()) then                
            try
                Log.ReadOnlyEditor.Save dlg.FileName
                Log.PrintInfoMsg "Log File saved as:\r\n%s" dlg.FileName
            with e -> 
                Log.PrintIOErrorMsg "Failed to save text from Log at :\r\n%s\r\n%A" dlg.FileName e
    
    let saveLogSelected (t:Tab) = 
        if Log.ReadOnlyEditor.SelectedText.Length > 0 then // this check is done in "canexecute command"
           let dlg = new Microsoft.Win32.SaveFileDialog()
           if t.FileInfo.IsSome && t.FileInfo.Value.Directory.Exists then dlg.InitialDirectory <- t.FileInfo.Value.DirectoryName
           if t.FileInfo.IsSome then dlg.FileName <- t.FileInfo.Value.Name  + "_Log" 
           dlg.Title <- "Save Seleceted Text from Log Window of " + dialogCaption
           dlg.DefaultExt <- ".txt"
           dlg.Filter <- "Text Files(*.txt)|*.txt|Text Files(*.csv)|*.csv|All Files(*.*)|*"
           if isTrue (dlg.ShowDialog()) then                
              try 
                   IO.File.WriteAllText(dlg.FileName, Log.ReadOnlyEditor.SelectedText) 
                   Log.PrintInfoMsg "Selected text from Log saved as:\r\n%s" dlg.FileName
              with e -> 
                   Log.PrintIOErrorMsg "Failed to save selected text from Log at :\r\n%s\r\n%A" dlg.FileName e
               
   
