namespace Seff

open System
open System.IO
open System.Windows
open Seff.Util


module FileDialogs =   
    

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
      dlg.Title <- "Open file for " + Appearance.dialogCaption
      dlg.Filter <- "FSharp Files(*.fsx, *.fs)|*.fsx;*.fs|Text Files(*.txt)|*.txt|All Files(*.*)|*"
      if isTrue (dlg.ShowDialog()) then
          for num,f in Seq.indexed dlg.FileNames do
              let fi = new FileInfo(f)
              if num = 0 then  addFile (fi, true) 
              else             addFile (fi, false)

    /// returns true if file is saved or if closing ok (not canceled by user)
    let askIfClosingTabIsOk(t:Tab, doSaveAs: Tab->bool) :bool=  
        if t.IsCodeSaved then true
        else 
            let msg = sprintf "Do you want to save the changes to:\r\n%s\r\nbefore closing this tab?" t.FormatedFileName
            match MessageBox.Show(msg, Appearance.dialogCaption, MessageBoxButton.YesNoCancel, MessageBoxImage.Question) with
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
            match MessageBox.Show(msg, Appearance.dialogCaption, MessageBoxButton.YesNoCancel, MessageBoxImage.Question) with
            | MessageBoxResult.Yes -> 
                let OKs = seq { for t in tabs do if not t.IsCodeSaved then yield saveTab t }// if saving was canceled cancel closing
                if Seq.exists ( fun OK -> OK = false) OKs then false else true // iterate unsafed files, if one file saving was canceled abort the closing of the main window                 
            | MessageBoxResult.No  -> true
            | _                    -> false 
   
   

               
   
