﻿namespace Seff.Views

open System
open System.Windows
open System.Windows.Input
open System.Windows.Controls
open System.Collections.Generic

open Seff
open Seff.Model
open Seff.Util
open Seff.Views.Util
open Seff.Config

type HeaderGestureTooltip = {header:string; gesture:string; toolTip:string}

type Menu (config:Config,cmds:Commands, tabs:Tabs, log:Log) = 
    let bar = new Windows.Controls.Menu()
    
    // File menu: 
    let fileMenu = MenuItem(Header = "_File")                                                                        
   
    
    // TODO add all built in  DocmentNavigatin shortcuts
    let maxFilesInRecentMenu = 40

    let mutable recentFilesInsertPosition = 0
    
    let sep() = Separator():> Control    
    
    let item (ngc: string * string * #ICommand * string) = 
        let n,g,c,tt = ngc
        MenuItem(Header = n, InputGestureText = g, ToolTip = tt, Command = c):> Control
    
    
    let setRecentFiles()=
        async{            
            let usedFiles = 
                config.RecentlyUsedFiles.GetUniqueExistingSorted()     //youngest file is first       
                |> Seq.truncate maxFilesInRecentMenu
                |> Seq.toArray
            
                       
            do! Async.SwitchToContext Sync.syncContext
            
            ///first clear
            while fileMenu.Items.Count > recentFilesInsertPosition do 
                fileMenu.Items.RemoveAt recentFilesInsertPosition            
            
            let HeaderIsIn=HashSet()
            
            //create time separator if not existing yet
            let tb(s) = 
                if not<| HeaderIsIn.Contains s then // so that haeader appears only once
                    HeaderIsIn.Add s  |> ignore 
                    let tb = TextBlock (Text= "          - " + s + " -", FontWeight = FontWeights.Bold)                
                    let mi = MenuItem (Header = tb )
                    mi.Focusable <- false // to not highlight it ?                
                    fileMenu.Items.Add( mi)  |> ignore  

            // then insert all again
            let now = DateTime.Now
            let today = now.DayOfYear
            let thisYear = now.Year
            for uf in usedFiles  do       //must be ordered ,youngest file must be first             
                let lol = uf.lastOpendUtc.ToLocalTime()
                
                //create time separator if not existing yet:
                if   lol.Year = thisYear && lol.DayOfYear >= today      then tb "last used today"
                elif lol.Year = thisYear && lol.DayOfYear  = today - 1  then tb "yesterday"
                else
                    let age = now - uf.lastOpendUtc
                    if   age < TimeSpan.FromDays(7.0)  then  tb "up to a week ago"
                    elif age < TimeSpan.FromDays(31.0) then  tb "up to a month ago"
                    elif age < TimeSpan.FromDays(365.0) then tb "up to a year ago"
                    else                                     tb "older"
                
                // create menu item:
                let openCom  = mkCmdSimple ( fun a -> tabs.AddFile(uf.fileInfo, true)  |> ignore ) 
                let header = // include last two parent directories
                    let ps = General.pathParts uf.fileInfo 
                    if ps.Length < 4 then             ps |> String.concat " \\ " // full path in this case
                    else "...\\ " + (ps |> Array.rev |> Seq.truncate 3 |> Seq.rev |> String.concat " \\ " ) // partial path
                let tt = 
                    uf.fileInfo.FullName 
                    + "\r\nlast opend: " + uf.lastOpendUtc.ToString("yyyy-MM-dd HH:mm") + " (used for sorting in this menu)"
                    + "\r\nlast saved: " + uf.fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm") 
                let mi = MenuItem (Header = new TextBlock (Text = header), ToolTip=tt, Command = openCom) // wrap in textblock to avoid Mnemonics (alt key access at underscore)
                fileMenu.Items.Add(mi) |> ignore 

                
        } |> Async.Start
    
    /// for right clicking on file pathes:
    let filePathStartRegex = Text.RegularExpressions.Regex(""""[A-Z]:[\\/]""")
    let mutable tempItemsInMenu = 0


    do 
        
        updateMenu bar [// this function is called after window is layed out otherwise somehow the menu does not show. e.g.  if it is just a let value. // TODO still true ? 
            fileMenu,[
                menuItem cmds.NewTab
                menuItem cmds.OpenFile
                menuItem cmds.OpenTemplateFile
                sep()
                menuItem cmds.Save
                menuItem cmds.Export
                menuItem cmds.SaveAs
                menuItem cmds.SaveIncrementing
                menuItem cmds.SaveAll
                menuItem cmds.Close
                sep()
                menuItem cmds.SaveLog
                menuItem cmds.SaveLogSel 
                sep() ]
            MenuItem(Header = "_Edit"),[                 
                menuItem cmds.Copy     
                menuItem cmds.Cut      
                menuItem cmds.Paste    
                sep()
                menuItem cmds.Comment
                menuItem cmds.UnComment
                menuItem cmds.ToggleComment2
                sep()
                menuItem cmds.UnDo     
                menuItem cmds.ReDo     
                sep()
                menuItem cmds.Find     
                menuItem cmds.Replace 
                sep()
                menuItem cmds.SwapLineUpCtrl   
                menuItem cmds.SwapLineDownCtrl
                sep()
                menuItem cmds.ToUppercase  
                menuItem cmds.Tolowercase 
                menuItem cmds.ToTitleCase 
                sep()
                menuItem cmds.ToggleBoolean
                sep()
                menuItem cmds.AlignCode
                ]
            MenuItem(Header = "_Select"),[ 
                menuItem cmds.SelectLine 
                menuItem cmds.SwapWordLeft
                menuItem cmds.SwapWordRight  
                ]
            MenuItem(Header = "_BoxSelect", ToolTip="Create a Box Selection by \r\n holding down the Alt key while selecting \r\n or pressing the middle mouse button."),[                 
                menuItem cmds.BoxSelLeftByCharacter   
                menuItem cmds.BoxSelRightByCharacter
                sep()
                menuItem cmds.BoxSelLeftByWord        
                menuItem cmds.BoxSelRightByWord       
                sep()
                menuItem cmds.BoxSelUpByLine         
                menuItem cmds.BoxSelDownByLine        
                sep()
                menuItem cmds.BoxSelToLineStart       
                menuItem cmds.BoxSelToLineEnd 
                ]
            MenuItem(Header = "F_SI", ToolTip="FSharp Interactive code evaluation"),[ 
                menuItem cmds.RunAllText        
                menuItem cmds.RunAllTextSave
                menuItem cmds.RunAllTxSaveClear
                sep()
                menuItem cmds.RunCurrentLines  
                menuItem cmds.RunSelectedText
                sep()
                menuItem cmds.RunTextTillCursor
                menuItem cmds.RunTextFromCursor
                sep()
                menuItem cmds.ClearLog 
                menuItem cmds.CancelFSI 
                menuItem cmds.ResetFSI  
                if config.Hosting.IsHosted then
                    sep()
                    menuItem cmds.ToggleSync
                sep()
                menuItem cmds.CompileScriptR
                menuItem cmds.CompileScriptD
                ]
            MenuItem(Header = "_View"),[                 
                menuItem cmds.ToggleSplit 
                menuItem cmds.ToggleLogSize 
                menuItem cmds.ToggleLogLineWrap 
                sep()
                menuItem cmds.ClearLog
                sep()
                menuItem cmds.FontBigger
                menuItem cmds.FontSmaller
                sep()
                menuItem cmds.CollapsePrim
                menuItem cmds.CollapseCode
                menuItem cmds.ExpandCode
                ]
            MenuItem(Header = "_About"),[ //set top level refrence too
                MenuItem(Header = "_Help?")
                MenuItem(Header = "_Version 0.?.?")
                sep()
                menuItem cmds.SettingsFolder
                menuItem cmds.AppFolder
                menuItem cmds.OpenXshdFile
                menuItem cmds.ReloadXshdFile                 
                ]
            ]
        recentFilesInsertPosition <- fileMenu.Items.Count // to put recent files at bottom of file menu
        setRecentFiles() // trigger it here to to have the correct recent menu asap on startup
        config.RecentlyUsedFiles.OnRecentFilesChanged.Add(setRecentFiles ) //this event will be triggered 1000 ms after new tabs are created

        tabs.Control.ContextMenu <- // TODO or attach to each new editor window ?
            makeContextMenu [
                menuItem cmds.CollapsePrim
                menuItem cmds.CollapseCode
                menuItem cmds.ExpandCode
                sep()
                menuItem cmds.AlignCode
                sep()
                menuItem cmds.Copy 
                menuItem cmds.Cut
                menuItem cmds.Paste
                sep()
                menuItem cmds.Comment
                menuItem cmds.UnComment
                menuItem cmds.ToggleComment2
                sep()
                menuItem cmds.RunTextTillCursor
                menuItem cmds.RunTextFromCursor
                sep()
                menuItem cmds.RunAllText
                menuItem cmds.RunCurrentLines
                menuItem cmds.RunSelectedText                
                sep()
                menuItem cmds.UnDo
                menuItem cmds.ReDo
                //sep()
                ]
                
        log.ReadOnlyEditor.ContextMenu <- 
            makeContextMenu [
                menuItem cmds.ClearLog
                menuItem cmds.CancelFSI
                menuItem cmds.ResetFSI
                sep()
                menuItem cmds.ToggleLogSize
                menuItem cmds.ToggleSplit
                menuItem cmds.ToggleLogLineWrap
                sep()                
                menuItem cmds.Copy
                sep()
                menuItem cmds.SaveLog
                menuItem cmds.SaveLogSel
                ]
        
        /// add menu to open file path if there is on on current line
        tabs.Control.PreviewMouseRightButtonDown.Add ( fun m -> 
            for i = 1 to tempItemsInMenu do // the menu entry, maybe another entry and  the separator   
                tabs.Control.ContextMenu.Items.RemoveAt(0)
            tempItemsInMenu <- 0     
                
            let ava = tabs.Current.AvaEdit                      
            let pos = ava.GetPositionFromPoint(m.GetPosition(ava))
            if pos.HasValue then               
                let line = ava.Document.GetLineByNumber(pos.Value.Line)
                let txt  = ava.Document.GetText(line)
                let m = filePathStartRegex.Match(txt)
                if m.Success then
                    match Str.between "\"" "\"" txt with 
                    |None -> ()
                    |Some fullPath ->
                        let dir =  IO.Path.GetDirectoryName(fullPath.Replace("\\\\", "\\").Replace("/", "\\"))
                        let shortDir = Str.shrink 30 " ... " dir 
                        let cmd = {
                                name = sprintf "Open folder '%s' in Explorer" shortDir
                                gesture = ""
                                cmd = mkCmdSimple (fun _ -> Diagnostics.Process.Start("Explorer.exe", "\"" + dir+ "\"") |> ignoreObj) 
                                tip = sprintf "Try to open folder at \r\n%s" dir
                                }
                        tabs.Control.ContextMenu.Items.Insert(0, sep()       )  
                        tabs.Control.ContextMenu.Items.Insert(0, menuItem cmd)  
                        tempItemsInMenu <- 2
                        if fullPath.EndsWith ".fsx" || fullPath.EndsWith ".fs" then 
                            let name  =  IO.Path.GetFileName(fullPath)
                            let fi = IO.FileInfo(fullPath)
                            let cmd = {
                                    name = sprintf "Open file '%s'" name
                                    gesture = ""
                                    cmd = mkCmdSimple (fun _ -> tabs.AddFile(fi,true)  |> ignore )
                                    tip = sprintf "Try to open file %s from  at \r\n%s" name fullPath
                                    }
                            
                            tabs.Control.ContextMenu.Items.Insert(0, menuItem cmd) 
                            tempItemsInMenu <- tempItemsInMenu + 1                        
            )
               
                


    member this.Bar = bar

    member this.SetRecentFiles() = setRecentFiles()
