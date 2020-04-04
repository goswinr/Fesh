namespace Seff

open System
open System.Windows.Input

open Seff.Fsi
open Seff.CreateTab
open Seff.FileDialogs



module CommandHelp = 

    type Command(canExecute, execute) as this = 
        // from https://github.com/Prolucid/Elmish.WPF/blob/0c7ce6a1e21a1314b299eba05684f2f60e08c353/src/Elmish.WPF/Binding.fs#L12
        let canExecuteChanged = Event<EventHandler,EventArgs>()
        let handler = EventHandler(fun _ _ -> this.RaiseCanExecuteChanged()) 
        do CommandManager.RequerySuggested.AddHandler(handler)        
        member private x._Handler = handler // CommandManager only keeps a weak reference to the event handler, so a strong handler must be maintained
        member x.RaiseCanExecuteChanged () = canExecuteChanged.Trigger(x,EventArgs.Empty)        
        //interface is implemented as members and as interface members( to be sure it works):
        member x.CanExecuteChanged = canExecuteChanged.Publish
        member x.CanExecute p = canExecute p
        member x.Execute p = execute p
        interface ICommand with
            [<CLIEvent>]
            member x.CanExecuteChanged = x.CanExecuteChanged
            member x.CanExecute p =      x.CanExecute p 
            member x.Execute p =         x.Execute p 
    
    /// creates a ICommand
    let mkCmd canEx ex = new Command(canEx,ex) :> ICommand

    /// creates a ICommand, CanExecute is always true
    let mkCmdSimple action =
        let ev = Event<_ , _>()
        { new Windows.Input.ICommand with
                member this.CanExecute(obj) = true
                member this.Execute(obj) = action(obj)
                [<CLIEvent>]
                member this.CanExecuteChanged = ev.Publish
                }

