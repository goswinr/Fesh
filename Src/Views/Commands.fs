namespace Seff.Views

open System
open System.Windows
open System.Windows.Input 

open AvalonEditB
open AvalonEditB.Editing

open FsEx.Wpf.Command

open Seff
open Seff.Model
open Seff.Editor
open Seff.Editor.Selection
open Seff.Editor.SelectionForEval

type Commands (grid:TabsAndLog, statusBar:SeffStatusBar)  = 
    
    let fonts = Fonts(grid)

    let tabs = grid.Tabs
    let log = grid.Log
    let config= grid.Config
    let fsi = tabs.Fsi
    
                                     
    let evalAllText()          =                                             fsi.Evaluate {editor=tabs.Current.Editor; amount=All}                               
    let evalAllTextSave()      =               tabs.SaveAsync(tabs.Current); fsi.Evaluate {editor=tabs.Current.Editor; amount=All} 
    let evalAllTextSaveClear() =  log.Clear(); tabs.SaveAsync(tabs.Current); fsi.Evaluate {editor=tabs.Current.Editor; amount=All} 
    let evalContinue()         =               tabs.SaveAsync(tabs.Current); fsi.Evaluate {editor=tabs.Current.Editor; amount=ContinueFromChanges} 
    let markEvaluated()        =  tabs.Current.Editor.EvalTracker.MarkEvalutedTillOffset(Selection.currentLineEnd tabs.CurrAvaEdit + 2 )

    let evalSelectedLines()    =  fsi.Evaluate {editor=tabs.Current.Editor; amount = FsiSegment <|SelectionForEval.expandSelectionToFullLines(tabs.CurrAvaEdit) }
    let evalSelectedText()     =  fsi.Evaluate {editor=tabs.Current.Editor; amount = FsiSegment <|SelectionForEval.current (tabs.CurrAvaEdit)                   }   // null or empty check is done in fsi.Evaluate        
    let evalTillCursor()       =  fsi.Evaluate {editor=tabs.Current.Editor; amount = FsiSegment <|SelectionForEval.linesTillCursor(tabs.CurrAvaEdit)            }           
    
    
    //let evalFromCursor()       =  let ln,tx = Selection.linesFromCursor(tabs.CurrAvaEdit)             in  fsi.Evaluate {editor=tabs.Current.Editor; code = tx ; file=tabs.Current.FilePath; allOfFile=false; fromLine = ln }           
        
    let compileScript(useMsBuild) = CompileScript.compileScript(tabs.CurrAvaEdit.Text , tabs.Current.FilePath, true, useMsBuild) 
    
    let version = lazy (let an = Reflection.Assembly.GetAssembly(tabs.GetType()).GetName() in sprintf "%s %s" an.Name (an.Version.ToString()))

    //see https://github.com/icsharpcode/AvalonEdit/blob/697ff0d38c95c9e5a536fbc05ae2307ec9ef2a63/AvalonEditB/Editing/CaretNavigationCommandHandler.cs#L73
    //TODO these get evaluated for each cmd on every mouse click or key press. is this OK?  any lag ?? in Canexecute for commands

    let isEse (_:obj)  = tabs.Current.Editor.AvaEdit.SelectionLength > 0
    let isLse (_:obj)  = log.AvalonLog.Selection.Length > 0
    let isAsy (_:obj)  = fsi.State = Evaluating && fsi.Mode.IsAsync
    let isAsy472 (x:obj) = fsi.State = Evaluating && fsi.Mode = Async472

    // NOTE :--------------------------------------------------------------------
    // some more gestures and for selection manipulation are defined in module CursorBehaviour.previewKeyDown(..)
    // NOTE :--------------------------------------------------------------------

    // File menu:                                                                          
    member val NewTab            = {name= "New File"                  ;gesture= "Ctrl + N"       ;cmd= mkCmdSimple (fun _ -> tabs.AddTab(new Tab(Editor.New(config)),true))      ;tip= "Create a new script file."   }
    member val OpenFile          = {name= "Open File"                 ;gesture= "Ctrl + O"       ;cmd= mkCmdSimple (fun _ -> tabs.OpenFile())                                    ;tip= "Open a script file."  }
    member val OpenTemplateFile  = {name= "Edit Template File"        ;gesture= ""               ;cmd= mkCmdSimple (fun _ -> tabs.AddFile(config.DefaultCode.FileInfo,true)|> ignore)     ;tip= "Opens the template file that is used when creating a New File ( Ctrl + N)." }
    member val Save              = {name= "Save"                      ;gesture= "Ctrl + S"       ;cmd= mkCmdSimple (fun _ -> tabs.Save(tabs.Current) |> ignore )                 ;tip= "Saves the file. Shows a dialog only if the open file does not exist anymore." }
    member val Export            = {name= "Export"                    ;gesture= ""               ;cmd= mkCmdSimple (fun _ -> tabs.Export(tabs.Current) |> ignore)                ;tip= "Shows a dialog to export the file at a new path or name. But keeps the file open at previous location." }
    member val SaveAs            = {name= "Save As"                   ;gesture= "Ctrl + Alt + S" ;cmd= mkCmdSimple (fun _ -> tabs.SaveAs(tabs.Current) |> ignore)                ;tip= "Shows a dialog to save the file at a new path or name." }
    member val SaveIncrementing  = {name= "Save Incrementing"         ;gesture= ""               ;cmd= mkCmdSimple (fun _ -> tabs.SaveIncremental(tabs.Current) |> ignore)       ;tip= "Save with increased last character of filename.\r\nCan be alphabetic or numeric ( e.g.  d->e or 5->6).\r\nDoes not overwrite any existing file."}
    member val SaveAll           = {name= "Save All"                  ;gesture= "Ctrl + Shift + S";cmd= mkCmdSimple (fun _ -> for t in tabs.AllTabs do tabs.Save(t) |> ignore)   ;tip= "Saves all tabs. Shows a dialog only if the open file does not exist on disk." }
    member val Close             = {name= "Close File"                ;gesture= "Ctrl + F4"      ;cmd= mkCmdSimple (fun _ -> tabs.CloseTab(tabs.Current))                        ;tip= "Closes the current tab, if there is only one tab then the window will be closed."}
    member val SaveLog           = {name= "Save Text in Log"          ;gesture= ""               ;cmd= mkCmdSimple (fun _ -> log.SaveAllText(tabs.Current.FilePath))             ;tip= "Save all text from Log Window." }
    member val SaveLogSel        = {name= "Save Selected Text in Log" ;gesture= ""               ;cmd= mkCmd isLse (fun _ -> log.SaveSelectedText(tabs.Current.FilePath))        ;tip= "Save selected text from Log Window."  }
                                               
    //Edit menu:                                                                                                                              
    member val Comment           = {name= "Comment"                   ;gesture= "Ctrl + K"       ;cmd= mkCmdSimple (fun _ -> Commenting.comment tabs.CurrAvaEdit)                ;tip= "Removes '//' at the beginning of current line, \r\nor from all line touched by current selection" }
    member val UnComment         = {name= "Uncomment"                 ;gesture= "Ctrl + U"       ;cmd= mkCmdSimple (fun _ -> Commenting.unComment tabs.CurrAvaEdit)              ;tip= "Puts '//' at the beginning of current line, \r\nor all line touched by current selection" }                                                                                     
    member val ToggleComment     = {name= "Toggle Comment"            ;gesture= "Ctrl + /"       ;cmd= mkCmdSimple (fun _ -> Commenting.toggleComment tabs.CurrAvaEdit)          ;tip= "Toggels the commebntedlines in current selection." }                                                                                     
    
    member val SwapLineUp        = {name= "Swap Lines Up"              ;gesture= "Alt + Up"       ;cmd=mkCmdSimple (fun _ -> SwapLines.swapLinesUp  (tabs.Current.Editor)) ;tip= "Swap the current line(s) with the previous line."  }                                                                                     
    member val SwapLineDown      = {name= "Swap Lines Down"            ;gesture= "Alt + Down"     ;cmd=mkCmdSimple (fun _ -> SwapLines.swapLinesDown(tabs.Current.Editor)) ;tip= "Swap the current line(s) with the next line."  } 
    
    member val ToUppercase       = {name= "To UPPERCASE"              ;gesture= ""               ;cmd=AvalonEditCommands.ConvertToUppercase                                      ;tip= "Convertes the selected text to UPPERCASE."  }     
    member val Tolowercase       = {name= "To lowercase"              ;gesture= ""               ;cmd=AvalonEditCommands.ConvertToLowercase                                      ;tip= "Convertes the selected text to lowercase."  }     
    member val ToTitleCase       = {name= "To Titlecase "             ;gesture= ""               ;cmd=AvalonEditCommands.ConvertToTitleCase                                      ;tip= "Convertes the selected text to Titlecase."  }     
    member val ToggleBoolean     = {name= "Toggle bool literal"       ;gesture= "Ctrl + T"       ;cmd = mkCmdSimple (fun _ -> CursorBehaviour.toggleBoolean(tabs.CurrAvaEdit) )  ;tip= "Convertes a 'true' literal to 'false' and a 'false' literal to 'true' if they are currently selected exclusively" }     
    
    member val AlignCode         = {name= "Align Code"                ;gesture= "Ctrl + I"       ;cmd = mkCmdSimple (fun _ -> Formating.alignByNonLetters(tabs.Current.Editor))  ;tip= "Inserts spaces where required so that non leter symbols align verticaly" }     
                                                                                                                                                 
    //Select menu:                                                                                                                               
    member val SelectLine        = {name= "Select Current Line"       ;gesture= "Ctrl  + L"     ;cmd= mkCmdSimple (fun _ -> expandSelectionToFullLines(tabs.CurrAvaEdit) |> ignore )  ;tip= "Select current line"} // TODO compare VSCODE shortcuts to  see https://github.com/icsharpcode/SharpDevelop/wiki/Keyboard-Shortcuts
    member val SwapWordLeft      = {name= "Swap selected word left"   ;gesture= "Alt + Left"    ;cmd= mkCmdSimple (fun _ -> SwapWords.left  tabs.CurrAvaEdit|> ignore )  ;tip= "Swaps the currently selected word with the word on the left. A word may include any letter, digit, underscore or dot. "} 
    member val SwapWordRight     = {name= "Swap selected word right"  ;gesture= "Alt + Right"   ;cmd= mkCmdSimple (fun _ -> SwapWords.right tabs.CurrAvaEdit|> ignore )  ;tip= "Swaps the currently selected word with the word on the right. A word may include any letter, digit, underscore or dot. "} 
                                                                                                                                    
    //FSI menu:                                                                                                                              
    member val RunAllText        = {name= "Evaluate All"                   ;gesture= "F5"             ;cmd= mkCmdSimple (fun _ -> evalAllText() )             ;tip= "Send all text in the current file to FSharp Interactive" }
    member val RunAllTextSave    = {name= "Save, Evaluate All"             ;gesture= "F6"             ;cmd= mkCmdSimple (fun _ -> evalAllTextSave())          ;tip= "First Save current File, then send all it's text to FSharp Interactive" }
    member val RunAllTxSaveClear = {name= "Save, Clear Log, Evaluate All"  ;gesture= "F7"             ;cmd= mkCmdSimple (fun _ -> evalAllTextSaveClear())     ;tip= "First Save current File, then Clear Log, then send all text to FSharp Interactive" }  
    member val RunCurrentLines   = {name= "Evaluate CurrentLines"          ;gesture= "Ctrl + Enter"   ;cmd= mkCmdSimple (fun _ -> evalSelectedLines())        ;tip= "Sends the currently seleceted Lines in the editor to FSharp Interactive.\r\nIncludes partially selected lines in full."}
    member val RunSelectedText   = {name= "Evaluate Selected Text"         ;gesture= "Alt + Enter"    ;cmd= mkCmd isEse (fun _ -> evalSelectedText())         ;tip= "Sends the currently seleceted Text in the editor to FSharp Interactive" }// TODO mark evaluated code with grey background
    member val RunTextTillCursor = {name= "Evaluate till Cursor"           ;gesture= "F3"             ;cmd= mkCmdSimple (fun _ -> evalTillCursor())           ;tip= "Sends all lines till and including the current line  to FSharp Interactive" }
    //member val RunTextFromCursor = {name= "Run Text from Cursor"      ;gesture= "F4"             ;cmd= mkCmdSimple (fun _ -> evalFromCursor())           ;tip= "Sends all lines from and including the current line  to FSharp Interactive" }    
    
    member val EvalContinue      = {name= "Save, continue evaluation"    ;gesture= "F4"             ;cmd= mkCmdSimple (fun _ -> evalContinue())           ;tip= "Sends all chnaged or new lines from end of grey text to FSharp Interactive" }    
    member val MarkEval          = {name= "Mark as Evaluated till Current Line"  ;gesture= ""               ;cmd= mkCmdSimple (fun _ -> markEvaluated())               ;tip= "Mark text till current line inclusive as evaluated" }    
    
    
    member val ClearLog          = {name= "Clear Log"                 ;gesture= "Ctrl + Alt + C" ;cmd= mkCmdSimple (fun _ -> log.Clear())                ;tip= "Clear all text from FSI Log window"  }
    member val CancelFSI         = {name= "Cancel FSI"                ;gesture= "Ctrl + Break"   ;cmd= mkCmd isAsy472 (fun _ -> fsi.CancelIfAsync())    ;tip= "Cancel running FSI evaluation \r\n(only available on .NET Framework and only in asynchronous mode)" }
    member val ResetFSI          = {name= "Reset FSI"                 ;gesture= "Ctrl + Alt + R" ;cmd= mkCmdSimple (fun _ -> log.Clear();fsi.Reset())    ;tip= "Clear all text from FSI Log window and reset FSharp Interactive" }
    member val ToggleSync        = {name= "Toggle Sync / Async"       ;gesture= ""               ;cmd= mkCmdSimple (fun _ -> fsi.ToggleSync())           ;tip= "Switch between synchronous and asynchronous evaluation in FSI, see status in StatusBar"} 
    member val CompileScriptSDK  = {name= "Compile Script via dotnet SDK" ;gesture= "Ctrl + B"        ;cmd= mkCmdSimple (fun _ -> compileScript(false))  ;tip= "Create an net48 fsproj with current code (including unsaved changes) and build it via 'dotnet build' in Release x64. dotnet SDK needs to be installed."} 
    member val CompileScriptMSB  = {name= "Compile Script via MSBuild"    ;gesture= "Ctrl + Shift + B";cmd= mkCmdSimple (fun _ -> compileScript(true))   ;tip= "Create an net48 fsproj with current code (including unsaved changes) and build it via  MSBuild in Release x64. VS2019 needs to be installed."} 
                                                                                                                                       
   //View menu:                                                                                                                        
    member val ToggleSplit       = {name= "Toggle Window Split"       ;gesture= ""               ;cmd= mkCmdSimple (fun _ -> grid.ToggleSplit())         ;tip= "Toggle between vertical and horizontal window arrangement of Editor and Log View" }
    member val ToggleLogSize     = {name= "Toggle Log Maximased"      ;gesture= "F11"            ;cmd= mkCmdSimple (fun _ -> grid.ToggleMaxLog())        ;tip= "Maximises or resets the size of the Log window. \r\n(depending on current state)" }
    member val ToggleLogLineWrap = {name= "Toggle Line Wraping in Log";gesture= "Alt + Z"        ;cmd= mkCmdSimple (fun _ -> log.ToggleLineWrap(config)) ;tip= "Toggle Line Wraping in Log window" }  
    member val FontBigger        = {name= "Make Font Bigger"          ;gesture= "Ctrl + '+'"     ;cmd= mkCmdSimple (fun _ -> fonts.FontsBigger())         ;tip= "Increase Text Size for both Editor and Log" }
    member val FontSmaller       = {name= "Make Font Smaller"         ;gesture= "Ctrl + '-'"     ;cmd= mkCmdSimple (fun _ -> fonts.FontsSmaller())        ;tip= "Decrease Text Size for both Editor and Log" }
    member val CollapseCode      = {name= "Collapse all Code Foldings";gesture= ""               ;cmd= mkCmdSimple (fun _ -> Foldings.CollapseAll(tabs.Current.Editor,tabs.Config)) ;tip= "Collapse all Code Foldings in this file" }
    member val CollapsePrim      = {name= "Collapse primary Code Foldings";gesture= ""           ;cmd= mkCmdSimple (fun _ -> Foldings.CollapsePrimary(tabs.Current.Editor,tabs.Config)) ;tip= "Collapse primary Code Foldings, doesn't chnage secondary or tertiary foldings" }
    member val ExpandCode        = {name= "Expand all Code Foldings"  ;gesture= ""               ;cmd= mkCmdSimple (fun _ -> Foldings.ExpandAll(tabs.Current.Editor,tabs.Config))  ;tip= "Expand or unfold all Code Foldings in this file"  }
    member val PopOutToolTip     = {name= "Make Tooltip persitent"    ;gesture= "Ctrl + P"       ;cmd= mkCmdSimple (fun _ -> PopOut.create(grid,statusBar))  ;tip= "Makes all currently showing ToolTip, Typeinfo or Errorinfo windows persistent as pop up window" }
                                                                                                                                     
    //About Menu  
    member val Version           = {name= "Version " + version.Value  ;gesture= ""               ;cmd= mkCmdSimple (fun _ -> Diagnostics.Process.Start("http://seff.io/") |> ignore )     ;tip= "Opens a browser window showing http://seff.io/"  }
    member val SettingsFolder    = {name= "Open Settings Folder"      ;gesture= ""               ;cmd= mkCmdSimple (fun _ -> config.Hosting.OpenSettingsFolder())                         ;tip= "Opens the Folder where user settinsg such as default file content is saved." }
    member val AppFolder         = {name= "Open App Folder"           ;gesture= ""               ;cmd= mkCmdSimple (fun _ -> config.Hosting.OpenAppFolder())                              ;tip= "Opens the Folder where this App (Seff.exe) is loaded from." }
    member val ReloadXshdFile    = {name= "Reload SyntaxHighlighting" ;gesture= "F10"            ;cmd= mkCmdSimple (fun _ -> SyntaxHighlighting.setFSharp(tabs.CurrAvaEdit,config,true))  ;tip= "Reloads FSharpSynatxHighlighterExtended.xshd, this is useful for testing new highlighting files without a restart." }
    member val OpenXshdFile      = {name= "Open SyntaxHighlighting in VsCode" ;gesture= ""       ;cmd= mkCmdSimple (fun _ -> SyntaxHighlighting.openVSCode())                             ;tip= "Opens the FSharpSynatxHighlighterExtended.xshd, file in VsCode." }

    //--------------------------
    // Built in Commands from Avalonedit (listed as functiosn so the can be created more than once( eg for menu; and context menu)
    //----------------------------
    member val Copy      = {name= "Copy"     ;gesture=  "Ctrl + C"     ;cmd= ApplicationCommands.Copy   ; tip= "Copy selected text\r\nOr full current line if nothing is selceted." }
    member val Cut       = {name= "Cut"      ;gesture=  "Ctrl + X"     ;cmd= ApplicationCommands.Cut    ; tip= "Cut selected text\r\nOr full current line if nothing is selceted." }                       
    member val Paste     = {name= "Paste"    ;gesture=  "Ctrl + V"     ;cmd= ApplicationCommands.Paste  ; tip= "Insert text from Clipboard." }                     
    member val UnDo      = {name= "UnDo"     ;gesture=  "Ctrl + Z"     ;cmd= ApplicationCommands.Undo   ; tip= "Undo last edit."  }                      
    member val ReDo      = {name= "ReDo"     ;gesture=  "Ctrl + Y"     ;cmd= ApplicationCommands.Redo   ; tip= "Undo last undo."  }                      
    member val Find      = {name= "Find"     ;gesture=  "Ctrl + F"     ;cmd= ApplicationCommands.Find   ; tip= "Find text of current selection." }                      
    member val Replace   = {name= "Replace"  ;gesture=  "Ctrl + H"     ;cmd= ApplicationCommands.Replace; tip= "Find and replace text of current selection."  }   
    
    member val DeleteLine = {name= "Delete Line"  ;gesture=  "Ctrl + D"       ;cmd = AvalonEditCommands.DeleteLine                ; tip= "Deletes the current line."  }  
    member val TrailWhite = {name= "Removes trailing whitespace" ;gesture= "" ;cmd = AvalonEditCommands.RemoveTrailingWhitespace  ; tip= "Removes trailing whitespace from the selected lines (or the whole document if the selection is empty)." }     
    
    
    // this shortcut is implemented in Avalonedit but I cant find out wher the routed commnd class is
    //member val SelectLinesUp      = {name= "Select Lines Upwards"      ;gesture= "Shift + Up"     ;cmd = null ;tip= "Not implemented yet"}
    //member val SelectLinesDown    = {name= "Select Lines Downwards"    ;gesture= "Shift + Down"   ;cmd = null ;tip= "Not implemented yet"} //TODO!   
           
    member val BoxSelLeftByCharacter  = {name= "Box Select Left By Character"  ;gesture= "Alt + Shift + Left"        ;cmd= RectangleSelection.BoxSelectLeftByCharacter  ; tip= "Expands the selection left by one character; creating a rectangular selection." }  
    member val BoxSelRightByCharacter = {name= "Box Select Right By Character" ;gesture= "Alt + Shift + Right"       ;cmd= RectangleSelection.BoxSelectRightByCharacter ; tip= "Expands the selection right by one character; creating a rectangular selection." }  
    member val BoxSelLeftByWord       = {name= "Box Select Left By Word"       ;gesture= "Ctrl + Alt + Shift + Left" ;cmd= RectangleSelection.BoxSelectLeftByWord       ; tip= "Expands the selection left by one word; creating a rectangular selection." }        
    member val BoxSelRightByWord      = {name= "Box Select Right By Word"      ;gesture= "Ctrl + Alt + Shift + Right";cmd= RectangleSelection.BoxSelectRightByWord      ; tip= "Expands the selection right by one word; creating a rectangular selection." }       
    member val BoxSelUpByLine         = {name= "Box Select Up By Line"         ;gesture= "Alt + Shift + Up"          ;cmd= RectangleSelection.BoxSelectUpByLine         ; tip= "Expands the selection up by one line; creating a rectangular selection." }          
    member val BoxSelDownByLine       = {name= "Box Select Down By Line"       ;gesture= "Alt + Shift + Down"        ;cmd= RectangleSelection.BoxSelectDownByLine       ; tip= "Expands the selection down by one line; creating a rectangular selection." }        
    member val BoxSelToLineStart      = {name= "Box Select To Line Start"      ;gesture= "Alt + Shift + Home"        ;cmd= RectangleSelection.BoxSelectToLineStart      ; tip= "Expands the selection to the start of the line; creating a rectangular selection." }       
    member val BoxSelToLineEnd        = {name= "Box Select To Line End"        ;gesture= "Alt + Shift + End"         ;cmd= RectangleSelection.BoxSelectToLineEnd        ; tip= "Expands the selection to the end of the line; creating a rectangular selection." }  

   // TODO add  all built in  DocmentNavigatin shortcuts
   
    member this.Fonts = fonts

    /// exluding the ones already provided by avalonedit
    member this.SetUpGestureInputBindings () =         
            
            // NOTE :--------------------------------------------------------------------
            // some more gestures and selection depending  ovewrites  are defined in CursorBehaviour.previewKeyDown
            // NOTE :--------------------------------------------------------------------

            let allCustomCommands = [  //for setting up Key gestures below, exluding the ones already provided by avalonedit
                 this.NewTab           
                 this.OpenFile         
                 //this.OpenTemplateFile 
                 this.Save
                 //this.Export
                 this.SaveAs           
                 //this.SaveIncrementing 
                 this.SaveAll          
                 this.Close            
                 //this.SaveLog          
                 //this.SaveLogSel
             
                 this.Comment          
                 this.UnComment
                 this.ToggleComment
                 //this.TrailWhite
                 this.ToggleBoolean
                 this.AlignCode

                 //this.SwapLineDown     // handeled via native keyboard hook see module KeyboardNative
                 //this.SwapLineUp       // handeled via native keyboard hook see module KeyboardNative                 
                 
                 this.SelectLine
                 //this.SwapWordLeft    // key gesture handeled via previewKeyDown event in CursorBehaviour module
                 //this.SwapWordRight   // key gesture handeled via previewKeyDown event in CursorBehaviour module
                 //this.SelectLinesUp   // implemented in AvalonEditB
                 //this.SelectLinesDown // implemented in AvalonEditB
                            
                 this.RunAllText       
                 this.RunAllTextSave 
                 this.RunAllTxSaveClear
                 this.RunCurrentLines 
                 this.RunSelectedText 
                 this.RunTextTillCursor
                 //this.RunTextFromCursor
                 this.EvalContinue
                 this.ClearLog        
                 this.CancelFSI        
                 this.ResetFSI         
                 if config.Hosting.IsHosted then this.ToggleSync
                 this.CompileScriptSDK
                 this.CompileScriptMSB
             
                 //this.ToggleSplit      
                 this.ToggleLogSize    
                 this.ToggleLogLineWrap
                 this.FontBigger       
                 this.FontSmaller
                 this.PopOutToolTip
                                       
                 //this.SettingsFolder   
                 //this.AppFolder   
                 this.ReloadXshdFile
                 ] 


            // these functions parse the KeyGesture from a string defined above. 
            // eg. "Ctrl + V" becomes KeyGesture(Key.V , ModifierKeys.Control)
            // this is to make sure the displayed gesture matches the actual gesture
            let getKey = function
                |"'-'"   -> Key.Subtract //|"'+'"   -> Key.Add // fails on gg.Split('+') 
                |"Break" -> Key.Cancel
                |"Up"    -> Key.Up
                |"Down"  -> Key.Down
                |"/"     -> Key.OemQuestion // the '/' key next to right shift
                | x -> match Key.TryParse(x,true) with
                       |true, k -> k
                       | _ -> log.PrintfnAppErrorMsg "*AllInputBindings: failed to parse cmd Key '%A'" x; Key.NoName
        
            let getModKey = function
                |"Ctrl"     -> ModifierKeys.Control        
                | x -> match ModifierKeys.TryParse(x,true) with
                       |true, k -> k
                       | _ -> log.PrintfnAppErrorMsg "*AllInputBindings: failed to parse ModifierKey '%A'" x; ModifierKeys.None
        
        
            try
                let bindings = 
                    [|  for cmd in allCustomCommands do
                            match cmd.gesture.Trim() with
                            | "" -> log.PrintfnAppErrorMsg "*SetUpGestureInputBindings: Input gesture is empty for '%s'" cmd.name
                            | "Ctrl + '+'" | "Ctrl + +" | "Ctrl +" -> // because  gg.Split('+') would fail
                                yield InputBinding(cmd.cmd,  KeyGesture(Key.Add,ModifierKeys.Control)) 
                            | gg ->
                                yield
                                    match gg.Split('+') |> Array.map ( fun k -> k.Trim()) with
                                    | [| m1; m2; k |]   -> InputBinding(cmd.cmd,  KeyGesture(getKey k, getModKey m1 + getModKey m2))
                                    | [| m; k |]        -> InputBinding(cmd.cmd,  KeyGesture(getKey k, getModKey m))
                                    | [| k |]           -> InputBinding(cmd.cmd,  KeyGesture(getKey k))
                                    | _ -> 
                                        log.PrintfnAppErrorMsg "*SetUpGestureInputBindings: failed to parse cmd Input gesture '%s' for '%s'" cmd.gesture cmd.name
                                        InputBinding(cmd.cmd,  KeyGesture(Key.None))
                                        // TODO check for memoryleaks: https://github.com/icsharpcode/AvalonEdit/blame/master/ICSharpCode.AvalonEdit/Editing/TextAreaDefaultInputHandlers.cs#L71-L79

                    |]
                grid.Window.Window.InputBindings.AddRange (bindings)
                
                // to no redirect alt to the menu bar :
                grid.Tabs.Control.InputBindings.Add (InputBinding(this.RunSelectedText.cmd, KeyGesture(Key.Return , ModifierKeys.Alt))) |> ignore 
                grid.Tabs.Control.InputBindings.Add (InputBinding(this.RunSelectedText.cmd, KeyGesture(Key.Enter  , ModifierKeys.Alt))) |> ignore 
            with e -> 
                log.PrintfnAppErrorMsg "*AllInputBindings: failed to create keyboard shortcuts: %A"e
         
    

        