namespace Seff

open System
open System.Windows.Input
open System.Windows.Controls
open Seff.Util
open Seff.Util.WPF
open Seff.FileDialogs
open Seff.Config
open SyntaxHighlighting
open System.Collections.Generic



type Menu private () = 
    
    static let maxFilesInRecentMenu = 30
    
    static let bar = new Windows.Controls.Menu()

    static let fileMenu = MenuItem(Header = "_File")
    
    static let fileOpeners = Dictionary<string,MenuItem>()
    
    static let mutable recentFilesInsertPosition = 0

    static let sep() = Separator():> Control
    
    static let item (ngc: string * string * #ICommand * string) = 
        let n,g,c,tt = ngc
        MenuItem(Header = n, InputGestureText = g, ToolTip = tt, Command = c):> Control
    
    static member Bar = bar

    /// create and hook up context and main window menu:
    static member Initialize() = // this function is called after window is layed out otherwise somehow the menu does not show. e.g.  if it is just a let value.
        updateMenu bar [
            fileMenu,[
                item Commands.NewTab
                item Commands.OpenFile
                item Commands.OpenTemplateFile
                sep()
                item Commands.Save
                item Commands.SaveAs
                item Commands.SaveIncremental
                item Commands.SaveAll
                item Commands.Close
                sep()
                item Commands.SaveLog
                item Commands.SaveLogSel
                sep()
                ]
            MenuItem(Header = "_Edit"),[ 
                item Commands.Copy 
                item Commands.Cut
                item Commands.Paste
                sep()
                item Commands.Comment
                item Commands.UnComment
                sep()
                item Commands.Undo
                item Commands.Redo
                sep()
                item Commands.Find
                item Commands.Replace
                ]
            MenuItem(Header = "_Select"),[ 
                item Commands.SelectLine  
                ]
            MenuItem(Header = "_BoxSelect", ToolTip="Create a Box Selection by \r\n holding down the Alt key while selecting \r\n or pressing the middle mouse button."),[ 
                item Commands.boxSelectLeftByCharacter  
                item Commands.boxSelectRightByCharacter 
                sep()
                item Commands.boxSelectLeftByWord       
                item Commands.boxSelectRightByWord 
                sep()
                item Commands.boxSelectUpByLine         
                item Commands.boxSelectDownByLine 
                sep()
                item Commands.boxSelectToLineStart      
                item Commands.boxSelectToLineEnd 
                ]
            MenuItem(Header = "F_SI", ToolTip="FSharp Interactive code evaluation"),[ 
                item Commands.RunAllText
                item Commands.RunAllTextSave
                item Commands.RunSelectedLines
                item Commands.RunSelectedText                
                sep()
                item Commands.ClearFSI
                item Commands.CancelFSI
                item Commands.ResetFSI
                if Config.Context.IsStandalone then
                    sep()
                    item Commands.ToggleSync
                ]
            MenuItem(Header = "_View"),[                 
                item Commands.ToggleLogSize 
                item Commands.ToggleSplit 
                item Commands.ToggleLogLineWrap //TODO replace with actual tick box in status bar 
                sep()
                item Commands.ClearFSI
                sep()
                item Commands.FontBigger
                item Commands.FontSmaller
                ]
            MenuItem(Header = "_About"),[ //set top level refrence too
                item Commands.SettingsFolder
                MenuItem(Header = "_Help")
                MenuItem(Header = "_Version 0.?.?")
                item Commands.ReloadXshd         
                ]
            ]
        recentFilesInsertPosition <- fileMenu.Items.Count // to put recent files at bottom of file menu

        Tabs.Control.ContextMenu <- // TODO or attach to each new editor window ?
            makeContextMenu [
                item Commands.Copy 
                item Commands.Cut
                item Commands.Paste
                sep()
                item Commands.Comment
                item Commands.UnComment
                sep()
                item Commands.Undo
                item Commands.Redo
                sep()
                item Commands.RunAllText
                item Commands.RunSelectedLines
                item Commands.RunSelectedText
                ]
                
        Log.ReadOnlyEditor.ContextMenu <- 
            makeContextMenu [
                item Commands.Copy
                item Commands.ToggleLogSize
                item Commands.ToggleLogLineWrap
                sep()
                item Commands.ClearFSI
                item Commands.CancelFSI
                item Commands.ResetFSI
                sep()
                item Commands.SaveLog
                item Commands.SaveLogSel
                ]

        Config.RecentlyUsedFiles.OnRecentFilesChanged.Add(Menu.SetRecentFiles ) //this event will be triggered 1000 ms after new tabs are created
        Config.RecentlyUsedFiles.loadFromFile(Menu.SetRecentFiles) // trigger it here to to have the correct recent menu asap

    static member SetRecentFiles()=
        async{            
            let fis = 
                Config.RecentlyUsedFiles.Items
                |> Seq.filter ( fun fi -> fi.Exists)
                |> Seq.distinctBy( fun fi -> fi.FullName.ToLowerInvariant())
                |> Seq.truncate maxFilesInRecentMenu
                |> Seq.toArray
            do! Async.SwitchToContext Sync.syncContext
            ///first clear
            while fileMenu.Items.Count > recentFilesInsertPosition do 
                fileMenu.Items.RemoveAt recentFilesInsertPosition
            // then insert all again
            for fi in fis do                
                let lPath = fi.FullName.ToLowerInvariant()
                // reuse previosly creted MenuItems if possible
                match fileOpeners.TryGetValue(lPath) with
                |true , m -> fileMenu.Items.Add(m) |> ignore  
                |_ -> 
                    let openCom  = mkCmdSimple ( fun a -> Tabs.AddFile(fi, true)) 
                    let mi = MenuItem (Header = fi.Name, ToolTip=fi.FullName, Command = openCom)
                    fileMenu.Items.Add(mi) |> ignore 
                    fileOpeners.[lPath] <- mi
                
        } |> Async.Start

