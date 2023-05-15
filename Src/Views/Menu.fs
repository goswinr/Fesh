namespace Seff.Views

open System
open System.Windows
open System.Windows.Input
open System.Windows.Controls
open System.Collections.Generic

open AvalonEditB

open FsEx.Wpf.Command
open FsEx.Wpf.DependencyProps

open Seff.Model
open Seff.Util
open Seff.Config
open Seff.Views.MenuUtil


type HeaderGestureTooltip = {
    header:string
    gesture:string
    toolTip:string
    }


module RecognizePath = 
    open Seff.Editor.Selection

    let filePathStartRegex = Text.RegularExpressions.Regex("""[A-Z]:[\\/]""") // C:\    
    let filePathEndRegex = Text.RegularExpressions.Regex("""[\"<>:|?*]""") // invalid characters in file path
    // let filePathEndRegex = Text.RegularExpressions.Regex("""["()\[\]']""") //   [ or ] or ( or ) or " or '
    // Or disallow spaces too : let filePathEndRegex = Text.RegularExpressions.Regex("""["()\[\] ']""") //  a space or [ or ] or ( or ) or " or '

    let sep() = Separator():> Control

    let deDup = HashSet(2)

    let badChars = 
        //IO.Path.GetInvalidPathChars()
        [|
        '"'
        '<'
        '>'
        '?'
        '*'
        |]

    let addPathIfPresentToMenu (m:MouseButtonEventArgs, tempItemsInMenu:ref<int>, menu:ContextMenu, ava:TextEditor, openFile:IO.FileInfo*bool->bool)= 
        for i = 1 to !tempItemsInMenu do // the menu entry, maybe another entry and  the separator
            menu.Items.RemoveAt(0)
        tempItemsInMenu := 0
        deDup.Clear()

        let pos = ava.GetPositionFromPoint(m.GetPosition(ava))
        if pos.HasValue then
            //(1) add Google search
            match getSelType(ava.TextArea) with
            |NoSel
            |RectSel -> ()
            |RegSel -> 
                let pos = ava.GetPositionFromPoint(m.GetPosition(ava))
                if pos.HasValue then
                    let p = pos.Value
                    let selPos = getSelectionOrdered(ava.TextArea)
                    if selPos.stPos.Line = p.Line && selPos.enPos.Line = selPos.stPos.Line then // only if curser is at selection. And selection is just one line.
                        let txt = ava.TextArea.Selection.GetText()
                        if txt.Length > 2 then 
                            let cmd = {
                                name = sprintf "Do a Google search for '%s'" txt
                                gesture = ""
                                cmd = mkCmdSimple (fun _ -> Diagnostics.Process.Start(sprintf "https://www.google.com/search?q=%s" txt) |> ignore )
                                tip = sprintf "This Command will open your default browser and do a google search for\r\n'%s'" txt
                                }
                            menu.Items.Insert(0, Separator():> Control)
                            incr tempItemsInMenu
                            menu.Items.Insert(0, menuItem cmd)
                            incr tempItemsInMenu
            
            //(2) recognize a file path
            let line = ava.Document.GetLineByNumber(pos.Value.Line)
            let lineTxt  = ava.Document.GetText(line)
            let ss = filePathStartRegex.Matches(lineTxt)
            for s in ss do
                if s.Success then
                    let e = filePathEndRegex.Match(lineTxt,s.Index+s.Length) // add length to jump over first colon ':'
                    let fullPath = 
                        let raw = 
                            if e.Success then   lineTxt.Substring(s.Index, e.Index - s.Index)
                            else                lineTxt.Substring(s.Index)
                        raw.Split(badChars).[0].Split([|':'|])
                        |> Seq.take 2 // the first colon is allowed the later ones not
                        |> String.concat ":"

                    try
                        let dir =  IO.Path.GetDirectoryName(fullPath.Replace("\\\\", "\\").Replace("/", "\\"))
                        if not <| deDup.Contains dir then
                            deDup.Add dir  |> ignore
                            //let shortDir = Str.shrink 30 " ... " dir
                            let cmd = {
                                    name = sprintf "Open folder in Explorer  '%s' " dir // shortDir
                                    gesture = ""
                                    cmd = mkCmdSimple (fun _ ->
                                        if IO.Directory.Exists dir then  Diagnostics.Process.Start("Explorer.exe", "\"" + dir+ "\"") |> ignore
                                        else ISeffLog.log.PrintfnIOErrorMsg "Directory '%s' does not exist" dir
                                        )
                                    tip = sprintf "Try to open folder  in Explorer at \r\n%s" dir
                                    }
                            menu.Items.Insert(0, sep()       )
                            incr tempItemsInMenu
                            menu.Items.Insert(0, menuItem cmd)
                            incr tempItemsInMenu

                        if fullPath.EndsWith ".fsx" || fullPath.EndsWith ".fs" then
                            if not <| deDup.Contains fullPath then
                                deDup.Add fullPath  |> ignore
                                let name  =  IO.Path.GetFileName(fullPath)
                                let fi = IO.FileInfo(fullPath)
                                let cmd = {
                                        name = sprintf "Open file in Seff  '%s'" fullPath //name
                                        gesture = ""
                                        cmd = mkCmdSimple (fun _ -> openFile(fi,true)  |> ignore ) // does not need check if file exists !
                                        tip = sprintf "Try to open file %s from  at \r\n%s" name fullPath
                                        }

                                menu.Items.Insert(0, menuItem cmd)
                                incr tempItemsInMenu

                        //else
                        //    if not <| deDup.Contains fullPath then
                        //        deDup.Add fullPath  |> ignore
                        // always show this option:
                        let cmd = {
                                name = sprintf "Open with VS Code  '%s'" fullPath
                                gesture = ""
                                cmd = mkCmdSimple (fun _ ->
                                    try
                                        if IO.Directory.Exists fullPath || IO.File.Exists fullPath then
                                            let p = new System.Diagnostics.Process()
                                            p.StartInfo.FileName <- "code"
                                            let inQuotes = "\"" + fullPath + "\""
                                            p.StartInfo.Arguments <- String.concat " " [inQuotes;  "--reuse-window"]
                                            p.StartInfo.WindowStyle <- Diagnostics.ProcessWindowStyle.Hidden
                                            p.Start() |> ignore
                                        else
                                            ISeffLog.log.PrintfnIOErrorMsg "Directory or file '%s' does not exist" fullPath
                                    with e ->
                                        ISeffLog.log.PrintfnIOErrorMsg "Open with VS Code failed: %A" e
                                    )
                                tip = sprintf "Try to open file in VS Code:\r\n%s" fullPath
                                }

                        menu.Items.Insert(0, menuItem cmd)
                        incr tempItemsInMenu


                    with e ->
                        ISeffLog.log.PrintfnIOErrorMsg "Failed to make menu item for fullPath %s:\r\n%A" fullPath e

 #nowarn "44" //to use log.AvalonLog.AvalonEdit in addPathIfPresentToMenu

