namespace Seff

open System
open System.IO
open ICSharpCode
open System.Windows.Controls
open System.IO
open System.Windows
open System.Windows.Controls
open ICSharpCode.AvalonEdit
open System.Windows.Media
open Seff.Util.WPF
open FSharp.Compiler.SourceCodeServices
 
 /// the tab that holds the editor of a code file, Log window is not part of tab, exists only once
type FsxTab () = 
    inherit TabItem()
    let ed = new AvalonEdit.TextEditor() |> Appearance.setForEditor
    
    member val Editor = ed with get
    member val HeaderTextBlock:TextBlock = null with get,set  /// used for marking unsaved tab with a star *, to be sure compare code with tab.CodeAtLastSave 
    member val CompletionWin : AvalonEdit.CodeCompletion.CompletionWindow option = None with get,set
    member val CodeAtLastSave = "" with get,set // TODO use editor.IsModified instead !!
    member val FileInfo: FileInfo option = None with get,set
    member val ChangesAreProbalySaved = true with get,set 
        
    member val FsCheckerResult: FSharpCheckFileResults option = None with get,set
    member val FsCheckerRunning = 0 with get,set // each check will get a unique id, used for UI background only?
    
    member val TextMarkerService = new ErrorUI.TextMarkerService(ed) with get
    member val ErrorToolTip =    new ToolTip(IsOpen=false) with get,set 

    member val TypeInfoToolTip = new ToolTip(IsOpen=false) with get,set
    
    member val CompletionWindowJustClosed = false with get,set // for one letter completions to not trigger another completion
    member val CompletionWindowClosed = fun ()->() with get,set //will be set with all the other eventhandlers setup, but ref is needed before
    
    member val FoldingManager : Folding.FoldingManager = null with get,set// set up in Createtab function
    member val Foldings:Option<ResizeArray<int*int>> =  None with get,set

  // additional text change event:
    //let completionInserted = new Event<string>() // event needed because Text change event is not raised after completion insert    
    //[<CLIEvent>]
    //member this.CompletionInserted = completionInserted.Publish
    //member this.TriggerCompletionInserted x = completionInserted.Trigger x // to raise it after completion inserted ?
