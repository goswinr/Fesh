namespace Seff.Views

open Seff
open System
open System.Windows.Input
open System.Windows.Controls
open Seff.Views.Util
open Seff.Config
open System.Collections.Generic
open Seff.Util
open System.Windows


type HeaderGestureTooltip = {header:string; gesture:string; toolTip:string}

type Menu (config:Config,cmds:Commands, tabs:Tabs, log:Log) = 
    let bar = new Windows.Controls.Menu()
    
    // File menu: 
    let fileMenu = MenuItem(Header = "_File")                                                                        
   
    
    // TODO add  all built in  DocmentNavigatin shortcuts
    let maxFilesInRecentMenu = 30

    let mutable recentFilesInsertPosition = 0
    let sep() = Separator():> Control    
    let item (ngc: string * string * #ICommand * string) = 
        let n,g,c,tt = ngc
        MenuItem(Header = n, InputGestureText = g, ToolTip = tt, Command = c):> Control
    
    
    let setRecentFiles()=
        async{            
            let ufs = 
                config.RecentlyUsedFiles.GetUniqueExistingSorted()               
                |> Seq.truncate maxFilesInRecentMenu
                |> Seq.toArray
            
            let savetimes = 
                ufs
                |> Array.map ( fun uf -> uf.fileInfo.LastWriteTime) // get stil async
            
            do! Async.SwitchToContext Sync.syncContext
            
            ///first clear
            while fileMenu.Items.Count > recentFilesInsertPosition do 
                fileMenu.Items.RemoveAt recentFilesInsertPosition            


            let now  = DateTime.Now.DayOfYear
            let year = DateTime.Now.Year
            let mutable today  = false
            let mutable yester = false
            let mutable week   = false
            let mutable month  = false
            let mutable older  = false

            let tb(s) = 
                let tb = TextBlock (Text= "          - " + s + " -", FontWeight = FontWeights.Bold)                
                let mi = MenuItem (Header = tb )
                fileMenu.Items.Add( mi)  |> ignore 

            // then insert all again
            for uf,savetime  in Seq.zip ufs savetimes do                
                let d = uf.date.DayOfYear
                let y = uf.date.Year
                
                if   y=year && d     = now &&              not today  then tb "last used today";       today <- true
                elif y=year && d+1   = now &&              not yester then tb "yesterday";  yester <- true
                elif y=year && d+7  >= now && d+1 < now && not week   then tb "this week" ;   week <- true
                elif y=year && d+31 >= now && d+7 < now && not week   then tb "this month";  month <- true                  
                elif  now > d+31                        && not older  then tb "older";       older <- true  
              
                let openCom  = mkCmdSimple ( fun a -> tabs.AddFile(uf.fileInfo, true)  |> ignore ) 
                let header = // include last two parent directories
                    let ps = General.pathParts uf.fileInfo 
                    if ps.Length < 4 then             ps |> String.concat " \\ " // full path in this case
                    else "...\\ " + (ps |> Array.rev |> Seq.truncate 3 |> Seq.rev |> String.concat " \\ " ) // partial path
                let tt = 
                    uf.fileInfo.FullName 
                    + "\r\nlast  used: " + uf.date.ToString("yyyy-MM-dd HH:mm")
                    + "\r\nlast saved: " + savetime.ToString("yyyy-MM-dd HH:mm")
                let mi = MenuItem (Header = new TextBlock (Text = header), ToolTip=tt, Command = openCom) // wrap in textblock to avoid Mnemonics (alt key access at underscore)
                fileMenu.Items.Add(mi) |> ignore 
              
                
        } |> Async.Start

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
                sep()
                menuItem cmds.ToggleBoolean
                ]
            MenuItem(Header = "_Select"),[ 
                menuItem cmds.SelectLine  ]
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
                menuItem cmds.BoxSelToLineEnd ]
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
                menuItem cmds.CompileScript
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
                menuItem cmds.ExpandCode
                menuItem cmds.CollapseCode
                ]
            MenuItem(Header = "_About"),[ //set top level refrence too
                MenuItem(Header = "_Help?")
                MenuItem(Header = "_Version 0.?.?")
                sep()
                menuItem cmds.SettingsFolder
                menuItem cmds.OpenXshdFile
                menuItem cmds.ReloadXshdFile                 
                ]
            ]
        recentFilesInsertPosition <- fileMenu.Items.Count // to put recent files at bottom of file menu
        setRecentFiles() // trigger it here to to have the correct recent menu asap on startup
        config.RecentlyUsedFiles.OnRecentFilesChanged.Add(setRecentFiles ) //this event will be triggered 1000 ms after new tabs are created

        tabs.Control.ContextMenu <- // TODO or attach to each new editor window ?
            makeContextMenu [
                menuItem cmds.ExpandCode
                menuItem cmds.CollapseCode
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

        

    member this.Bar = bar

    member this.SetRecentFiles()= setRecentFiles()
