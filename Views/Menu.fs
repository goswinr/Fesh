namespace Seff.Views

open System
open System.Windows.Input
open System.Windows.Controls
open Seff.Views.Util
open Seff.Config
open System.Collections.Generic
open SyntaxHighlighting

type HeaderGestureTooltip = {header:string; gesture:string; toolTip:string}

type Menu (config:Config) = 
    let bar = new Windows.Controls.Menu()
    
    // File menu: 
    let fileMenu         =MenuItem(Header = "_File")                                                                        
    let newTab           ={header= "New File"                  ;gesture= "Ctrl + N"        ;toolTip= "Create a new script file"}
    let openFile         ={header= "Open File"                 ;gesture= "Ctrl + O"        ;toolTip= "Open a script file"}
    let openTemplateFile ={header= "Edit Template File"        ;gesture= ""                ;toolTip= "Opens the template file that is used when creating a New File ( Ctrl + N)"}
    let save             ={header= "Save"                      ;gesture= "Ctrl + S"        ;toolTip= "Saves the file. Shows a dialog only if the open file does not exist anymore"}
    let saveAs           ={header= "Save As"                   ;gesture= "Ctrl + Alt + S"  ;toolTip= "Shows a dialog to save the file at a new path or name."}
    let saveIncrementing ={header= "Save Incrementing"         ;gesture= ""                ;toolTip= "Save with increased last character of filename, can be alphabetic or numeric ( e.g.  d->e or 5->6), does not overwrite any existing file"}
    let saveAll          ={header= "Save All"                  ;gesture= "Ctrl + Shift + S";toolTip= "Saves all tabs. Shows a dialog only if the open file does not exist on disk"}
    let close            ={header= "Close File"                ;gesture= "Ctrl + F4"       ;toolTip= "Closes the current tab, if ther is only one tab then the window will be closed"}
    let saveLog          ={header= "Save Text in Log"          ;gesture= ""                ;toolTip= "Save all text from Log Window"}
    let saveLogSel       ={header= "Save Selected Text in Log" ;gesture= ""                ;toolTip= "Save selected text from Log Window"}
                                                              
    //Edit menu:                                              
    let comment          ={header= "Comment"                   ;gesture= "Ctrl + K"        ;toolTip= "Removes '//' at the beginning of current line, \r\nor from all line touched by current selection"}
    let unComment        ={header= "Uncomment"                 ;gesture= "Ctrl + U"        ;toolTip= "Puts '//' at the beginning of current line, \r\nor all line touched by current selection"}                                                                                     
                                                               
    //Select menu:                                             
    let selectLine       ={header= "Select Current Line"       ;gesture= "Ctrl  + L"       ;toolTip= "Select current line"} // TODO compare VSCODE shortcuts to  see https://github.com/icsharpcode/SharpDevelop/wiki/Keyboard-Shortcuts
    //let selectLinesUp    ={header= "Select Lines Upwards"      ;gesture= "Shift + Up"      ;toolTip= "Not implemented yet"}
    //let selectLinesDown  ={header= "Select Lines Downwards"    ;gesture= "Shift + Down"    ;toolTip= "Not implemented yet"}//TODO!   
                                                               
    //FSI menu:                                                
    let runAllText       ={header= "Run All Text"              ;gesture= "F5"              ;toolTip= "Send all text in the current file to FSharp Interactive"}
    let runAllTextSave   ={header= "Save and Run All Text"     ;gesture= "F6"              ;toolTip= "First Save current File, then send all it's text to FSharp Interactive"}
    let runSelectedLines ={header= "Run Selected Lines"        ;gesture= "Ctrl + Enter"    ;toolTip= "Sends the currently seleceted Lines in the editor to FSharp Interactive.\r\nIncludes partially selected lines in full."}
    let runSelectedText  ={header= "Run Selected Text"         ;gesture= "Alt + Enter"     ;toolTip= "Sends the currently seleceted Text in the editor to FSharp Interactive"}// TODO mark evaluated code with grey background
    let clearFSI         ={header= "Clear Log"                 ;gesture= "Ctrl + Alt + C"  ;toolTip= "Clear all text from FSI Log window"}
    let cancelFSI        ={header= "Cancel FSI"                ;gesture= "Ctrl + Break"    ;toolTip= "Cancel running FSI evaluation (only available in asynchronous mode)"}
    let resetFSI         ={header= "Reset FSI"                 ;gesture= "Ctrl + Alt + R"  ;toolTip= "Reset FSharp Interactive"}
    let toggleSync       ={header= "Toggle Sync / Async"       ;gesture= ""                ;toolTip= "Switch between synchronous and asynchronous evaluation in FSI, see status in StatusBar"} 
                                                               
   //View menu:                                                
    let toggleSplit      ={header= "Toggle Window Split"       ;gesture= "Ctrl + T"        ;toolTip= "Toggle between vertical and horizontal Screen Split of Editor and Log"}
    let toggleLogSize    ={header= "Toggle Log Maximased"      ;gesture= "Ctrl + M"        ;toolTip= "Maximises or resets the size of the Log window. \r\n(depending on curren state)"}
    let toggleLogLineWrap={header= "Toggle Line Wraping in Log";gesture= "Alt + Z"         ;toolTip= "Toggle Line Wraping in Log window"}  
    let fontBigger       ={header= "Make Font Bigger"          ;gesture= "Ctrl + '+'"      ;toolTip= "Increase Text Size for both Editor and Log"}
    let fontSmaller      ={header= "Make Font Smaller"         ;gesture= "Ctrl + '-'"      ;toolTip= "Decrease Text Size for both Editor and Log"}
                                                               
    //Settings menu                                            
    let settingsFolder   ={header= "Open Settings Folder"      ;gesture= ""                ;toolTip= "Opens the Folder where user settinsg such as default file content is saved."}
    let reloadXshdFile   ={header= "Reload Xshd File"          ;gesture= "F11"             ;toolTip= "Reloads FSharpSynatxHighlighterExtended.xshd, this is useful for testing new highlighting files without a restart."}
    
    
    //--------------------------
    // Built in Commands from Avalonedit (listed as functiosn so the can be created more than once( eg for menu, and context menu)
    //----------------------------
    let copy             () = MenuItem(Header= "Copy"                        ,InputGesture=  "Ctrl + C"          , ToolTip= "Copy selected text, or full current line if nothing is selceted."                  ,Command= ApplicationCommands.Copy)
    let cut              () = MenuItem(Header= "Cut"                         ,InputGesture=  "Ctrl + X"          , ToolTip= "Cut selected text, or full current line if nothing is selceted."                   ,Command= ApplicationCommands.Cut)                       
    let paste            () = MenuItem(Header= "Paste"                       ,InputGesture=  "Ctrl + V"          , ToolTip= "Insert text from Clipboard"                                                        ,Command= ApplicationCommands.Paste)                     
    let unDo             () = MenuItem(Header= "UnDo"                        ,InputGesture=  "Ctrl + Z"          , ToolTip= "Undo last edit"                                                                    ,Command= ApplicationCommands.Undo)                      
    let reDo             () = MenuItem(Header= "ReDo"                        ,InputGesture=  "Ctrl + Y"          , ToolTip= "Undo last undo"                                                                    ,Command= ApplicationCommands.Redo)                      
    let find             () = MenuItem(Header= "Find"                        ,InputGesture=  "Ctrl + F"          , ToolTip= "Find text of current selection"                                                    ,Command= ApplicationCommands.Find)                      
    let replace          () = MenuItem(Header= "Replace"                     ,InputGesture=  "Ctrl + H"          , ToolTip= "Find and replace text of current selection"                                        ,Command= ApplicationCommands.Replace )   
    
    
    
    
    
    // TODO add  all built in  DocmentNavigatin shortcuts
    let maxFilesInRecentMenu = 30
    let fileOpeners = Dictionary<string,MenuItem>()    
    let mutable recentFilesInsertPosition = 0
    let s_______________________ep() = Separator():> Control    
    let item (ngc: string * string * #ICommand * string) = 
        let n,g,c,tt = ngc
        MenuItem(Header = n, InputGestureText = g, ToolTip = tt, Command = c):> Control
    
    do  
        updateMenu bar [// this function is called after window is layed out otherwise somehow the menu does not show. e.g.  if it is just a let value. // TODO still true ? 
            fileMenu,[
                newTab()
                openFile()
                openTemplateFile()
                s_______________________ep()
                save()
                saveAs()
                saveIncremental()
                saveAll()
                close()
                s_______________________ep()
                saveLog()
                saveLogSel() ]
            MenuItem(Header = "_Edit"),[                 
                copy ()    
                cut()      
                paste()    
                s_______________________ep()
                comment()
                unComment()
                s_______________________ep()
                unDo()     
                reDo()     
                s_______________________ep()
                find()     
                replace() ]
            MenuItem(Header = "_Select"),[ 
                selectLine()  ]
            MenuItem(Header = "_BoxSelect", ToolTip="Create a Box Selection by \r\n holding down the Alt key while selecting \r\n or pressing the middle mouse button."),[                 
                MenuItem(Header= "Box Select Left By Character"  ,InputGesture=  "Alt + Shift + Left"       , ToolTip= "Expands the selection left by one character, creating a rectangular selection."    ,Command= RectangleSelection.BoxSelectLeftByCharacter )  
                MenuItem(Header= "Box Select Right By Character" ,InputGesture= "Alt + Shift + Right"       , ToolTip= "Expands the selection right by one character, creating a rectangular selection."   ,Command= RectangleSelection.BoxSelectRightByCharacter)  
                s_______________________ep()
                MenuItem(Header= "Box Select Left By Word"       ,InputGesture= "Ctrl + Alt + Shift + Left" , ToolTip= "Expands the selection left by one word, creating a rectangular selection."         ,Command= RectangleSelection.BoxSelectLeftByWord)        
                MenuItem(Header= "Box Select Right By Word"      ,InputGesture= "Ctrl + Alt + Shift + Right", ToolTip= "Expands the selection right by one word, creating a rectangular selection."        ,Command= RectangleSelection.BoxSelectRightByWord)       
                s_______________________ep()
                MenuItem(Header= "Box Select Up By Line"         ,InputGesture= "Alt + Shift + Up"          , ToolTip= "Expands the selection up by one line, creating a rectangular selection."           ,Command= RectangleSelection.BoxSelectUpByLine)          
                MenuItem(Header= "Box Select Down By Line"       ,InputGesture= "Alt + Shift + Down"        , ToolTip= "Expands the selection down by one line, creating a rectangular selection."         ,Command= RectangleSelection.BoxSelectDownByLine)        
                s_______________________ep()
                MenuItem(Header= "Box Select To Line Start"      ,InputGesture= "Alt + Shift + Home"        , ToolTip= "Expands the selection to the start of the line, creating a rectangular selection." ,Command= RectangleSelection.BoxSelectToLineStart)       
                MenuItem(Header= "Box Select To Line End"        ,InputGesture= "Alt + Shift + End"         , ToolTip= "Expands the selection to the end of the line, creating a rectangular selection."   ,Command= RectangleSelection.BoxSelectToLineEnd)  ]
            MenuItem(Header = "F_SI", ToolTip="FSharp Interactive code evaluation"),[ 
                runAllText        ()
                runAllTextSave    ()
                runSelectedLines  ()
                runSelectedText   ()             
                s_______________________ep()
                clearFSI  ()
                cancelFSI ()
                resetFSI  ()
                if config.Context.IsStandalone then
                    s_______________________ep()
                    toggleSync()
                ]
            MenuItem(Header = "_View"),[                 
                toggleSplit ()
                toggleLogSize ()
                toggleLogLineWrap ()
                s_______________________ep()
                clearFSI()
                s_______________________ep()
                fontBigger()
                fontSmaller()
                ]
            MenuItem(Header = "_About"),[ //set top level refrence too
                settingsFolder()
                MenuItem(Header = "_Help?")
                MenuItem(Header = "_Version 0.?.?")
                reloadXshd ()       
                ]
            ]
        recentFilesInsertPosition <- fileMenu.Items.Count // to put recent files at bottom of file menu

        Tabs.Control.ContextMenu <- // TODO or attach to each new editor window ?
            makeContextMenu [
                Copy 
                Cut
                Paste
                s_______________________ep()
                Comment
                UnComment
                s_______________________ep()
                Undo
                Redo
                s_______________________ep()
                RunAllText
                RunSelectedLines
                RunSelectedText
                ]
                
        log.ReadOnlyEditor.ContextMenu <- 
            makeContextMenu [
                Copy
                ToggleLogSize
                ToggleLogLineWrap
                s_______________________ep()
                ClearFSI
                CancelFSI
                ResetFSI
                s_______________________ep()
                SaveLog
                SaveLogSel
                ]

        config.RecentlyUsedFiles.OnRecentFilesChanged.Add(this.SetRecentFiles ) //this event will be triggered 1000 ms after new tabs are created
        config.RecentlyUsedFiles.LoadFromFile(this.SetRecentFiles) // trigger it here to to have the correct recent menu asap on startup

    member this.Bar = bar

    member SetRecentFiles()=
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