module Commands = 
    open CommandHelp
    //see https://github.com/icsharpcode/AvalonEdit/blob/697ff0d38c95c9e5a536fbc05ae2307ec9ef2a63/ICSharpCode.AvalonEdit/Editing/CaretNavigationCommandHandler.cs#L73
    //TODO these gets evaluated for each command on every mouse click or key perss . is this OK?  any lag ?? in Canexecute for commands
    let private isTab       a   = Tab.current.IsSome (* Log.print "isTab was evalauted"; *) 
    let private isEditorSel a   = Tab.current.IsSome && Tab.currEditor.SelectionLength > 0
    let private isLogSel    a   = UI.log.SelectionLength > 0
    let private runsAsync   a = Fsi.state = Fsi.Evaluating && Fsi.mode = Fsi.Async
             
    let RunSelectedText  = "Run Selected Text"        , "Alt + Enter"   , mkCmd isEditorSel (fun a -> Fsi.evaluate Tab.currEditor.SelectedText),"Sends the currently seleceted Text in the editor to FSharp Interactive"// TODO mark evaluated code with grey background
    let RunSelectedLines = "Run Selected Lines"       , "Ctrl + Enter"  , mkCmd isTab       (fun a -> Fsi.evaluate <| ModifyUI.expandSelectionToFullLines Tab.currTab),"Sends the currently seleceted Lines in the editor to FSharp Interactive.\r\nIncludes partially selected lines in full."
    let RunAllText       = "Run All Text"             , "F5"            , mkCmd isTab       (fun a -> Fsi.evaluate  Tab.currEditor.Text) ,"Send all text in the current file to FSharp Interactive"
    let RunAllTextSave   = "Save and Run All Text"    , "F6"            , mkCmd isTab       (fun a -> if save Tab.currTab then Fsi.evaluate  Tab.currEditor.Text) ,"First Save current File, then send all it's text to FSharp Interactive"
                                                        
    let ResetFSI         = "Reset FSI"                , "Ctrl + Alt + R", mkCmd isTab     (fun a -> Fsi.reset()    ),"Reset FSharp Interactive"
    let CancelFSI        = "Cancel FSI"               , "Ctrl + Break"  , mkCmd runsAsync (fun a -> Fsi.cancelIfAsync()   ),"Cancel running FSI evaluation (only available in asynchronous mode) "
    let ClearFSI         = "Clear Log"                , "Ctrl + Alt + C", mkCmdSimple (fun a -> Fsi.clearLog() ),"Clear all text from FSI Log window"
    let ToggleSync       = "Toggle Sync / Async"      , ""              , mkCmdSimple (fun a -> Fsi.toggleSync()),"Switch between synchronous and asynchronous evaluation in FSI, see status in StatusBar"
                                                        
    let NewTab           = "New File"                 , "Ctrl + N"      , mkCmdSimple (fun a -> newTab(Config.getDefaultCode(),None,true)|>ignore),"Create a new script file"
    let OpenFile         = "Open File"                , "Ctrl + O"      , mkCmdSimple (fun a -> openFileDlg newTab),"Open a script file"
    let OpenTemplateFile = "Edit Template File"         ,""             , mkCmdSimple (fun a -> openFile(IO.FileInfo(Config.fileDefaultCode),newTab,true)|>ignore),"Opens the template file that is used when creating a New File ( Ctrl + N)"
    let Close            = "Close File"               , "Ctrl + F4"     , mkCmdSimple (fun a -> altF4close()),"Closes the current Tab, if no tab present Application will be closed" 
    
    let SaveIncremental  = "Save Incremental"         , ""              , mkCmd isTab (fun a -> saveIncremental Tab.currTab |> ignore),"increases the last letter of filename, can be alphabetic or numeric "
    let SaveAs           = "Save As"                  , "Ctrl + Alt + S", mkCmd isTab (fun a -> saveAs          Tab.currTab |> ignore),"Shows a dialog to save the file at a new path or name."
    let Save             = "Save"                     , "Ctrl + S"      , mkCmd isTab (fun a -> save            Tab.currTab |> ignore),"Saves the file. Shows a dialog only if the open file does not exist anymore"
    let SaveAll          = "Save All"                 , "Ctrl + Shift + S", mkCmd isTab (fun a -> Seq.iter (save >> ignore) Tab.allTabs),"Saves all tabs. Shows a dialog only if the open file does not exist on disk"
    let SaveLog          = "Save Text in Log"         , ""              , mkCmd isTab (fun a -> saveLog            Tab.currTab |> ignore),"Save all text from Log Window"
    let SaveLogSel       = "Save Selected Text in Log", ""              , mkCmd isLogSel (fun a -> saveLogSelected Tab.currTab |> ignore),"Save selected text from Log Window"
                                                        
    let FontBigger       = "Make Font Bigger"         , "Ctrl + '+'"    , mkCmdSimple (fun a -> ModifyUI.fontBigger ()) ,"Increase Text Size for both Editor and Log"
    let FontSmaller      = "Make Font Smaller"        , "Ctrl + '-'"    , mkCmdSimple (fun a -> ModifyUI.fontSmaller()) ,"Decrease Text Size for both Editor and Log"
    let Comment          = "Comment"                  , "Ctrl + K"      , mkCmd isTab (fun a -> ModifyCode.comment Tab.currTab) ,"Removes '//' at the beginning of current line, \r\nor from all line touched by current selection"
    let UnComment        = "UnComment"                , "Ctrl + U"      , mkCmd isTab (fun a -> ModifyCode.unComment Tab.currTab),"Puts '//' at the beginning of current line, \r\nor all line touched by current selection"
    // todo refine 
    let ToggleLogSize    = "Toggle Log Maximased"       , "Ctrl + M"   , mkCmdSimple (fun a ->  WindowLayout.maxLog()) ,"Maximises or resets the size of the Log window. \r\n(depending on curren state)"
    let ToggleSplit      = "Toggle Screen Split"        , "Ctrl + T"   , mkCmdSimple (fun a ->  WindowLayout.toggleSplit()), "Toggle between vertical and horizontal Screen Split of Editor and Log"
    let ToggleLogLineWrap= "Toggle Line Wraping in Log" , "Alt + Z"    , mkCmdSimple (fun a ->  ModifyUI.toggleLogLineWrap()), "Toggle Line Wraping in Log window"

    // Ctrl + I does not work,(alredy taken by avalonedit?) see https://github.com/icsharpcode/SharpDevelop/wiki/Keyboard-Shortcuts
    let SelectLine      = "Select Current Line"       , "Ctrl  + L"     , mkCmd isTab (fun a -> ModifyUI.expandSelectionToFullLines Tab.currTab|> ignore),"Select current line"
    let SelectLinesUp   = "Select Lines Upwards"      , "Shift + Up"    , mkCmd isTab (fun a -> ModifyUI.expandSelectionToFullLines Tab.currTab|> ignore),"Not implemented yet"
    let SelectLinesDown = "Select Lines Downwards"    , "Shift + Down"  , mkCmd isTab (fun a -> ModifyUI.expandSelectionToFullLines Tab.currTab|> ignore),"Not implemented yet"

    let SettingsFolder = "Open Settings Folder"       , ""              , mkCmdSimple (fun a -> Config.openConfigFolder()), "Opens the Folder where user settinsg such as default file content is saved."

    let ReloadXshd =     "Reload Xshd file"          , "F11"            , mkCmd isTab (fun a -> XshdHighlighting.setFSharp(Tab.currTab.Editor,true)), "for Testing only: Reloads the Syntax highlighting file FSharpSynatxHighlighterExtended.xshd"

    // built in Commands                                
    let Copy             = "Copy"                     , "Ctrl + C"      , ApplicationCommands.Copy, "Copy selected text, or full current line if nothing is selceted."
    let Cut              = "Cut"                      , "Ctrl + X"      , ApplicationCommands.Cut  ,"Cut selected text, or full current line if nothing is selceted."
    let Paste            = "Paste"                    , "Ctrl + V"      , ApplicationCommands.Paste,"Insert text from Clipboard"
    let Undo             = "UnDo"                     , "Ctrl + Z"      , ApplicationCommands.Undo,"Undo last edit"
    let Redo             = "ReDo"                     , "Ctrl + Y"      , ApplicationCommands.Redo,"Undo last undo"
    let Find             = "Find"                     , "Ctrl + F"      , ApplicationCommands.Find,"Fint text of current selection"
    let Replace          = "Replace"                  , "Ctrl + H"      , ApplicationCommands.Replace ,"Not implemented yet"//TODO implement

       
            
    let allShortCutKeyGestures = 
        
        let allCustomCommands = [  //for seting up Key gestures below
             RunSelectedText  
             RunSelectedLines 
             RunAllText
             RunAllTextSave
            
             ResetFSI         
             CancelFSI        
             ClearFSI
             if Config.currentRunContext = Config.RunContext.Hosted then ToggleSync
            
             NewTab           
             OpenFile 
             Close
            
             SaveIncremental
             SaveAs           
             Save
             SaveAll
             SaveLog          
             SaveLogSel       
            
             FontBigger       
             FontSmaller      
             Comment          
             UnComment
             ToggleLogSize
             ToggleSplit
             ToggleLogLineWrap

             SelectLine
             SelectLinesUp  
             SelectLinesDown

             ReloadXshd
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
                   | _ -> Log.print "*AllInputBindings: failed to parse Command Key '%A'" x; Key.NoName
        
        let getModKey = function
            |"Ctrl"     -> ModifierKeys.Control        
            | x -> match ModifierKeys.TryParse(x,true) with
                   |true, k -> k
                   | _ -> Log.print "*AllInputBindings: failed to parse ModifierKey '%A'" x; ModifierKeys.None

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
                                Log.print "*AllInputBindings: failed to parse command Input gesture '%s'" g
                                InputBinding(cmd,  KeyGesture(Key.None))
            |]
        
        with e -> 
            Log.print "*AllInputBindings: failed to create keyboard shortcuts: %A"e
            [| |]
