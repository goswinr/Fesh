namespace Seff.Views

open System
open System.Windows.Input    
open Seff.Config
open Seff.Views.Util
open ICSharpCode.AvalonEdit.Editing

open Seff.Editor
open Seff
open Seff.Editor.Selection
open ICSharpCode.AvalonEdit



type Commands (grid:TabsAndLog)  = 
    
    let fonts = Fonts(grid)

    let tabs = grid.Tabs
    let log = grid.Log
    let config= grid.Config
    let fsi = tabs.Fsi
    


    let evalAllText()          =                                  fsi.Evaluate {code=tabs.CurrAvaEdit.Text                                    ; file=tabs.Current.FilePath; allOfFile=true ; fromLine = 1}                               
    let evalAllTextSave()      =  if tabs.Save(tabs.Current) then fsi.Evaluate {code=tabs.CurrAvaEdit.Text                                    ; file=tabs.Current.FilePath; allOfFile=true ; fromLine = 1} 
    let evalAllTextSaveClear() =  log.Clear(); if tabs.Save(tabs.Current) then  fsi.Evaluate {code=tabs.CurrAvaEdit.Text                      ; file=tabs.Current.FilePath; allOfFile=true ; fromLine = 1} // clear needs to be done first for correct coloring of log
    let evalSelectedLines()    =                                  fsi.Evaluate {code = Selection.expandSelectionToFullLines(tabs.CurrAvaEdit) ; file=tabs.Current.FilePath; allOfFile=false; fromLine = 99}//DODO wrong 1 
    let evalSelectedText()     =                                  fsi.Evaluate {code = tabs.CurrAvaEdit.SelectedText                          ; file=tabs.Current.FilePath; allOfFile=false; fromLine = 99}//DODO wrong 1 
    let evalTillCursor()       =                                  fsi.Evaluate {code = Selection.linesTillCursor(tabs.CurrAvaEdit)            ; file=tabs.Current.FilePath; allOfFile=false; fromLine = 99}//DODO wrong 1 
    let evalFromCursor()       =                                  fsi.Evaluate {code = Selection.linesFromCursor(tabs.CurrAvaEdit)            ; file=tabs.Current.FilePath; allOfFile=false; fromLine = 99}//DODO wrong 1 
    
    //see https://github.com/icsharpcode/AvalonEdit/blob/697ff0d38c95c9e5a536fbc05ae2307ec9ef2a63/ICSharpCode.AvalonEdit/Editing/CaretNavigationCommandHandler.cs#L73
    //TODO these gets evaluated for each cmd on every mouse click or key perss . is this OK?  any lag ?? in Canexecute for commands

    let isEse a  = tabs.Current.Editor.AvaEdit.SelectionLength > 0
    let isLse a  = log.ReadOnlyEditor.SelectionLength > 0
    let isAsy a  = fsi.State = Evaluating && fsi.Mode = Async

  
    // File menu:                                                                          
    member val NewTab            = {name= "New File"                  ;gesture= "Ctrl + N"       ;cmd= mkCmdSimple (fun _ -> tabs.AddTab(new Tab(Editor.New(config)),true))      ;tip= "Create a new script file."                                                       }
    member val OpenFile          = {name= "Open File"                 ;gesture= "Ctrl + O"       ;cmd= mkCmdSimple (fun _ -> tabs.OpenFile())                                    ;tip= "Open a script file."                                                             }
    member val OpenTemplateFile  = {name= "Edit Template File"        ;gesture= ""               ;cmd= mkCmdSimple (fun _ -> tabs.AddFile(config.DefaultCode.FileInfo,true)|>ignore)     ;tip= "Opens the template file that is used when creating a New File ( Ctrl + N)."      }
    member val Save              = {name= "Save"                      ;gesture= "Ctrl + S"       ;cmd= mkCmdSimple (fun _ -> tabs.Save(tabs.Current) |> ignore )                 ;tip= "Saves the file. Shows a dialog only if the open file does not exist anymore."    }
    member val Export            = {name= "Export"                    ;gesture= ""               ;cmd= mkCmdSimple (fun _ -> tabs.Export(tabs.Current) |> ignore)                ;tip= "Shows a dialog to export the file at a new path or name. But keeps the file open at previous location."                          }
    member val SaveAs            = {name= "Save As"                   ;gesture= "Ctrl + Alt + S" ;cmd= mkCmdSimple (fun _ -> tabs.SaveAs(tabs.Current) |> ignore)                ;tip= "Shows a dialog to save the file at a new path or name." }
    member val SaveIncrementing  = {name= "Save Incrementing"         ;gesture= ""               ;cmd= mkCmdSimple (fun _ -> tabs.SaveIncremental(tabs.Current) |> ignore)       ;tip= "Save with increased last character of filename.\r\nCan be alphabetic or numeric ( e.g.  d->e or 5->6).\r\nDoes not overwrite any existing file."}
    member val SaveAll           = {name= "Save All"                 ;gesture= "Ctrl + Shift + S";cmd= mkCmdSimple (fun _ -> for t in tabs.AllTabs do tabs.Save(t) |> ignore)    ;tip= "Saves all tabs. Shows a dialog only if the open file does not exist on disk."    }
    member val Close             = {name= "Close File"                ;gesture= "Ctrl + F4"      ;cmd= mkCmdSimple (fun _ -> tabs.CloseTab(tabs.Current))                        ;tip= "Closes the current tab, if there is only one tab then the window will be closed."}
    member val SaveLog           = {name= "Save Text in Log"          ;gesture= ""               ;cmd= mkCmdSimple (fun _ -> log.SaveAllText(tabs.Current.FilePath))             ;tip= "Save all text from Log Window."                                                  }
    member val SaveLogSel        = {name= "Save Selected Text in Log" ;gesture= ""               ;cmd= mkCmd isLse (fun _ -> log.SaveSelectedText(tabs.Current.FilePath))        ;tip= "Save selected text from Log Window."                                             }
                                    
                                               
    //Edit menu:                                                                                                                              
    member val Comment           = {name= "Comment"                   ;gesture= "Ctrl + K"       ;cmd= mkCmdSimple (fun _ -> Commenting.comment tabs.CurrAvaEdit)                ;tip= "Removes '//' at the beginning of current line, \r\nor from all line touched by current selection"   }
    member val UnComment         = {name= "Uncomment"                 ;gesture= "Ctrl + U"       ;cmd= mkCmdSimple (fun _ -> Commenting.unComment tabs.CurrAvaEdit)              ;tip= "Puts '//' at the beginning of current line, \r\nor all line touched by current selection"           }                                                                                     
    member val ToggleComment     = {name= "Toggle Comment"            ;gesture= "Ctrl + T"       ;cmd= mkCmdSimple (fun _ -> Commenting.toggleComment tabs.CurrAvaEdit)          ;tip= "Swaps commented and not commented lines"           }                                                                                     
    
    member val SwapLineUp        = {name= "Swap Line Up"              ;gesture= "Alt + Up"       ;cmd=AvalonEditCommands.SwapLinesUp                                             ;tip= "Swap the current line and the previous line."                                                       }                                                                                     
    member val SwapLineDown      = {name= "Swap Line Down"            ;gesture= "Alt + Down"     ;cmd=AvalonEditCommands.SwapLinesDown                                           ;tip= "Swap the current line and the next line."                                                           } 
    member val ToUppercase       = {name= "To UPPERCASE"              ;gesture= ""               ;cmd=AvalonEditCommands.ConvertToUppercase                                      ;tip= "Convertes the selected text to UPPERCASE."                                                          }     
    member val Tolowercase       = {name= "To lowercase"              ;gesture= ""               ;cmd=AvalonEditCommands.ConvertToLowercase                                      ;tip= "Convertes the selected text to lowercase."                                                          }     
    member val ToggleBoolean     = {name= "Toggle bool literals"      ;gesture= "Ctrl + B"       ;cmd = mkCmdSimple (fun _ -> CursorBehaviour.toggleBoolean(tabs.CurrAvaEdit) )  ;tip= "Convertes a 'true' literal to 'false' and a 'false' literal to 'true' if they are currently selected exclusively"                                                          }     

                                                                                                                                             
    //Select menu:                                                                                                                               
    member val SelectLine        = {name= "Select Current Line"       ;gesture= "Ctrl  + L"      ;cmd= mkCmdSimple (fun _ -> expandSelectionToFullLines(tabs.CurrAvaEdit) |> ignore )  ;tip= "Select current line"} // TODO compare VSCODE shortcuts to  see https://github.com/icsharpcode/SharpDevelop/wiki/Keyboard-Shortcuts
    //member val SelectLinesUp      ={name= "Select Lines Upwards"      ;gesture= "Shift + Up"     ;cmd= mkCmdSimple (fun _ ->                                                           ;tip= "Not implemented yet"}
    //member val SelectLinesDown    ={name= "Select Lines Downwards"    ;gesture= "Shift + Down"   ;cmd= mkCmdSimple (fun _ ->                                                           ;tip= "Not implemented yet"} //TODO!   
                                                                                                                                         
    //FSI menu:                                                                                                                              
    member val RunAllText        = {name= "Run All Text"              ;gesture= "F5"             ;cmd= mkCmdSimple (fun _ -> evalAllText() )             ;tip= "Send all text in the current file to FSharp Interactive"                }
    member val RunAllTextSave    = {name= "Save and Run All Text"     ;gesture= "F6"             ;cmd= mkCmdSimple (fun _ -> evalAllTextSave())          ;tip= "First Save current File, then send all it's text to FSharp Interactive" }
    member val RunAllTxSaveClear = {name= "Save, Clear Log, Run All Text" ;gesture= "F7"         ;cmd= mkCmdSimple (fun _ -> evalAllTextSaveClear())     ;tip= "First Save current File, then Clear Log, then send all text to FSharp Interactive" }  
    member val RunCurrentLines   = {name= "Run CurrentLines"          ;gesture= "Ctrl + Enter"   ;cmd= mkCmdSimple (fun _ -> evalSelectedLines())        ;tip= "Sends the currently seleceted Lines in the editor to FSharp Interactive.\r\nIncludes partially selected lines in full."}
    member val RunSelectedText   = {name= "Run Selected Text"         ;gesture= "Alt + Enter"    ;cmd= mkCmd isEse (fun _ -> evalSelectedText())         ;tip= "Sends the currently seleceted Text in the editor to FSharp Interactive"     }// TODO mark evaluated code with grey background
    member val RunTextTillCursor = {name= "Run Text till Cursor"      ;gesture= "F3"             ;cmd= mkCmdSimple (fun _ -> evalTillCursor())           ;tip= "Sends all lines till and including the current line  to FSharp Interactive" }
    member val RunTextFromCursor = {name= "Run Text from Cursor"      ;gesture= "F4"             ;cmd= mkCmdSimple (fun _ -> evalFromCursor())           ;tip= "Sends all lines from and including the current line  to FSharp Interactive" }    
    member val ClearLog          = {name= "Clear Log"                 ;gesture= "Ctrl + Alt + C" ;cmd= mkCmdSimple (fun _ -> log.Clear())                ;tip= "Clear all text from FSI Log window"                                         }
    member val CancelFSI         = {name= "Cancel FSI"                ;gesture= "Ctrl + Break"   ;cmd= mkCmd isAsy (fun _ -> fsi.CancelIfAsync())        ;tip= "Cancel running FSI evaluation (only available in asynchronous mode)"        }
    member val ResetFSI          = {name= "Reset FSI"                 ;gesture= "Ctrl + Alt + R" ;cmd= mkCmdSimple (fun _ -> fsi.Reset())                ;tip= "Reset FSharp Interactive"                                                   }
    member val ToggleSync        = {name= "Toggle Sync / Async"       ;gesture= ""               ;cmd= mkCmdSimple (fun _ -> fsi.ToggleSync())           ;tip= "Switch between synchronous and asynchronous evaluation in FSI, see status in StatusBar"} 
                                                                                                                                       
   //View menu:                                                                                                                        
    member val ToggleSplit       = {name= "Toggle Window Split"       ;gesture= ""       ;cmd= mkCmdSimple (fun _ -> grid.ToggleSplit())         ;tip= "Toggle between vertical and horizontal window arrangement of Editor and Log View"              }
    member val ToggleLogSize     = {name= "Toggle Log Maximased"      ;gesture= "F11"            ;cmd= mkCmdSimple (fun _ -> grid.ToggleMaxLog())        ;tip= "Maximises or resets the size of the Log window. \r\n(depending on current state)"    }
    member val ToggleLogLineWrap = {name= "Toggle Line Wraping in Log";gesture= "Alt + Z"        ;cmd= mkCmdSimple (fun _ -> log.ToggleLineWrap(config)) ;tip= "Toggle Line Wraping in Log window"                                                  }  
    member val FontBigger        = {name= "Make Font Bigger"          ;gesture= "Ctrl + '+'"     ;cmd= mkCmdSimple (fun _ -> fonts.FontsBigger())         ;tip= "Increase Text Size for both Editor and Log"                                        }
    member val FontSmaller       = {name= "Make Font Smaller"         ;gesture= "Ctrl + '-'"     ;cmd= mkCmdSimple (fun _ -> fonts.FontsSmaller())        ;tip= "Decrease Text Size for both Editor and Log"                                        }
    member val CollapseCode      = {name= "Collapse all Code Foldings" ;gesture= ""               ;cmd= mkCmdSimple (fun _ -> tabs.Current.Editor.Folds.CollapseAll()) ;tip= "Collapse all Code Foldings in this file"                                 }
    member val ExpandCode        = {name= "Expand all Code Foldings"  ;gesture= ""               ;cmd= mkCmdSimple (fun _ -> tabs.Current.Editor.Folds.ExpandAll() ) ;tip= "Expand or unfold all Code Foldings in this file"                        }
                                                                                                                                     
    //Settings Menu                                                                                                                      
    member val SettingsFolder    = {name= "Open Settings Folder"      ;gesture= ""               ;cmd= mkCmdSimple (fun _ -> config.Hosting.OpenSettingsFolder())                         ;tip= "Opens the Folder where user settinsg such as default file content is saved."                                        }
    member val ReloadXshdFile    = {name= "Reload Xshd File"          ;gesture= "F10"            ;cmd= mkCmdSimple (fun _ -> SyntaxHighlighting.setFSharp(tabs.CurrAvaEdit,config,true))  ;tip= "Reloads FSharpSynatxHighlighterExtended.xshd, this is useful for testing new highlighting files without a restart." }

    //--------------------------
    // Built in Commands from Avalonedit (listed as functiosn so the can be created more than once( eg for menu; and context menu)
    //----------------------------
    member val Copy      ={name= "Copy"     ;gesture=  "Ctrl + C"     ;cmd= ApplicationCommands.Copy   ; tip= "Copy selected text\r\nOr full current line if nothing is selceted." }
    member val Cut       ={name= "Cut"      ;gesture=  "Ctrl + X"     ;cmd= ApplicationCommands.Cut    ; tip= "Cut selected text\r\nOr full current line if nothing is selceted."  }                       
    member val Paste     ={name= "Paste"    ;gesture=  "Ctrl + V"     ;cmd= ApplicationCommands.Paste  ; tip= "Insert text from Clipboard."                                        }                     
    member val UnDo      ={name= "UnDo"     ;gesture=  "Ctrl + Z"     ;cmd= ApplicationCommands.Undo   ; tip= "Undo last edit."                                                    }                      
    member val ReDo      ={name= "ReDo"     ;gesture=  "Ctrl + Y"     ;cmd= ApplicationCommands.Redo   ; tip= "Undo last undo."                                                    }                      
    member val Find      ={name= "Find"     ;gesture=  "Ctrl + F"     ;cmd= ApplicationCommands.Find   ; tip= "Find text of current selection."                                    }                      
    member val Replace   ={name= "Replace"  ;gesture=  "Ctrl + H"     ;cmd= ApplicationCommands.Replace; tip= "Find and replace text of current selection."                        }   
    
    member val BoxSelLeftByCharacter  ={name= "Box Select Left By Character"  ;gesture=  "Alt + Shift + Left"       ;cmd= RectangleSelection.BoxSelectLeftByCharacter  ; tip= "Expands the selection left by one character; creating a rectangular selection."     }  
    member val BoxSelRightByCharacter ={name= "Box Select Right By Character" ;gesture= "Alt + Shift + Right"       ;cmd= RectangleSelection.BoxSelectRightByCharacter ; tip= "Expands the selection right by one character; creating a rectangular selection."    }  
    member val BoxSelLeftByWord       ={name= "Box Select Left By Word"       ;gesture= "Ctrl + Alt + Shift + Left" ;cmd= RectangleSelection.BoxSelectLeftByWord       ; tip= "Expands the selection left by one word; creating a rectangular selection."          }        
    member val BoxSelRightByWord      ={name= "Box Select Right By Word"      ;gesture= "Ctrl + Alt + Shift + Right";cmd= RectangleSelection.BoxSelectRightByWord      ; tip= "Expands the selection right by one word; creating a rectangular selection."         }       
    member val BoxSelUpByLine         ={name= "Box Select Up By Line"         ;gesture= "Alt + Shift + Up"          ;cmd= RectangleSelection.BoxSelectUpByLine         ; tip= "Expands the selection up by one line; creating a rectangular selection."            }          
    member val BoxSelDownByLine       ={name= "Box Select Down By Line"       ;gesture= "Alt + Shift + Down"        ;cmd= RectangleSelection.BoxSelectDownByLine       ; tip= "Expands the selection down by one line; creating a rectangular selection."          }        
    member val BoxSelToLineStart      ={name= "Box Select To Line Start"      ;gesture= "Alt + Shift + Home"        ;cmd= RectangleSelection.BoxSelectToLineStart      ; tip= "Expands the selection to the start of the line; creating a rectangular selection."  }       
    member val BoxSelToLineEnd        ={name= "Box Select To Line End"        ;gesture= "Alt + Shift + End"         ;cmd= RectangleSelection.BoxSelectToLineEnd        ; tip= "Expands the selection to the end of the line; creating a rectangular selection."    }  

   // TODO add  all built in  DocmentNavigatin shortcuts
   
    member this.Fonts = fonts

    /// exluding the ones already provided by avalonedit
    member this.SetUpGestureInputBindings () =         
            
            let allCustomCommands = [  //for seting up Key gestures below, exluding the ones already provided by avalonedit
                 this.NewTab           
                 this.OpenFile         
                 this.OpenTemplateFile 
                 this.Save
                 this.Export
                 this.SaveAs           
                 this.SaveIncrementing 
                 this.SaveAll          
                 this.Close            
                 this.SaveLog          
                 this.SaveLogSel
             
                 this.Comment          
                 this.UnComment
                 this.ToggleComment
                 this.ToggleBoolean

                 this.SelectLine       
                 //this.SelectLinesUp  
                 //this.SelectLinesDown
                            
                 this.RunAllText       
                 this.RunAllTextSave 
                 this.RunAllTxSaveClear
                 this.RunCurrentLines 
                 this.RunSelectedText 
                 this.RunTextTillCursor
                 this.RunTextFromCursor
                 this.ClearLog        
                 this.CancelFSI        
                 this.ResetFSI         
                 if config.Hosting.IsHosted then this.ToggleSync
             
                 this.ToggleSplit      
                 this.ToggleLogSize    
                 this.ToggleLogLineWrap
                 this.FontBigger       
                 this.FontSmaller
                                       
                 this.SettingsFolder   
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
                | x -> match Key.TryParse(x,true) with
                       |true, k -> k
                       | _ -> log.PrintAppErrorMsg "*AllInputBindings: failed to parse cmd Key '%A'" x; Key.NoName
        
            let getModKey = function
                |"Ctrl"     -> ModifierKeys.Control        
                | x -> match ModifierKeys.TryParse(x,true) with
                       |true, k -> k
                       | _ -> log.PrintAppErrorMsg "*AllInputBindings: failed to parse ModifierKey '%A'" x; ModifierKeys.None
        
        
            try
                let bindings = 
                    [|  for cmd in allCustomCommands do
                            match cmd.gesture.Trim() with
                            |"" -> ()
                            | "Ctrl + '+'" | "Ctrl + +" | "Ctrl +" -> // because  gg.Split('+') would fail
                                yield InputBinding(cmd.cmd,  KeyGesture(Key.Add,ModifierKeys.Control)) 
                            | gg -> 
                                yield
                                    match gg.Split('+') |> Array.map ( fun k -> k.Trim()) with
                                    | [| m1; m2; k |]   -> InputBinding(cmd.cmd,  KeyGesture(getKey k, getModKey m1 + getModKey m2))
                                    | [| m; k |]        -> InputBinding(cmd.cmd,  KeyGesture(getKey k, getModKey m))
                                    | [| k |]           -> InputBinding(cmd.cmd,  KeyGesture(getKey k))
                                    | _ -> 
                                        log.PrintAppErrorMsg "*SetUpGestureInputBindings: failed to parse cmd Input gesture '%s'" cmd.gesture
                                        InputBinding(cmd.cmd,  KeyGesture(Key.None))
                    |]
                grid.Window.Window.InputBindings.AddRange (bindings)
                // to no redirect alt to the menu bar :
                grid.Tabs.Control.InputBindings.Add (InputBinding(this.RunSelectedText.cmd, KeyGesture(Key.Return , ModifierKeys.Alt))) |> ignore 
                grid.Tabs.Control.InputBindings.Add (InputBinding(this.RunSelectedText.cmd, KeyGesture(Key.Enter  , ModifierKeys.Alt))) |> ignore 
            with e -> 
                log.PrintAppErrorMsg "*AllInputBindings: failed to create keyboard shortcuts: %A"e
         
    

        