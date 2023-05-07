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
open AvalonEditB.Rendering
open System.Threading

/// returns a bigger integer on each access for naming unsaved files
type Counter private () = 
    static let unsavedFile = ref 0

    /// Returns a bigger integer on each access
    /// used to give each unsaved file a unique number
    static member UnsavedFileName() = 
        incr unsavedFile
        sprintf "*unsaved-%d*" !unsavedFile


 /// The tab that holds the tab header and the code editor
type Editor private (code:string, config:Config, initalFilePath:FilePath)  = 
    let avaEdit = 
        let av = TextEditor()
        av.Options.IndentationSize <- config.Settings.GetIntSaveDefault("IndentationSize", 4) // do first because its used by tabs to spaces below.
        av.Text <- code |> unifyLineEndings |> tabsToSpaces av.Options.IndentationSize

        av.BorderThickness <- new Thickness( 0.0)
        av.ShowLineNumbers <- true // background color is set in ColumnRulers.cs
        av.VerticalScrollBarVisibility <- Controls.ScrollBarVisibility.Auto
        av.HorizontalScrollBarVisibility <- Controls.ScrollBarVisibility.Auto
        av.Options.HighlightCurrentLine <- true // http://stackoverflow.com/questions/5072761/avalonedit-highlight-current-line-even-when-not-focused
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
        av.FontSize <- config.Settings.GetFloat("SizeOfFont", StyleState.fontSize) // TODO odd sizes like  17.0252982466288  makes block selection delete fail on the last line
        av.AllowDrop <- true
        //avaEdit.TextArea.TextView.CurrentLineBackground <- Brushes.Ivory |> Brush.brighter 10 |> Brush.freeze
        //avaEdit.TextArea.TextView.CurrentLineBorder <- new Pen(Brushes.Gainsboro|> Brush.freeze, 2.0) |> Util.Pen.freeze
        //avaEdit.TextArea.AllowCaretOutsideSelection <- true
        //avaEdit.Options.EnableVirtualSpace <- true // to postion caret anywhere in editor
        av

    let search =            
        let se = Search.SearchPanel.Install(avaEdit)
        se.MarkerCornerRadius <- 0.
        se.MatchCase  <- true  // config.Settings.GetBool("SearchMatchCase", true) // TODO how to save changes ?
        se.WholeWords <- false // config.Settings.GetBool("SearchWholeWords", false)
        se

    //let id = Guid.NewGuid() // DELETE   
    let mutable checkState = FileCheckState.Checking //.NotStarted // local to this editor
    let mutable filePath   = initalFilePath  
    
    let getFilePath() = filePath
    let state = new InteractionState(config)
    let folds = new Foldings(avaEdit, state, getFilePath)
     

    //let checker             = Checker.GetOrCreate(config)  // DELETE
    
    let evalTracker         = new EvaluationTracker(avaEdit, checker, id)
    let errorHighlighter    = new ErrorHighlighter(avaEdit, folds.Manager)
    let semanticHighlighter = SemanticHighlighting.setup(avaEdit, id, checker)
    let compls              = new Completions(avaEdit, config, checker)    

    do               
        SyntaxHighlighting.setFSharp(avaEdit,false) 
        
    member _.State = state    

    //member val IsCurrent = false with get,set //  this is managed in Tabs.selectionChanged event handler

    member val TypeInfoTip = new Controls.ToolTip(IsOpen=false)    
    
    //member val SemanticRanges : SemanticClassificationItem [] = [| |] with get,set

    member val CodeAtLastSave : string = "" with get,set // used to check if file was changed in the background by other apps in FileChangeTracker
   
    // all instances of Editor refer to the same checker instance
    //member this.GlobalChecker = checker  // DELETE

    member this.ErrorHighlighter = errorHighlighter

    member this.EvalTracker = evalTracker

    member this.Completions = compls

    //member this.Config = config

    member this.Folds = folds

    member this.Search = search    
    
    /// This function will be set below in SetUp static member of Editor.
    /// It is used to highlight text in the editor , for example to match the current selection in Log.
    member val HighlightText = fun (t:string) -> () with get, set 

    // IEditor members:
    //member this.Id              = id    
    member this.AvaEdit         = avaEdit
    
    /// This CheckState is local to the current editor
    member this.FileCheckState  with get() = checkState  and  set(v) = checkState <- v
    
    /// setting this alone does not change the tab header !!
    member this.FilePath        with get() = filePath    and set (v)= filePath <- v
    
    //member this.Log = config.Log   
    member this.IsComplWinOpen  = compls.IsOpen
    member this.EvaluateFrom    = evalTracker.EvaluateFrom

    interface IEditor with
        //member _.Id              = id  // DELETE
        member _.AvaEdit         = avaEdit
        member _.FileCheckState  with get() = checkState and  set(v) = checkState <- v
        member _.FilePath        = filePath // the interface is get only, it does not need a setter
        //member _.Log             = config.Log // DELETE
        member _.FoldingManager  = folds.Manager
        member _.EvaluateFrom    = evalTracker.EvaluateFrom
        member _.IsComplWinOpen  = compls.IsOpen        
        //member _.SemanticRanges  = semanticHighlighter.Ranges  // DELETE
        //member _.Completions     = compls :> obj

    /// sets up Text change event handlers
    /// a static method so that an instance if IEditor can be used
    static member SetUp  (code:string, config:Config, filePath:FilePath ) = 
        let ed = Editor(code, config, filePath )
        let avaEdit = ed.AvaEdit
        let compls = ed.Completions 
        
        
        

        let editorServices = {
            folds           = folds
            evalTracker     : EvaluationTracker
            errorHili       : ErrorHighlighter
            //semanticHili    : SemanticHighlighter
            //selectionHili   : SelectionHighlighter
            compls          : Completions  
            }


        
        ed.HighlightText <- SelectionHighlighting.HiEditor.setup(ed)        
        BracketHighlighter.Setup(ed, ed.GlobalChecker) 
        
        // for logging Debug and Error Messages in AvalonEditB
        Logging.LogAction <- new Action<string>( fun (s:string) -> ISeffLog.log.PrintfnDebugMsg "AvalonEditB Logging.Log: %s" s)

        avaEdit.Drop.Add                      (fun e -> DragAndDrop.onTextArea(  avaEdit, e))
        avaEdit.PreviewKeyDown.Add            (fun e -> KeyboardShortcuts.previewKeyDown(    ed     , e))  // A single Key event arg, indent and dedent, and change block selection delete behavior
        avaEdit.TextArea.PreviewTextInput.Add (fun e -> CursorBehavior.previewTextInput(     avaEdit, e))  // A TextCompositionEventArgs that has a string , handling typing in rectangular selection
        avaEdit.TextArea.AlternativeRectangularPaste <- Action<string,bool>( fun txt txtIsFromOtherRectSel -> RectangleSelection.paste(ed.AvaEdit, txt, txtIsFromOtherRectSel)) //TODO check txtIsFromOtherRectSel on pasting text with \r\n

        // setup and tracking folding status, (needs a ref to file path:  )
        ed.Folds.InitState( ed )
        ed.Folds.Margin.MouseUp.Add (fun e -> 
            ed.Folds.UpdateCollapseStatus()
            config.FoldingStatus.Set(ed) )
        
        avaEdit.TextArea.TextView.LineTransformers.Add(new NonStandardIndentColorizer(ed.Folds.BadIndentations))        
        

        let rulers =  new ColumnRulers(avaEdit) // draw last , so on top? do foldings first

        //----------------------------------
        //--FS Checker and Code completion--
        //----------------------------------    

        
        avaEdit.Document.Changing.Add(DocChangeEvents.changing state.FastColorizer)
        avaEdit.Document.Changed.Add (DocChangeEvents.changed  ed state)
        avaEdit.Document.Changed.Add(fun a -> ed.EvalTracker.SetLastChangeAt a.Offset)
                 
        // check if closing and inserting from completion window is desired with currently typed character:
        avaEdit.TextArea.TextEntering.Add (compls.MaybeInsertOrClose)
        avaEdit.TextArea.TextEntering.Add (fun _ -> ed.TypeInfoTip.IsOpen <- false )// close type info on typing
        
        (*  // DELETE
        avaEdit.Document.Changed.Add(fun a -> 
            DocChanged.logPerformance( a.InsertedText.Text) // AutoHotKey SendInput of ßabcdefghijklmnopqrstuvwxyz£
            //DocChanged.delayDocChange(a, ed, compls, ed.GlobalChecker) // to trigger for Autocomplete or error highlighting with immediate delay, (instead of delay in checkCode function.)
            DocChanged.docChanged(a, ed, compls, ed.GlobalChecker)
            ed.EvalTracker.SetLastChangeAt(a.Offset)
            )                           

        // check if closing and inserting from completion window is desired with currently typed character:
        avaEdit.TextArea.TextEntering.Add (DocChanged.closeAndMaybeInsertFromCompletionWindow compls)
        avaEdit.TextArea.TextEntering.Add (fun _ -> ed.TypeInfoTip.IsOpen <- false )// close type info on typing

        ed.GlobalChecker.OnCheckedForErrors.Add(fun (iEditorOfCheck,chRes) -> // this then triggers folding too, statusbar update is added in statusbar class
            if iEditorOfCheck.Id = ed.Id then // make sure it draws only on one editor, not all!
                AutoFixErrors.references(iEditorOfCheck, chRes)
                ed.ErrorHighlighter.Draw(ed)
            )
        *)

        compls.OnShowing.Add(fun _ -> ed.ErrorHighlighter.ToolTip.IsOpen <- false)
        compls.OnShowing.Add(fun _ -> ed.TypeInfoTip.IsOpen              <- false)
        ed.TypeInfoTip.SetValue(Controls.ToolTipService.InitialShowDelayProperty, 50) // this delay is also set in Initialize.fs

        
        // Mouse Hover:
        avaEdit.TextArea.TextView.MouseHover.Add(fun e -> TypeInfo.mouseHover(e, ed, ed.TypeInfoTip))
        avaEdit.TextArea.TextView.MouseHoverStopped.Add(fun _ -> ed.TypeInfoTip.IsOpen <- false )

        ed

    ///additional constructor using default code
    static member New (config:Config) =  
        let dummyName = Counter.UnsavedFileName()
        Editor.SetUp( config.DefaultCode.Get() , config, NotSet dummyName)


    (* https://github.com/icsharpcode/AvalonEdit/blob/master/ICSharpCode.AvalonEdit.Sample/document.html

        Change Events:
            Here is the order in which events are raised during a document update:
            BeginUpdate()

            UpdateStarted event is raised
            Insert() / Remove() / Replace()

            Changing event is raised
            The document is changed
            TextAnchor.Deleted events are raised if anchors were in the deleted text portion
            Changed event is raised
            EndUpdate()

            TextChanged event is raised
            TextLengthChanged event is raised
            LineCountChanged event is raised
            UpdateFinished event is raised
        If the insert/remove/replace methods are called without a call to BeginUpdate(), they will call BeginUpdate() and EndUpdate() to ensure no change happens outside of UpdateStarted/UpdateFinished.

        There can be multiple document changes between the BeginUpdate() and EndUpdate() calls. In this case, the events associated with EndUpdate will be raised only once after the whole document update is done.

        The UndoStack listens to the UpdateStarted and UpdateFinished events to group all changes into a single undo step.
        *) 