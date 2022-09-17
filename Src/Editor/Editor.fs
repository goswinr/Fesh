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
open Seff.Model
open Seff.Config
open Seff.Util.Str

 /// The tab that holds the tab header and the code editor
type Editor private (code:string, config:Config, filePath:FilePath)  = 
    let avaEdit = new TextEditor()
    let id = Guid.NewGuid()
    let log = config.Log

    let checker =           Checker.GetOrCreate(config)

    let folds =             new Foldings(avaEdit,checker, config, id)
    let evalTracker      =  new EvaluationTracker(avaEdit,checker, id)
    let errorHighlighter =  new ErrorHighlighter(avaEdit,folds.Manager, log)

    let search =            Search.SearchPanel.Install(avaEdit)

    let compls =            new Completions(avaEdit,config,checker)
    let rulers =            new ColumnRulers(avaEdit, log) // do foldings first
    //let selText =           SelectedTextTracer.Setup(this,folds,config) // moved to: static member SetUp(..)


    let mutable checkState = FileCheckState.NotStarted // local to this editor
    let mutable filePath = filePath

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
        //avaEdit.Options.EnableVirtualSpace <- true // to postion caret anywhere in editor
        avaEdit.TextArea.SelectionCornerRadius <- 0.0
        avaEdit.TextArea.SelectionBorder <- null
        avaEdit.FontFamily <- Style.fontEditor
        avaEdit.FontSize <- config.Settings.GetFloat("FontSize", Seff.Style.fontSize) // TODO odd sizes like  17.0252982466288  makes block selection delete fail on the last line
        avaEdit.AllowDrop <- true
        //avaEdit.TextArea.TextView.CurrentLineBackground <- Brushes.Ivory |> Brush.brighter 10 |> Brush.freeze
        //avaEdit.TextArea.TextView.CurrentLineBorder <- new Pen(Brushes.Gainsboro|> Brush.freeze, 2.0) |> Util.Pen.freeze

        //avaEdit.TextArea.AllowCaretOutsideSelection <- true
        SyntaxHighlighting.setFSharp(avaEdit,false)

        search.MatchCase  <- true //config.Settings.GetBool("SearchMatchCase", true) // TODO how to save changes ?
        search.WholeWords <- true //config.Settings.GetBool("SearchWholeWords", true)
        


    member val IsCurrent = false with get,set //  this is managed in Tabs.selectionChanged event handler

    member val TypeInfoTip = new Controls.ToolTip(IsOpen=false)

    // all instances of Editor refer to the same checker instance
    member this.GlobalChecker = checker

    member this.ErrorHighlighter = errorHighlighter
    member this.EvalTracker = evalTracker

    member this.Completions = compls
    member this.Config = config

    member this.Folds = folds
    member this.Search = search


    member this.SetFilePathMustBeInSyncWithTabsPath(v)= filePath <- v // only the Tab class containing this editor takes care of updating this

    // IEditor members:

    member this.Id              = id
    member this.AvaEdit         = avaEdit
    ///This CheckState is local to the current editor
    member this.FileCheckState  with get() = checkState    and  set(v) = checkState <- v
    member this.FilePath        with get() = filePath    
    member this.Log = log
    member this.IsComplWinOpen  = compls.IsOpen

    member this.EvaluateFrom    = evalTracker.EvaluateFrom

    interface IEditor with
        member this.Id              = id
        member this.AvaEdit         = avaEdit
        member this.FileCheckState  with get() = checkState and  set(v) = checkState <- v
        member this.FilePath        = filePath // interface does not need setter
        member this.Log             = log
        member this.FoldingManager  = folds.Manager
        member this.EvaluateFrom    = evalTracker.EvaluateFrom
        member this.IsComplWinOpen  = compls.IsOpen

    // additional text change event:
    //let completionInserted = new Event<string>() // event needed because Text change event is not raised after completion insert
    //[<CLIEvent>]
    //member this.CompletionInserted = completionInserted.Publish
    //member this.TriggerCompletionInserted x = completionInserted.Trigger x // to raise it after completion inserted ?

    /// sets up Text change event handlers
    /// a static method so that an instance if IEditor can be used
    static member SetUp  (code:string, config:Config, filePath:FilePath ) = 
        let ed = Editor(code, config, filePath )
        let avaEdit = ed.AvaEdit
        let compls = ed.Completions
        let log = ed.Log

        SelectedTextTracer.Setup(ed, config)
        BracketHighlighter.Setup(ed, ed.GlobalChecker)

        Logging.LogAction <- new Action<string>( fun (s:string) -> log.PrintfnDebugMsg "Logging.Log: %s" s)       
        

        avaEdit.Drop.Add                      (fun e -> CursorBehavior.TextAreaDragAndDrop( avaEdit,e))
        avaEdit.PreviewKeyDown.Add            (fun e -> KeyboardShortcuts.previewKeyDown(    avaEdit, e, compls))   //to indent and dedent, and change block selection delete behavior
        avaEdit.TextArea.PreviewTextInput.Add (fun e -> CursorBehavior.previewTextInput(    avaEdit, e))   //to change block selection delete behavior
        avaEdit.TextArea.AlternativeRectangularPaste <- Action<string,bool>( fun txt txtIsFromOtherRectSel -> RectangleSelection.paste(ed.AvaEdit,txt,txtIsFromOtherRectSel)) //TODO check txtIsFromOtherRectSel on pasting text with \r\n

        // setup and tracking folding status, (needs a ref to file path:  )
        ed.Folds.InitState( ed )
        ed.Folds.Margin.MouseUp.Add (fun e -> config.FoldingStatus.Set(ed) )

        //----------------------------------
        //--FS Checker and Code completion--
        //----------------------------------

        // Evaluation Tracker:
        // or use avaEdit.Document.Changing event ??

        //avaEdit.Document.Changed.Add(fun a -> ISeffLog.log.PrintfnColor 100 222 160 "Document.Changed:\r\n'%s'" avaEdit.Text)
        avaEdit.Document.Changed.Add(fun a -> ed.EvalTracker.SetLastChangeAt(a.Offset))
        avaEdit.Document.Changed.Add(fun a -> 
            match DocChanged.docChanged(a,ed,compls) with // the trigger for Autocomplete
            |DocChanged.DoNothing ->()
            |DocChanged.CheckCode -> ed.GlobalChecker.CheckThenHighlightAndFold(ed)
            )        

        // check if closing and inserting from completion window is desired now:
        avaEdit.TextArea.TextEntering.Add (DocChanged.closeAndMaybeInsertFromCompletionWindow compls)

        ed.GlobalChecker.OnCheckedForErrors.Add(fun iEditorOfCheck -> // this then triggers folding too, statusbar update is added in statusbar class
            if iEditorOfCheck.Id = ed.Id then // make sure it draws only on one editor, not all!
                ed.ErrorHighlighter.Draw(ed)
            )

        compls.OnShowing.Add(fun _ -> ed.ErrorHighlighter.ToolTip.IsOpen <- false)
        compls.OnShowing.Add(fun _ -> ed.TypeInfoTip.IsOpen              <- false)

        // Mouse Hover:
        avaEdit.TextArea.TextView.MouseHover.Add(fun e -> TypeInfo.mouseHover(e, ed, log, ed.TypeInfoTip))
        avaEdit.TextArea.TextView.MouseHoverStopped.Add(fun _ -> ed.TypeInfoTip.IsOpen <- false )
        avaEdit.TextArea.TextEntering.Add (fun _ -> ed.TypeInfoTip.IsOpen <- false )// close type info on typing

        ed


    ///additional constructor using default code
    static member New (config:Config) =  Editor.SetUp( config.DefaultCode.Get() , config, NotSet)
