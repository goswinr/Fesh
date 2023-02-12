namespace Seff.Editor


open System
open System.Windows
open System.Windows.Media
open System.Windows.Input

open FSharp.Compiler.Tokenization // for keywords

open AvalonEditB
open AvalonEditB.Utils
open AvalonEditB.Document
open AvalonLog

open Seff
open Seff.Editor.SelectionHighlighting
open Seff.Model
open Seff.Config
open Seff.Util.Str
open FSharp.Compiler.EditorServices

 /// The tab that holds the tab header and the code editor
type Editor private (code:string, config:Config, filePath:FilePath)  = 
    let avaEdit = new TextEditor()
    let id = Guid.NewGuid()
    let log = config.Log

    let checker =           Checker.GetOrCreate(config)

    let folds =             new Foldings(avaEdit,checker, config, id)
    let evalTracker      =  new EvaluationTracker(avaEdit,checker, id)
    let errorHighlighter =  new ErrorHighlighter(avaEdit,folds.Manager, log)
    let semanticHighlighter = SemanticHighlighting.setup(avaEdit,id,checker)

    let search =            Search.SearchPanel.Install(avaEdit)

    let compls =            new Completions(avaEdit,config,checker)
    let rulers =            new ColumnRulers(avaEdit, log) // do foldings first
    
    let mutable checkState = FileCheckState.NotStarted // local to this editor
    let mutable filePath   = filePath

    //let mutable needsChecking = true // so that on a tab change a recheck is not triggered if not needed

    do
        avaEdit.BorderThickness <- new Thickness( 0.0)
        avaEdit.Text <- code |> unifyLineEndings |> tabsToSpaces avaEdit.Options.IndentationSize
        avaEdit.ShowLineNumbers <- true // background color is set in ColumnRulers.cs
        avaEdit.VerticalScrollBarVisibility <- Controls.ScrollBarVisibility.Auto
        avaEdit.HorizontalScrollBarVisibility <- Controls.ScrollBarVisibility.Auto
        avaEdit.Options.HighlightCurrentLine <- true // http://stackoverflow.com/questions/5072761/avalonedit-highlight-current-line-even-when-not-focused
        avaEdit.Options.EnableHyperlinks <- true
        avaEdit.TextArea.TextView.LinkTextForegroundBrush <- Brushes.DarkGreen
        avaEdit.Options.EnableTextDragDrop <- true

        avaEdit.Options.ShowSpaces <- false

        avaEdit.Options.ShowTabs <- false // they are always converted to spaces, see above
        avaEdit.Options.ConvertTabsToSpaces <- true
        avaEdit.Options.IndentationSize <- 4
        avaEdit.Options.HideCursorWhileTyping <- false
        avaEdit.TextArea.SelectionCornerRadius <- 0.0
        avaEdit.TextArea.SelectionBorder <- null
        avaEdit.FontFamily <- Style.fontEditor
        avaEdit.FontSize <- config.Settings.GetFloat("FontSize", Seff.Style.fontSize) // TODO odd sizes like  17.0252982466288  makes block selection delete fail on the last line
        avaEdit.AllowDrop <- true
        //avaEdit.TextArea.TextView.CurrentLineBackground <- Brushes.Ivory |> Brush.brighter 10 |> Brush.freeze
        //avaEdit.TextArea.TextView.CurrentLineBorder <- new Pen(Brushes.Gainsboro|> Brush.freeze, 2.0) |> Util.Pen.freeze
        //avaEdit.TextArea.AllowCaretOutsideSelection <- true
        //avaEdit.Options.EnableVirtualSpace <- true // to postion caret anywhere in editor

        //avaEdit.Foreground<-Brushes.HotPink
        SyntaxHighlighting.setFSharp(avaEdit,false)
        search.MarkerCornerRadius <- 0.
        search.MatchCase  <- true //config.Settings.GetBool("SearchMatchCase", true) // TODO how to save changes ?
        search.WholeWords <- false //config.Settings.GetBool("SearchWholeWords", false)

        //avaEdit.TextArea.Document.TextChanged.Add ( fun a -> ISeffLog.printError "Seff TextArea.Document.TextChanged")// debug only
        

    member val IsCurrent = false with get,set //  this is managed in Tabs.selectionChanged event handler

    member val TypeInfoTip = new Controls.ToolTip(IsOpen=false)    
    
    member val SemanticRanges : SemanticClassificationItem [] = [| |] with get,set

    member val CodeAtLastSave : string = "" with get,set // used to check if file was changed in the background by other apps

    // all instances of Editor refer to the same checker instance
    member this.GlobalChecker = checker

    member this.ErrorHighlighter = errorHighlighter

    member this.EvalTracker = evalTracker

    member this.Completions = compls

    member this.Config = config

    member this.Folds = folds

    member this.Search = search
    
    /// Only the Tab class containing this editor takes care of updating this.
    member this.SetFilePathMustBeInSyncWithTabsPath(v) = filePath <- v 

    member val HighlightText = fun (t:string) -> () with get, set // this function will be set below in SetUp static member        

    // IEditor members:

    member this.Id              = id
    
    member this.AvaEdit         = avaEdit
    
    /// This CheckState is local to the current editor
    member this.FileCheckState  with get() = checkState  and  set(v) = checkState <- v
    
    member this.FilePath        with get() = filePath    
    
    member this.Log = log
    
    member this.IsComplWinOpen  = compls.IsOpen

    member this.EvaluateFrom    = evalTracker.EvaluateFrom

    interface IEditor with
        member _.Id              = id
        member _.AvaEdit         = avaEdit
        member _.FileCheckState  with get() = checkState and  set(v) = checkState <- v
        member _.FilePath        = filePath // interface does not need setter
        member _.Log             = log
        member _.FoldingManager  = folds.Manager
        member _.EvaluateFrom    = evalTracker.EvaluateFrom
        member _.IsComplWinOpen  = compls.IsOpen
        member _.SemanticRanges  = semanticHighlighter.Ranges

    /// sets up Text change event handlers
    /// a static method so that an instance if IEditor can be used
    static member SetUp  (code:string, config:Config, filePath:FilePath ) = 
        let ed = Editor(code, config, filePath )
        let avaEdit = ed.AvaEdit
        let compls = ed.Completions
        let log = ed.Log        

        ed.HighlightText <- SelectionHighlighting.HiEditor.setup(ed)        
        //BracketHighlighter.Setup(ed, ed.GlobalChecker) // Disabled! TODO: fix bug first !!!

        Logging.LogAction <- new Action<string>( fun (s:string) -> log.PrintfnDebugMsg "Logging.Log: %s" s)

        avaEdit.Drop.Add                      (fun e -> CursorBehavior.TextAreaDragAndDrop(  avaEdit, e))
        avaEdit.PreviewKeyDown.Add            (fun e -> KeyboardShortcuts.previewKeyDown(    avaEdit, e, compls, ed.Search))   //to indent and dedent, and change block selection delete behavior
        avaEdit.TextArea.PreviewTextInput.Add (fun e -> CursorBehavior.previewTextInput(     avaEdit, e))   //to change block selection delete behavior
        avaEdit.TextArea.AlternativeRectangularPaste <- Action<string,bool>( fun txt txtIsFromOtherRectSel -> RectangleSelection.paste(ed.AvaEdit,txt,txtIsFromOtherRectSel)) //TODO check txtIsFromOtherRectSel on pasting text with \r\n

        // setup and tracking folding status, (needs a ref to file path:  )
        ed.Folds.InitState( ed )
        ed.Folds.Margin.MouseUp.Add (fun e -> config.FoldingStatus.Set(ed) )

        //----------------------------------
        //--FS Checker and Code completion--
        //----------------------------------  

        // or use avaEdit.Document.Changing event ??
        //avaEdit.Document.Changed.Add(fun a -> ISeffLog.log.PrintfnColor 100 222 160 "Document.Changed:\r\n'%s'" avaEdit.Text)        
        avaEdit.Document.Changed.Add(fun a -> 
            DocChanged.delayDocChange(a, ed, compls, ed.GlobalChecker) // to trigger for Autocomplete or error highlighting with immediate delay, (instead of delay in checkCode function.)
            ed.EvalTracker.SetLastChangeAt(a.Offset)
            ) 
                          

        // check if closing and inserting from completion window is desired now:
        avaEdit.TextArea.TextEntering.Add (DocChanged.closeAndMaybeInsertFromCompletionWindow compls)

        ed.GlobalChecker.OnCheckedForErrors.Add(fun (iEditorOfCheck,chRes) -> // this then triggers folding too, statusbar update is added in statusbar class
            if iEditorOfCheck.Id = ed.Id then // make sure it draws only on one editor, not all!
                AutoFixErrors.refrences(iEditorOfCheck, chRes)
                ed.ErrorHighlighter.Draw(ed)
            )

        compls.OnShowing.Add(fun _ -> ed.ErrorHighlighter.ToolTip.IsOpen <- false)
        compls.OnShowing.Add(fun _ -> ed.TypeInfoTip.IsOpen              <- false)
        ed.TypeInfoTip.SetValue(Controls.ToolTipService.InitialShowDelayProperty, 50) // also set in Initialize.fs

        // Mouse Hover:
        avaEdit.TextArea.TextView.MouseHover.Add(fun e -> TypeInfo.mouseHover(e, ed, ed.TypeInfoTip))
        avaEdit.TextArea.TextView.MouseHoverStopped.Add(fun _ -> ed.TypeInfoTip.IsOpen <- false )
        
        avaEdit.TextArea.TextEntering.Add (fun _ -> ed.TypeInfoTip.IsOpen <- false )// close type info on typing

        ed


    ///additional constructor using default code
    static member New (config:Config) =  Editor.SetUp( config.DefaultCode.Get() , config, NotSet)
