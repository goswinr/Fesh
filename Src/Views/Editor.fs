namespace Fesh.Editor


open System
open System.Windows
open System.Windows.Media
open System.Windows.Input

open FSharp.Compiler.Tokenization // for keywords

open AvalonEditB
open AvalonEditB.Utils
open AvalonEditB.Document
open AvalonLog
open Fittings

open Fesh
open Fesh.Model
open Fesh.Config
open Fesh.Util.Str
open FSharp.Compiler.EditorServices
open AvalonEditB.Rendering
open System.Threading
open System.Windows.Media

/// returns a bigger integer on each access for naming unsaved files
type Counter private () =
    static let unsavedFile = ref 0

    /// Returns a bigger integer on each access
    /// used to give each unsaved file a unique number
    static member UnsavedFileName() =
        incr unsavedFile
        sprintf "*unsaved-%d*" !unsavedFile


 /// The tab that holds the tab header and the code editor
type Editor private (code:string, config:Config, initialFilePath:FilePath)  =
    let avaEdit =
        let av = TextEditor()
        av.Options.IndentationSize <- config.Settings.GetInt("IndentationSize", 4) // do first because its used by tabs to spaces below.
        av.Text <- code

        av.BorderThickness <- new Thickness( 0.0)
        av.ShowLineNumbersWithDottedMargin <- false
        av.ShowLineNumbers <- true // background color is set in ColumnRulers.cs
        av.VerticalScrollBarVisibility <- Controls.ScrollBarVisibility.Auto
        av.HorizontalScrollBarVisibility <- Controls.ScrollBarVisibility.Auto
        av.Options.EnableHyperlinks <- true
        av.Options.EnableEmailHyperlinks <- false
        av.TextArea.TextView.LinkTextForegroundBrush <- Brushes.DarkGreen |> AvalonLog.Brush.freeze
        av.Options.EnableTextDragDrop <- true
        av.Options.ShowSpaces <- false
        av.Options.ShowTabs <- false // they are always converted to spaces, see above
        av.Options.ConvertTabsToSpaces <- true
        av.Options.HideCursorWhileTyping <- false
        av.TextArea.SelectionCornerRadius <- 0.0
        av.TextArea.SelectionBorder <- null
        av.FontFamily <- StyleState.fontEditor
        av.FontSize <- config.Settings.GetFloat("SizeOfFont", StyleState.fontSize)
        av.AllowDrop <- true
        av.Options.HighlightCurrentLine <- true // http://stackoverflow.com/questions/5072761/avalonedit-highlight-current-line-even-when-not-focused

        SyntaxHighlighting.setFSharp(av,false)

        av.TextArea.TextView.CurrentLineBackground <- Brushes.LightSteelBlue |> Brush.brighter 70 |> Brush.freeze
        // av.TextArea.TextView.CurrentLineBorder     <- new Pen(Brushes.LightSlateGray|> Brush.freeze, 1.0) |> Util.Pen.freeze

        //av.TextArea.AllowCaretOutsideSelection <- true
        //av.Options.EnableVirtualSpace <- true // to postion caret anywhere in editor
        av

    let search =
        let se = Search.SearchPanel.Install(avaEdit)
        se.MarkerCornerRadius <- 0.
        se.MatchCase  <- true  // config.Settings.GetBool("SearchMatchCase", true) // TODO how to actually save changes ?
        se.WholeWords <- false // config.Settings.GetBool("SearchWholeWords", false)
        se

    let mutable checkState = FileCheckState.NotChecked
    let mutable filePath   = initialFilePath
    let getFilePath() = filePath

    let foldMg      = Folding.FoldingManager.Install(avaEdit.TextArea)

    let state       = new InteractionState(avaEdit, foldMg, config)
    let compls      = new Completions(state)
    let folds       = new Foldings(foldMg, state, getFilePath)
    let brackets    = new BracketHighlighter( state)
    let semHiLi     = new SemanticHighlighter(state)
    let selHiLi     = new SelectionHighlighter(state)
    let evalTracker = new EvaluationTracker(avaEdit,state, config)
    let error       = new ErrorHighlighter(state, foldMg, fun () -> compls.IsOpen )
        // if config.Settings.GetBool(EvaluationTracker.SettingsStr, EvaluationTracker.onByDefault) then Some <| EvaluationTracker(avaEdit,state, config)
        // else None

    let drawServices :Redrawing.DrawingServices = {
        folds       = folds
        compls      = compls
        brackets    = brackets
        errors      = error
        semantic    = semHiLi
        selection   = selHiLi
        evalTracker = evalTracker
        }

    //this will trigger the redraw after all async events have arrived
    let eventCombiner = Redrawing.EventCombiner(drawServices ,state)

    member _.EventCombiner = eventCombiner

    member _.State = state

    member _.DrawingServices = drawServices

    member val TypeInfoTip = new Controls.ToolTip(IsOpen=false)

    member val CodeAtLastSave : string = "" with get,set // used to check if file was changed in the background by other apps in FileChangeTracker

    member _.ErrorHighlighter = error

    member _.SelectionHighlighter = selHiLi

    member _.Completions = compls

    member _.Folds = folds

    member _.Search = search


    // IEditor members:
    member _.AvaEdit = avaEdit

    /// This CheckState is local to the current editor
    member _.FileCheckState with get() = checkState and  set(v) = checkState <- v

    /// setting this alone does not change the tab header !!
    member _.FilePath with get() = filePath and set (v)= filePath <- v

    //member this.Log = config.Log
    member this.IsComplWinOpen  = compls.IsOpen


    interface IEditor with
        member _.AvaEdit         = avaEdit
        member _.FileCheckState  with get() = checkState  and  set(v) = checkState <- v
        member _.FilePath        = filePath // the interface is get only, it does not need a setter
        member _.IsComplWinOpen  = compls.IsOpen
        member _.FoldingManager  = foldMg
        member _.EvaluateFromLine    = evalTracker.EvaluateFromLine

    member this.CloseToolTips() =
        this.TypeInfoTip.IsOpen <- false
        this.DrawingServices.errors.ToolTip.IsOpen <- false

    /// sets up Text change event handlers
    /// a static method so that an instance if IEditor can be used
    static member SetUp  (code:string, config:Config, filePath:FilePath) =
        let ed = Editor(code, config, filePath )
        let avaEdit = ed.AvaEdit
        let compls = ed.Completions

        ed.Folds.Manager.AutoRedrawFoldingSections <- false  // to just redraw the changed line but not the full folding section on changes

        // for logging Debug and Error Messages in AvalonEditB
        Logging.LogAction <- new Action<string>( fun (s:string) -> IFeshLog.log.PrintfnDebugMsg "AvalonEditB Logging.Log: %s" s)

        // ----------------------------------------------------------
        // -------------------------View events ---------------------
        // ----------------------------------------------------------

        let _rulers =  new ColumnRulers(avaEdit) // draw last , so on top? do foldings first
        avaEdit.Loaded.Add (fun _ -> new MagicScrollbar.ScrollBarEnhancer(avaEdit, ed.ErrorHighlighter)  |> ignore )
        avaEdit.Drop.Add   (fun e -> DragAndDrop.onTextArea(avaEdit, e))


        ed.Completions.OnShowing.Add(fun _ ->                         ed.CloseToolTips() )
        avaEdit.TextArea.TextView.VisualLinesChanged.Add (fun _ ->    ed.CloseToolTips() )// close type info on typing
        avaEdit.TextArea.TextView.MouseHoverStopped.Add(fun _ ->      ed.CloseToolTips() )

        ed.Folds.Margin.MouseDown.Add(fun _ -> ed.CloseToolTips(); ed.Completions.CloseAndEnableReacting() ) // close tooltips on clicking in the margin

        // Mouse Hover Type info:
        avaEdit.TextArea.TextView.MouseHover.Add(fun e -> if not ed.IsComplWinOpen then TypeInfo.mouseHover(e, ed, ed.TypeInfoTip))
        ed.TypeInfoTip.SetValue(Controls.ToolTipService.InitialShowDelayProperty, 50) // this delay is also set in Initialize.fs


        // To clear selection highlighter marks first , before opening the search window. if they would be the same as the search word.
        // creating a new command binding for 'ApplicationCommands.Find' would remove the existing one. so we add to the delegate instead
        for binding in avaEdit.TextArea.CommandBindings do
            if  binding.Command = ApplicationCommands.Find    then   binding.Executed.Add(fun _ -> ed.CloseToolTips();ed.SelectionHighlighter.ClearMarksIfOneSelected())
            if  binding.Command = ApplicationCommands.Replace then   binding.Executed.Add(fun _ -> ed.CloseToolTips();ed.SelectionHighlighter.ClearMarksIfOneSelected())

        // ----------------------------------------------------------
        // -------------------------keyboard events -----------------
        // ----------------------------------------------------------

        avaEdit.TextArea.AlternativeRectangularPaste <- Action<string,bool>(fun txt txtIsFromOtherRectSel -> RectangleSelection.paste(ed.AvaEdit, txt, txtIsFromOtherRectSel)) //TODO check txtIsFromOtherRectSel on pasting text with \r\n
        avaEdit.TextArea.PreviewTextInput.Add ( fun e -> CursorBehavior.previewTextInput( avaEdit, e))  // A TextCompositionEventArgs that has a string , handling typing in rectangular selection
        avaEdit.PreviewKeyDown.Add (fun e -> KeyboardShortcuts.previewKeyDown( ed , e))  // A single Key event arg, indent and dedent, and change block selection delete behavior

        // -------------React to doc changes and add Line transformers----------------
        let drawServ = ed.DrawingServices
        // match drawServ.evalTracker with
        // |None -> ()
        // |Some evalTracker -> avaEdit.Document.Changed.Add(  fun a -> evalTracker.SetLastChangeAt (a.Offset-a.RemovalLength))
        avaEdit.PreviewKeyDown.Add ( fun e -> DocChangeEvents.ctrlSpace (ed, drawServ, ed.State, e)) // to trigger completion window on ctrl+space without entering any character
        avaEdit.Document.Changing.Add( fun a -> DocChangeEvents.changing (ed.State, a) )
        avaEdit.Document.Changed.Add ( fun a -> DocChangeEvents.changed  (ed,  drawServ, ed.State, a))


        // Check if closing and inserting from completion window is desired with currently typed character:
        avaEdit.TextArea.TextEntering.Add (compls.MaybeInsertOrClose)

        avaEdit.TextArea.TextView.LineTransformers.Insert(0, ed.State.FastColorizer) // insert at index 0 so that it is drawn first, so that text color is overwritten when selection highlighting happens.
        // avaEdit.TextArea.TextView.LineTransformers.Add(new DebugColorizer(  [| ed.State.TransformersSemantic |], ed.AvaEdit))  // for debugging the line transformers
        // avaEdit.TextArea.TextView.LineTransformers.Add(new DebugColorizer2( [| ed.State.TransformersSemantic |], ed.AvaEdit))  // for debugging the line transformers
        // avaEdit.Document.Changed.Add(fun a -> DocChangeEvents.logPerformance( a.InsertedText.Text)) // AutoHotKey SendInput of ßabcdefghijklmnopqrstuvwxyz£


        // avaEdit.KeyDown.Add (fun k ->  // close tooltips or clear selection on Escape key
        //     match k.Key with
        //     |Key.Escape -> // close ToolTips or if all are closed already  ClearSelectionHighlight
        //         if ed.TypeInfoTip.IsOpen || ed.DrawingServices.errors.ToolTip.IsOpen then ed.CloseToolTips()        //
        //         else ed.SelectionHighlighter.ClearAll()
        //     | _ -> ()  )

        ed

    ///additional constructor using default code
    static member New (config:Config) =
        let dummyName = Counter.UnsavedFileName()
        Editor.SetUp( config.DefaultCode.Get() , config, NotSet dummyName)


