namespace Seff

open System
open System.Windows.Input    
open Seff.Config
open Seff.Util.WPF
open ICSharpCode.AvalonEdit.Editing
open Seff.Model

type Commands private () = 

    //see https://github.com/icsharpcode/AvalonEdit/blob/697ff0d38c95c9e5a536fbc05ae2307ec9ef2a63/ICSharpCode.AvalonEdit/Editing/CaretNavigationCommandHandler.cs#L73
    //TODO these gets evaluated for each command on every mouse click or key perss . is this OK?  any lag ?? in Canexecute for commands
    
    static let isEditorSel a   = Tabs.Current.Editor.SelectionLength > 0
    static let isLogSel    a   = log.ReadOnlyEditor.SelectionLength > 0
    static let runsAsync   a   = Fsi.State = Evaluating && Fsi.Mode = Async
             
    static member val RunSelectedText  = "Run Selected Text"        , "Alt + Enter"   , mkCmd isEditorSel (fun a -> Fsi.Evaluate Tabs.Current.Editor.SelectedText),"Sends the currently seleceted Text in the editor to FSharp Interactive"// TODO mark evaluated code with grey background
    static member val RunSelectedLines = "Run Selected Lines"       , "Ctrl + Enter"  , mkCmdSimple       (fun a -> Fsi.Evaluate <| ModifyUI.expandSelectionToFullLines Tabs.Current),"Sends the currently seleceted Lines in the editor to FSharp Interactive.\r\nIncludes partially selected lines in full."
    static member val RunAllText       = "Run All Text"             , "F5"            , mkCmdSimple       (fun a -> Fsi.Evaluate  Tabs.Current.Editor.Text) ,"Send all text in the current file to FSharp Interactive"
    static member val RunAllTextSave   = "Save and Run All Text"    , "F6"            , mkCmdSimple       (fun a -> if Tabs.Save Tabs.Current then Fsi.Evaluate  Tabs.Current.Editor.Text) ,"First Save current File, then send all it's text to FSharp Interactive"
                                                       
    static member val ResetFSI         = "Reset FSI"                , "Ctrl + Alt + R", mkCmdSimple     (fun a -> Fsi.Reset()          ),"Reset FSharp Interactive"
    static member val CancelFSI        = "Cancel FSI"               , "Ctrl + Break"  , mkCmd runsAsync (fun a -> Fsi.CancelIfAsync()  ),"Cancel running FSI evaluation (only available in asynchronous mode) "
    static member val ClearFSI         = "Clear Log"                , "Ctrl + Alt + C", mkCmdSimple     (fun a -> log.ReadOnlyEditor.Clear()   ),"Clear all text from FSI Log window"
    static member val ToggleSync       = "Toggle Sync / Async"      , ""              , mkCmdSimple     (fun a -> Fsi.ToggleSync()     ),"Switch between synchronous and asynchronous evaluation in FSI, see status in StatusBar"
                                                       
    static member val NewTab           = "New File"                 , "Ctrl + N"      , mkCmdSimple (fun a -> Tabs.AddTab(new Tab(),true)),"Create a new script file"
    static member val OpenFile         = "Open File"                , "Ctrl + O"      , mkCmdSimple (fun a -> Tabs.OpenFile()),"Open a script file"
    static member val OpenTemplateFile = "Edit Template File"       ,""               , mkCmdSimple (fun a -> Tabs.AddFile(IO.FileInfo(DefaultCode.FilePath),true)|>ignore),"Opens the template file that is used when creating a New File ( Ctrl + N)"
    static member val Close            = "Close File"               , "Ctrl + F4"     , mkCmdSimple (fun a -> Tabs.CloseTab(Tabs.Current)),"Closes the current Tab, if no tab present Application will be closed" 
   
    static member val SaveIncremental  = "Save Incremental"         , ""              , mkCmdSimple    (fun a -> Tabs.SaveIncremental Tabs.Current |> ignore),"increases the last static memberter of filename, can be alphabetic or numeric "
    static member val SaveAs           = "Save As"                  , "Ctrl + Alt + S", mkCmdSimple    (fun a -> Tabs.SaveAs          Tabs.Current |> ignore),"Shows a dialog to save the file at a new path or name."
    static member val Save             = "Save"                     , "Ctrl + S"      , mkCmdSimple    (fun a -> Tabs.Save            Tabs.Current |> ignore),"Saves the file. Shows a dialog only if the open file does not exist anymore"
    static member val SaveAll          = "Save All"               , "Ctrl + Shift + S", mkCmdSimple    (fun a -> Seq.iter (Tabs.Save >> ignore) Tabs.AllTabs),"Saves all tabs. Shows a dialog only if the open file does not exist on disk"
    static member val SaveLog          = "Save Text in Log"         , ""              , mkCmdSimple    (fun a -> FileDialogs.saveLog            Tabs.Current |> ignore),"Save all text from Log Window"
    static member val SaveLogSel       = "Save Selected Text in Log", ""              , mkCmd isLogSel (fun a -> FileDialogs.saveLogSelected Tabs.Current |> ignore),"Save selected text from Log Window"
                                                       
    static member val FontBigger       = "Make Font Bigger"         , "Ctrl + '+'"    , mkCmdSimple (fun a -> ModifyUI.fontBigger ()) ,"Increase Text Size for both Editor and Log"
    static member val FontSmaller      = "Make Font Smaller"        , "Ctrl + '-'"    , mkCmdSimple (fun a -> ModifyUI.fontSmaller()) ,"Decrease Text Size for both Editor and Log"
    static member val Comment          = "Comment"                  , "Ctrl + K"      , mkCmdSimple (fun a -> Commenting.comment Tabs.Current) ,"Removes '//' at the beginning of current line, \r\nor from all line touched by current selection"
    static member val UnComment        = "UnComment"                , "Ctrl + U"      , mkCmdSimple (fun a -> Commenting.unComment Tabs.Current),"Puts '//' at the beginning of current line, \r\nor all line touched by current selection"
   // todo refine 
    static member val ToggleLogSize    = "Toggle Log Maximased"       , "Ctrl + M"   , mkCmdSimple (fun a ->  TabsAndLog.ToggleMaxLog()) ,"Maximises or resets the size of the Log window. \r\n(depending on curren state)"
    static member val ToggleSplit      = "Toggle Screen Split"        , "Ctrl + T"   , mkCmdSimple (fun a ->  TabsAndLog.ToggleSplit()), "Toggle between vertical and horizontal Screen Split of Editor and Log"
    static member val ToggleLogLineWrap= "Toggle Line Wraping in Log" , "Alt + Z"    , mkCmdSimple (fun a ->  ModifyUI.toggleLogLineWrap()), "Toggle Line Wraping in Log window"
   
   // TODO compare VSCODE shortcuts to  see https://github.com/icsharpcode/SharpDevelop/wiki/Keyboard-Shortcuts
    static member val SelectLine      = "Select Current Line"       , "Ctrl  + L"     , mkCmdSimple (fun a -> ModifyUI.expandSelectionToFullLines Tabs.Current|> ignore),"Select current line"
    static member val SelectLinesUp   = "Select Lines Upwards"      , "Shift + Up"    , mkCmdSimple (fun a -> ModifyUI.expandSelectionToFullLines Tabs.Current|> ignore),"Not implemented yet"
    static member val SelectLinesDown = "Select Lines Downwards"    , "Shift + Down"  , mkCmdSimple (fun a -> ModifyUI.expandSelectionToFullLines Tabs.Current|> ignore),"Not implemented yet"
   
    static member val SettingsFolder = "Open Settings Folder"       , ""              , mkCmdSimple (fun a -> Config.openConfigFolder()), "Opens the Folder where user settinsg such as default file content is saved."
   
    static member val ReloadXshd =     "Reload Xshd file"           , "F11"           , mkCmdSimple (fun a -> SyntaxHighlighting.setFSharp(Tabs.Current.Editor,true)), "for Testing only: Reloads the Syntax highlighting file FSharpSynatxHighlighterExtended.xshd"
   
   // built in Commands from Avalonedit 
   
    static member val Copy             = "Copy"                     , "Ctrl + C"      , ApplicationCommands.Copy, "Copy selected text, or full current line if nothing is selceted."
    static member val Cut              = "Cut"                      , "Ctrl + X"      , ApplicationCommands.Cut  ,"Cut selected text, or full current line if nothing is selceted."
    static member val Paste            = "Paste"                    , "Ctrl + V"      , ApplicationCommands.Paste,"Insert text from Clipboard"
    static member val Undo             = "UnDo"                     , "Ctrl + Z"      , ApplicationCommands.Undo,"Undo last edit"
    static member val Redo             = "ReDo"                     , "Ctrl + Y"      , ApplicationCommands.Redo,"Undo last undo"
    static member val Find             = "Find"                     , "Ctrl + F"      , ApplicationCommands.Find,"Fint text of current selection"
    static member val Replace          = "Replace"                  , "Ctrl + H"      , ApplicationCommands.Replace ,"Not implemented yet"//TODO implement
   
   // TODO add  all built in  DocmentNavigatin shortcuts
   
    static member val boxSelectLeftByCharacter  = "Box Select Left By Character" , "Alt + Shift + Left" , RectangleSelection.BoxSelectLeftByCharacter ,   "Expands the selection left by one character, creating a rectangular selection."
    static member val boxSelectRightByCharacter = "Box Select Right By Character" ,"Alt + Shift + Right", RectangleSelection.BoxSelectRightByCharacter,   "Expands the selection right by one character, creating a rectangular selection."
    static member val boxSelectLeftByWord       = "Box Select Left By Word"       ,"Ctrl + Alt + Shift + Left", RectangleSelection.BoxSelectLeftByWord,   "Expands the selection left by one word, creating a rectangular selection."
    static member val boxSelectRightByWord      = "Box Select Right By Word"      ,"Ctrl + Alt + Shift + Right",RectangleSelection.BoxSelectRightByWord,  "Expands the selection right by one word, creating a rectangular selection."
    static member val boxSelectUpByLine         = "Box Select Up By Line"         ,"Alt + Shift + Up",    RectangleSelection.BoxSelectUpByLine,           "Expands the selection up by one line, creating a rectangular selection."
    static member val boxSelectDownByLine       = "Box Select Down By Line"       ,"Alt + Shift + Down",  RectangleSelection.BoxSelectDownByLine,         "Expands the selection down by one line, creating a rectangular selection."
    static member val boxSelectToLineStart      = "Box Select To Line Start"      ,"Alt + Shift + Home",  RectangleSelection.BoxSelectToLineStart,        "Expands the selection to the start of the line, creating a rectangular selection."
    static member val boxSelectToLineEnd        = "Box Select To Line End"        ,"Alt + Shift + End",   RectangleSelection.BoxSelectToLineEnd,          "Expands the selection to the end of the line, creating a rectangular selection."
   
    
    /// exluding the ones already provided by avalonedit
    static member allShortCutKeyGestures () = 
        
        let allCustomCommands = [  //for seting up Key gestures below
             Commands.RunSelectedText  
             Commands.RunSelectedLines 
             Commands.RunAllText
             Commands.RunAllTextSave
             
             Commands.ResetFSI         
             Commands.CancelFSI        
             Commands.ClearFSI
             if config.AppDataLocation.IsStandalone then Commands.ToggleSync
             
             Commands.NewTab           
             Commands.OpenFile 
             Commands.Close
             
             Commands.SaveIncremental
             Commands.SaveAs           
             Commands.Save
             Commands.SaveAll
             Commands.SaveLog          
             Commands.SaveLogSel       
             
             Commands.FontBigger       
             Commands.FontSmaller      
             Commands.Comment          
             Commands.UnComment
             Commands.ToggleLogSize
             Commands.ToggleSplit
             Commands.ToggleLogLineWrap
             
             Commands.SelectLine
             Commands.SelectLinesUp  
             Commands.SelectLinesDown
             
             Commands.ReloadXshd
             ] 


        // these functions parse the KeyGesture from a string defined above. 
        // eg. "Ctrl + V" becomes KeyGesture(Key.V , ModifierKeys.Control)
        // this is to make sure the displayed gesture matches the actual gesture
        let getKey = function
            |"'-'"   -> Key.Subtract //|"'+'"   -> Key.Add // fails on gg.Split('+') 
            |"Break" -> Key.Cancel
            |"Up"    -> Key.Up
            |"Down"  -> Key.Down
            | x -> match Key.TryParse(x,true) with
                   |true, k -> k
                   | _ -> log.PrintAppErrorMsg "*AllInputBindings: failed to parse Command Key '%A'" x; Key.NoName
        
        let getModKey = function
            |"Ctrl"     -> ModifierKeys.Control        
            | x -> match ModifierKeys.TryParse(x,true) with
                   |true, k -> k
                   | _ -> log.PrintAppErrorMsg "*AllInputBindings: failed to parse ModifierKey '%A'" x; ModifierKeys.None

        try
            [|  for _,g,cmd,_ in allCustomCommands do
                    match g.Trim() with
                    |"" -> ()
                    | "Ctrl + '+'" | "Ctrl + +" | "Ctrl +" -> // because  gg.Split('+') would fail
                        yield InputBinding(cmd,  KeyGesture(Key.Add,ModifierKeys.Control)) 
                    | gg -> 
                        yield
                            match gg.Split('+') |> Array.map ( fun k -> k.Trim()) with
                            | [| m1; m2; k |]   -> InputBinding(cmd,  KeyGesture(getKey k, getModKey m1 + getModKey m2))
                            | [| m; k |]        -> InputBinding(cmd,  KeyGesture(getKey k, getModKey m))
                            | [| k |]           -> InputBinding(cmd,  KeyGesture(getKey k))
                            | _ -> 
                                log.PrintAppErrorMsg "*AllInputBindings: failed to parse command Input gesture '%s'" g
                                InputBinding(cmd,  KeyGesture(Key.None))
            |]
        
        with e -> 
            log.PrintAppErrorMsg "*AllInputBindings: failed to create keyboard shortcuts: %A"e
            [| |]
    

        