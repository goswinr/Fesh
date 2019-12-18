namespace Seff

open System
open System.Windows.Input
open System.Windows.Controls
open Seff.Util
open Seff.UtilWPF
open Seff.FileDialogs

module Menu = 
    
    /// a ref to the about menu so that other apps can append to it
    let mutable private AboutMenu : MenuItem = null 
    let addToAboutMenu (s:string) = if notNull AboutMenu then AboutMenu.Items.Add (MenuItem(Header = s) ) |> ignore<int> // add one mor item to about menu. like build infos

    module RecentFiles = 
        open CommandHelp

        let private hash = Collections.Generic.HashSet<string>() // to ensure no duplicates i recent list
    
        let mutable insertPosition = 0 // will be set in Menu setup function below

        /// to put recent files at bottom of File menu
        let updateRecentMenue (fi:IO.FileInfo) =
            let fileMenu = UI.menu.Items.[0] :?> MenuItem
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
                let openCom  = mkCmdSimple ( fun a -> openFile(fi, CreateTab.newTab,true)) // checking if file exist to grey it out would take too long
                fileMenu.Items.Insert(insertPosition, MenuItem (Header = fi.Name, ToolTip=fi.FullName, Command = openCom))            
                while fileMenu.Items.Count > Config.maxRecentFiles + insertPosition do
                    let lasti = fileMenu.Items.Count - 1
                    let rfile = (fileMenu.Items.[lasti] :?> MenuItem).ToolTip :?> string
                    hash.Remove rfile |> ignore
                    fileMenu.Items.RemoveAt lasti

    let private sep() = Separator():> Control
    
    let private fromCmd (ngc: string * string * #ICommand*string) = 
        let n,g,c,tt = ngc
        MenuItem(Header = n, InputGestureText = g, ToolTip = tt, Command = c):> Control

    /// create and hook up context and main window menu:
    let setup () = 
        FileDialogs.updateRecentMenu <- RecentFiles.updateRecentMenue
        
        // this function is called after window is layed out otherwise somehow the menu does not show. e.g.  if it is just a let value.
        updateMenu UI.menu [
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
                sep()
                fromCmd Commands.SelectLine                
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
                ]
            MenuItem(Header = "_View"),[                 
                fromCmd Commands.ToggleLogSize //TODO replace with actual tick box menu item 
                fromCmd Commands.ToggleSplit //TODO replace with actual tick box menu item 
                fromCmd Commands.ToggleLogLineWrap //TODO replace with actual tick box menu item 
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
                //MenuItem(Header = "Seff Assembly Buildtime "+Util.CompileTime.nowStrMenu)                
                ]
            ]
        RecentFiles.insertPosition <- (UI.menu.Items.[0] :?> MenuItem).Items.Count // to put recent files at bottom of file menu

        UI.tabControl.ContextMenu <- // TODO or attach to each new editor window ?
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
                
        UI.log.ContextMenu <- 
            makeContextMenu [
                fromCmd Commands.Copy
                fromCmd Commands.ToggleLogSize
                sep()
                fromCmd Commands.ClearFSI
                fromCmd Commands.CancelFSI
                fromCmd Commands.ResetFSI
                sep()
                fromCmd Commands.SaveLog
                fromCmd Commands.SaveLogSel
                ]

    