type Menu (config:Config,cmds:Commands, tabs:Tabs, statusBar:SeffStatusBar, log:Log) = 
    let bar = new Windows.Controls.Menu()

    // File menu:
    let fileMenu = MenuItem(Header = "_File")

    // TODO add all built in  Document Navigation shortcuts
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


            do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context

            //first clear
            while fileMenu.Items.Count > recentFilesInsertPosition do
                fileMenu.Items.RemoveAt recentFilesInsertPosition

            let HeaderIsIn=HashSet()

            //create time separator if not existing yet
            let tb(s) = 
                if not<| HeaderIsIn.Contains s then // so that header appears only once
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
                let lol = uf.lastOpenedUTC.ToLocalTime()

                //create time separator if not existing yet:
                if   lol.Year = thisYear && lol.DayOfYear >= today      then tb "last used today"
                elif lol.Year = thisYear && lol.DayOfYear  = today - 1  then tb "yesterday"
                else
                    let age = now - uf.lastOpenedUTC
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
                    + "\r\nlast opened: " + uf.lastOpenedUTC.ToString("yyyy-MM-dd HH:mm") + " (used for sorting in this menu)"
                    + "\r\nlast saved: " + uf.fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                let mi = MenuItem (Header = new TextBlock (Text = header), ToolTip=tt, Command = openCom) // wrap in textblock to avoid Mnemonics (alt key access at underscore)
                fileMenu.Items.Add(mi) |> ignore


        } |> Async.Start

    /// for right clicking on file paths:

    let tempItemsInEditorMenu = ref 0
    let tempItemsInLogMenu = ref 0


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
                menuItem cmds.ToggleComment
                sep()
                menuItem cmds.UnDo
                menuItem cmds.ReDo
                sep()
                menuItem cmds.Find
                menuItem cmds.Replace
                sep()
                menuItem cmds.DeleteLine
                sep()
                menuItem cmds.ToUppercase
                menuItem cmds.Tolowercase
                menuItem cmds.ToTitleCase
                menuItem cmds.TrailWhite
                sep()
                menuItem cmds.ToggleBoolean
                sep()
                menuItem cmds.AlignCode
                ]
            MenuItem(Header = "_Select"),[
                menuItem cmds.SelectLine
                sep()
                menuItem cmds.SwapLineUp
                menuItem cmds.SwapLineDown
                sep()
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
                //menuItem cmds.RunTextFromCursor
                menuItem cmds.EvalContinue
                //menuItem cmds.MarkEval
                sep()
                menuItem cmds.GoToError
                sep()
                menuItem cmds.ClearLog
                menuItem cmds.CancelFSI
                menuItem cmds.ResetFSI
                if config.RunContext.IsHosted then
                    sep()
                    menuItem cmds.ToggleSync
                sep()
                menuItem cmds.CompileScriptSDK
                menuItem cmds.CompileScriptMSB
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
                sep()
                menuItem cmds.PopOutToolTip
                ]
            MenuItem(Header = "_About"),[                
                menuItem cmds.Help
                sep()
                menuItem cmds.SettingsFolder
                menuItem cmds.AppFolder
                menuItem cmds.OpenXshdFile
                //menuItem cmds.ReloadXshdFile
                ]
            ]
        recentFilesInsertPosition <- fileMenu.Items.Count // to put recent files at bottom of file menu
        setRecentFiles() // trigger it here to to have the correct recent menu asap on startup
        config.RecentlyUsedFiles.OnRecentFilesChanged.Add(setRecentFiles) //this event will be triggered 2000 ms after new tabs are created

        // TODO or attach to each new editor window ?
        tabs.Control.ContextMenu <-
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
                menuItem cmds.ToggleComment
                sep()
                menuItem cmds.RunTextTillCursor
                //menuItem cmds.RunTextFromCursor
                menuItem cmds.EvalContinue
                //menuItem cmds.MarkEval
                sep()
                menuItem cmds.RunAllText
                menuItem cmds.RunCurrentLines
                menuItem cmds.RunSelectedText
                sep()
                menuItem cmds.UnDo
                menuItem cmds.ReDo
                //sep()
                ]

        log.AvalonLog.ContextMenu <-
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

        statusBar.FsiStatus.ContextMenu <-
            makeContextMenu [
                menuItem cmds.CancelFSI
                menuItem cmds.ResetFSI
                ]

        // add menu to open file path if there is on on current line
        tabs.Control.PreviewMouseRightButtonDown.Add ( fun m ->
            RecognizePath.addPathIfPresentToMenu (m, tempItemsInEditorMenu, tabs.Control.ContextMenu, tabs.Current.AvaEdit , tabs.AddFile)
            )


        // add menu to open file path if there is on on current line
        log.AvalonLog.PreviewMouseRightButtonDown.Add ( fun m ->
            RecognizePath.addPathIfPresentToMenu (m, tempItemsInLogMenu, log.AvalonLog.ContextMenu, log.AvalonLog.AvalonEdit , tabs.AddFile)
            )


    member this.Bar = bar

    member this.SetRecentFiles() = setRecentFiles()
