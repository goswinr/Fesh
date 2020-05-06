namespace Seff

open System
open System.Windows.Input
open System.Windows.Controls
open Seff.Util
open Seff.Util.WPF
open Seff.FileDialogs
open Seff.Config

module Menu = 
    
    let Bar = new Menu()

    /// a ref to the about menu so that hosting apps can append to it
    let mutable private AboutMenu : MenuItem = null 
    let addToAboutMenu (s:string) = if not <| isNull AboutMenu then AboutMenu.Items.Add (MenuItem(Header = s) ) |> ignore<int> // add one mor item to about menu. like build infos

    module RecentFiles = 
        open CommandHelp
        
        //static member Add (fi) = recentFilesStack.Push fi
        (*
        static member loadRecentFilesMenu updateRecentMenu =
             //static let recentFilesReOpened = new ResizeArray<FileInfo>() // to put them first in the menue
             try
                 IO.File.ReadAllLines RecentlyUsedFiles.FilePath
                 |> Seq.iter (
                     fun f -> 
                         let fl = f.ToLowerInvariant()
                         match recentFilesReOpened |> Seq.tryFind (fun fi -> fi.FullName.ToLowerInvariant() = fl ) with 
                         |Some _ -> ()
                         |None ->
                             let fi = new FileInfo(f)
                             recentFilesStack.Push fi
                             updateRecentMenu fi
                         )
                 for fi in recentFilesReOpened |> Seq.rev do // they are already distinct
                     recentFilesStack.Push fi
                     updateRecentMenu fi
             with e -> 
                 Log.PrintAppErrorMsg "Error Loading recently used files: %s"   e.Message
          *)
        let private hash = Collections.Generic.HashSet<string>() // to ensure no duplicates in recent list
    
        let mutable insertPosition = 0 // will be set in Menu setup function below

        /// to put recent files at bottom of File menu
        let updateRecentMenue (fi:IO.FileInfo) =
            let fileMenu = Bar.Items.[0] :?> MenuItem
            let file = fi.FullName.ToLowerInvariant()
            if hash.Contains file then // just move it to top of list
                let i = 
                    fileMenu.Items
                    |> Seq.cast 
                    |> Seq.skip insertPosition // to pass separator
                    |> Seq.findIndex (fun (mi:MenuItem) -> (mi.ToolTip :?> string).ToLowerInvariant() = file)
                    |> (+) insertPosition
                let mi = fileMenu.Items.[i]
                fileMenu.Items.RemoveAt i
                fileMenu.Items.Insert(insertPosition,mi)
            else 
                hash.Add file |> ignore
                //Config.recentFilesStack.Push f // not needed here, done seperatly
                let openCom  = mkCmdSimple ( fun a -> Tabs.AddFile(fi, true)) // checking if file exist to grey it out would take too long?
                fileMenu.Items.Insert(insertPosition, MenuItem (Header = fi.Name, ToolTip=fi.FullName, Command = openCom))            
                while fileMenu.Items.Count > RecentlyUsedFiles.maxCount + insertPosition do
                    let lasti = fileMenu.Items.Count - 1
                    let rfile = (fileMenu.Items.[lasti] :?> MenuItem).ToolTip :?> string
                    hash.Remove rfile |> ignore
                    fileMenu.Items.RemoveAt lasti

    let private sep() = Separator():> Control
    
    let private fromCmd (ngc: string * string * #ICommand * string) = 
        let n,g,c,tt = ngc
        MenuItem(Header = n, InputGestureText = g, ToolTip = tt, Command = c):> Control

    /// create and hook up context and main window menu:
    let setup () = 
        //FileDialogs.updateRecentMenu <- RecentFiles.updateRecentMenue // TODO
        
        // this function is called after window is layed out otherwise somehow the menu does not show. e.g.  if it is just a let value.
        updateMenu Bar [
            MenuItem(Header = "_File"),[
                fromCmd Commands.NewTab
                fromCmd Commands.OpenFile
                fromCmd Commands.OpenTemplateFile
                sep()
                fromCmd Commands.Save
                fromCmd Commands.SaveAs
                fromCmd Commands.SaveIncremental
                fromCmd Commands.SaveAll
                fromCmd Commands.Close
                sep()
                fromCmd Commands.SaveLog
                fromCmd Commands.SaveLogSel
                sep()
                ]
            MenuItem(Header = "_Edit"),[ 
                fromCmd Commands.Copy 
                fromCmd Commands.Cut
                fromCmd Commands.Paste
                sep()
                fromCmd Commands.Comment
                fromCmd Commands.UnComment
                sep()
                fromCmd Commands.Undo
                fromCmd Commands.Redo
                sep()
                fromCmd Commands.Find
                fromCmd Commands.Replace
                ]
            MenuItem(Header = "_Select"),[ 
                fromCmd Commands.SelectLine  
                ]
            MenuItem(Header = "_BoxSelect", ToolTip="Create a Box Selection by \r\n holding down the Alt key while selecting \r\n or pressing the middle mouse button."),[ 
                fromCmd Commands.boxSelectLeftByCharacter  
                fromCmd Commands.boxSelectRightByCharacter 
                sep()
                fromCmd Commands.boxSelectLeftByWord       
                fromCmd Commands.boxSelectRightByWord 
                sep()
                fromCmd Commands.boxSelectUpByLine         
                fromCmd Commands.boxSelectDownByLine 
                sep()
                fromCmd Commands.boxSelectToLineStart      
                fromCmd Commands.boxSelectToLineEnd 
                ]
            MenuItem(Header = "F_SI", ToolTip="FSharp Interactive code evaluation"),[ 
                fromCmd Commands.RunAllText
                fromCmd Commands.RunAllTextSave
                fromCmd Commands.RunSelectedLines
                fromCmd Commands.RunSelectedText                
                sep()
                fromCmd Commands.ClearFSI
                fromCmd Commands.CancelFSI
                fromCmd Commands.ResetFSI
                if Config.Context.IsStandalone then
                    sep()
                    fromCmd Commands.ToggleSync
                ]
            MenuItem(Header = "_View"),[                 
                fromCmd Commands.ToggleLogSize 
                fromCmd Commands.ToggleSplit 
                fromCmd Commands.ToggleLogLineWrap //TODO replace with actual tick box in status bar 
                sep()
                fromCmd Commands.ClearFSI
                sep()
                fromCmd Commands.FontBigger
                fromCmd Commands.FontSmaller
                ]
            MenuItem(Header = "_About") |> (fun mi -> AboutMenu<-mi; mi),[ //set top level refrence too
                fromCmd Commands.SettingsFolder
                MenuItem(Header = "_Help")
                MenuItem(Header = "_Version 0.1.1")
                fromCmd Commands.ReloadXshd         
                ]
            ]
        RecentFiles.insertPosition <- (Bar.Items.[0] :?> MenuItem).Items.Count // to put recent files at bottom of file menu

        Tabs.Control.ContextMenu <- // TODO or attach to each new editor window ?
            makeContextMenu [
                fromCmd Commands.Copy 
                fromCmd Commands.Cut
                fromCmd Commands.Paste
                sep()
                fromCmd Commands.Comment
                fromCmd Commands.UnComment
                sep()
                fromCmd Commands.Undo
                fromCmd Commands.Redo
                sep()
                fromCmd Commands.RunAllText
                fromCmd Commands.RunSelectedLines
                fromCmd Commands.RunSelectedText
                ]
                
        Log.ReadOnlyEditor.ContextMenu <- 
            makeContextMenu [
                fromCmd Commands.Copy
                fromCmd Commands.ToggleLogSize
                fromCmd Commands.ToggleLogLineWrap
                sep()
                fromCmd Commands.ClearFSI
                fromCmd Commands.CancelFSI
                fromCmd Commands.ResetFSI
                sep()
                fromCmd Commands.SaveLog
                fromCmd Commands.SaveLogSel
                ]

    
