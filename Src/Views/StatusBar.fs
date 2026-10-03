namespace Fesh.Views


open System
open Avalonia
open Avalonia.Media
open Avalonia.Controls.Documents
open Avalonia.Controls
open Avalonia.Controls.Primitives // status bar

open AvaloniaEdit
open AvaloniaLog.ImmBrush

open Fesh
open Fesh.Editor
open Fesh.Model
open Fittings // for TextBlockSelectable
open Fittings.DependencyProps


module MenuUtil =
    open Avalonia.Input
    let menuItem (cmd:CommandInfo) =
        let mi = MenuItem(Header = cmd.name, Command = cmd.cmd) //:> Control
        // mi.InputGesture <- KeyGesture (cmd.gesture)
        mi.ToolTip <- cmd.tip
        mi
        :> Control

open MenuUtil

module StatusbarStyle =

    let errColor =  Brushes.Red      |> brighter 160    // not ErrorStyle.errBackGr
    let warnColor = Brushes.Yellow   |> brighter 40     // not ErrorStyle.warnBackGr

    let textPadding = Thickness(4. , 1. , 4., 1. ) //left ,top, right, bottom)
    let okColor =   Brushes.Green    |> brighter 140
    let activeCol = Brushes.Orange   |> brighter 20
    let compileCol = Brushes.Magenta
    let grayText =  Brushes.Gray     |> darker 60
    let waitCol  =  Brushes.HotPink  |> brighter 80

open StatusbarStyle
open System.Threading

type CheckerStatus (grid:TabsAndLog) =
    let tb = TextBlock()

    let tabs = grid.Tabs
    let checkingTxt = "Checking for Errors ..."

    let mutable lastErrCount = -1
    let mutable lastFile : TextEditor = null

    let callCounter = ref 0L

    let getErrPanel(es:ErrorsBySeverity, addPersistInfo:bool) =
        let erk = es.errors.Count
        let wak = es.warnings.Count
        let ink = es.infos.Count
        let hik = es.hiddens.Count
        let maxShowCount = 4 // for each typ of error, the maximum number of errors to show
        makePanelVert [
            if erk>0 then
                if addPersistInfo then TextBlock(Text = "Click on text in statusbar or press Ctrl + E keys to scroll to first error.", FontSize = StyleState.fontSize * 0.75, FontStyle = FontStyle.Italic, Margin=Thickness 3.0)
                if addPersistInfo then TextBlock(Text = "Press Ctrl + P to persist this tooltip window.", FontSize = StyleState.fontSize * 0.75, FontStyle = FontStyle.Italic, Margin=Thickness 3.0)
                TextBlockSelectable(Text = "File: " + tabs.Current.FormattedFileName, FontSize = StyleState.fontSize * 0.8, Margin=Thickness 3.0 , TextWrapping = TextWrapping.Wrap)
                TextBlock(Text = "Errors:", FontSize = StyleState.fontSize , FontWeight = FontWeight.Bold )
            for e in Seq.truncate maxShowCount es.errors do
                TextBlockSelectable(Text = sprintf "• line %d: %s: %s" e.StartLine e.ErrorNumberText e.Message, FontSize = StyleState.fontSize * 0.9, Margin=Thickness 3.0 , TextWrapping = TextWrapping.Wrap)
            if erk > maxShowCount then
                TextBlock(Text = sprintf "• and %d more ..." (erk-maxShowCount), FontSize = StyleState.fontSize * 0.9)

            if wak>0 then
                TextBlock(Text="Warnings:", FontSize = StyleState.fontSize , FontWeight = FontWeight.Bold )
            for w in Seq.truncate maxShowCount es.warnings do
                TextBlockSelectable(Text = sprintf "• line %d: %s: %s" w.StartLine w.ErrorNumberText w.Message, FontSize = StyleState.fontSize * 0.9, Margin=Thickness 3.0 , TextWrapping = TextWrapping.Wrap)
            if wak > maxShowCount then
                TextBlock(Text = sprintf "• and %d more ..." (wak-maxShowCount), FontSize = StyleState.fontSize * 0.9)

            if ink>0 then
                TextBlock(Text="Infos:", FontSize = StyleState.fontSize , FontWeight = FontWeight.Bold )
            for i in Seq.truncate maxShowCount es.infos do
                TextBlockSelectable(Text = sprintf "• line %d: %s: %s" i.StartLine i.ErrorNumberText i.Message, FontSize = StyleState.fontSize * 0.9, Margin=Thickness 3.0, TextWrapping = TextWrapping.Wrap )
            if ink > maxShowCount then
                TextBlock(Text = sprintf "• and %d more ..." (ink-maxShowCount), FontSize = StyleState.fontSize * 0.9)

            if hik>0 then
                TextBlock(Text="Hidden Infos:", FontSize = StyleState.fontSize , FontWeight = FontWeight.Bold )
            for h in Seq.truncate maxShowCount es.hiddens do
                TextBlockSelectable(Text = sprintf "• line %d: %s: %s" h.StartLine h.ErrorNumberText h.Message, FontSize = StyleState.fontSize * 0.9, Margin=Thickness 3.0, TextWrapping = TextWrapping.Wrap )
            if hik > maxShowCount then
                TextBlock(Text = sprintf "• and %d more ..." (ink-maxShowCount), FontSize = StyleState.fontSize * 0.9)
            ]


    let updateCheckState(checkState:FileCheckState)=
        let callID = Interlocked.Increment callCounter
        match checkState with
        | Done res ->
            //IFeshLog.log.PrintfnDebugMsg $"checking  Done. Arrived in status bar with {res.checkRes.Diagnostics.Length} msgs"
            let es = res.errors
            let erWas = es.errorsAndWarnings

            if erWas.Count = 0 then
                if lastErrCount <> 0  || lastFile <> tabs.Current.Editor.AvaEdit then // no UI update needed in this case
                    tb.Text <- "No compiler errors"
                    tb.Background <- okColor

                    tb.ToolTip <- "FSharp Compiler Service found no Errors in"+ Environment.NewLine + tabs.Current.FormattedFileName
                    lastFile <- tabs.Current.Editor.AvaEdit
                    lastErrCount <- 0

            else
                lastFile <- tabs.Current.Editor.AvaEdit
                lastErrCount <- erWas.Count
                erWas.Sort(fun x y -> Operators.compare x.StartLine y.StartLine)// sort because we are not sure if they are already sorted

                let erk = es.errors.Count
                let wak = es.warnings.Count
                if wak > 0 && erk > 0 then
                    tb.Text <- sprintf " %d compiler errors, %d warnings, first one on line: %d" erk wak erWas.[0].StartLine
                    tb.Background <- errColor
                elif wak > 0 then
                    tb.Text <- sprintf " %d compiler warnings, first one on line %d" wak erWas.[0].StartLine
                    tb.Background <- warnColor
                elif erk > 0 then
                    tb.Text <- sprintf " %d compiler errors, first one on line: %d" erk erWas.[0].StartLine
                    tb.Background <- errColor
                else
                    tb.Text <- $"No compiler errors, {es.hiddens.Count + es.infos.Count} Infos"
                    tb.Background <- okColor

                let tip = new Popup(Child = getErrPanel(es, true) )
                tip.Placement <- PlacementMode.Top //https://docs.microsoft.com/en-us/dotnet/framework/wpf/controls/popup-placement-behavior
                tip.VerticalOffset <- -6.0
                tb.ToolTipControl <- tip

        | WaitForCompl _  -> ()
        | WaitForErr _| NotChecked ->
            async{
                do! Async.Sleep 200 // delay to only show check in progress massage if it takes long, otherwise just show results via on checked event
                match IEditor.current with
                |None -> ()
                |Some e ->
                    match e.FileCheckState with
                    | Done _ -> () //now need to update, the newer call takes care of this.
                    | WaitForCompl _  -> () // don't show when completion window is open
                    | WaitForErr _ | NotChecked ->
                        if callCounter.Value = callID then
                            lastErrCount <- -1
                            tb.Text <- checkingTxt
                            tb.Background <- waitCol //originalBackGround
                            tb.ToolTip <- sprintf "Checking %s for Errors ..." tabs.Current.FormattedFileName
            } |> Async.StartImmediate

    do
        lastErrCount <- -1
        tb.Padding <-textPadding
        tb.Text <- checkingTxt
        tb.Background <- waitCol //originalBackGround

        tabs.OnTabChanged.Add (fun _ -> TypeInfo.LogErrors.Clear()) // to show old errors in new tab again
        tabs.OnTabChanged.Add (fun t -> updateCheckState(t.Editor.FileCheckState))
        Checker.CheckingStateChanged.Add updateCheckState
        tb.PointerPressed.Add(fun e ->
            if e.Pointer.IsPrimary then  CheckerStatus.goToNextSegment grid.Tabs.Current.Editor)

    member _.TextBlock = tb

    member _.GetErrorPanelCached(ed:IEditor) =
        match ed.FileCheckState with
        | Done res -> Some (getErrPanel(res.errors,false))
        | _ -> None

    static member goToNextSegment(ed:Editor) =
        match ErrorUtil.getNextSegment(ed) with
        |None -> ()
        |Some seg ->  ed.Folds.GoToOffsetAndUnfold (seg.Offset, seg.Length, false)



type FsiRunStatus (grid:TabsAndLog)  =
    let tb =  TextBlock()

    do
        tb.Padding <- textPadding
        tb.Inlines.Add ("FSI is initializing . . .")
        tb.Background <- waitCol //originalBackGround
        //this.ContextMenu <- makeContextMenu [ menuItem cmds.CancelFSI ]
        tb.ToolTip <- "Shows the status of the fsi evaluation core. This is the same for all tabs. Only one script can run at the time."

        grid.Tabs.Fsi.OnCompiling.Add(fun evalData ->
            tb.Background <- activeCol
            tb.Inlines.Clear()
            match evalData.request.editor.FilePath with
            |Deleted fi|SetTo fi ->
                match evalData.request.amount with
                | All                 ->  tb.Inlines.Add(new Run ("FSI is compiling "          , Foreground = grayText))
                | ContinueFromChanges ->  tb.Inlines.Add(new Run ("FSI continues to compiling ", Foreground = grayText))
                | FsiSegment _        ->  tb.Inlines.Add(new Run ("FSI is compiling a part of ", Foreground = grayText))
                tb.Inlines.Add( new Run (fi.Name, FontFamily = StyleState.fontEditor) )
                tb.Inlines.Add( new Run (" . . ."                                              , Foreground = grayText))
            |NotSet dummyName ->
                tb.Inlines.Add( "FSI is compiling "+dummyName + " . . ." )
            )

        grid.Tabs.Fsi.OnEmitting.Add(fun codeToEval -> // TODO unused  till https://github.com/dotnet/fsharp/pull/15957
            tb.Background <- compileCol
            tb.Inlines.Clear()
            match codeToEval.editor.FilePath with
            |Deleted fi|SetTo fi ->
                match codeToEval.amount with
                | All                 ->  tb.Inlines.Add(new Run ("FSI is running ",           Foreground = grayText))
                | ContinueFromChanges ->  tb.Inlines.Add(new Run ("FSI continues to run "   ,  Foreground = grayText))
                | FsiSegment _        ->  tb.Inlines.Add(new Run ("FSI is running a part of ", Foreground = grayText))
                tb.Inlines.Add( new Run (fi.Name, FontFamily = StyleState.fontEditor) )
                tb.Inlines.Add( new Run (" . . ."                                           , Foreground = grayText))
            |NotSet dummyName ->
                tb.Inlines.Add( "FSI is running "+dummyName + " . . ." )
            )

        grid.Tabs.Fsi.OnIsReady.Add(fun _ ->
            tb.Inlines.Clear()
            tb.Inlines.Add("FSI is ready")
            tb.Background <- okColor)

    member _.TextBlock = tb

type FsiOutputStatus (grid:TabsAndLog) =
    let tb =  TextBlock()
    let onTxt = "FSI prints to log window"
    let offTxt = "FSI is quiet"
    let isOff () = grid.Config.Settings.GetBool ("fsiOutputQuiet", false)
    do
        tb.Padding <- textPadding
        tb.Text <- if isOff() then offTxt else onTxt
        tb.ToolTip <- "Click here to enable or disable the default output from fsi in the log window\r\n(This will also reset FSI.)"
        tb.PointerPressed.Add(fun e ->
            if e.Pointer.IsPrimary then
                if isOff() then
                    tb.Text <- onTxt
                    grid.Config.Settings.SetBool ("fsiOutputQuiet", false)
                else
                    tb.Text <- offTxt
                    grid.Config.Settings.SetBool ("fsiOutputQuiet", true)
                grid.Tabs.Fsi.Initialize()
            )
    member _.TextBlock = tb

type AsyncStatus (grid:TabsAndLog) =
    let tb =  TextBlock()
    let fsi = grid.Tabs.Fsi
    let isAsync = grid.Config.Settings.GetBool ("asyncFsi", false)
    let sync = "FSI evaluation mode: Synchronous"
    let asyn = "FSI evaluation mode: Asynchronous"

    do
        tb.Padding <- textPadding
        tb.Text <- if isAsync then asyn else sync
        tb.ToolTip <- "Click to switch between synchronous and asynchronous evaluation in FSI,\r\nsynchronous is needed for UI interaction,\r\nasynchronous allows easy cancellation and keeps the editor window alive"
        tb.PointerPressed.Add(fun _ -> fsi.ToggleSync())     // TODO better make it dependent on commands , not fsi
        fsi.OnModeChanged.Add(function
            | InSync             -> tb.Text <- sync
            | AsyncMode          -> tb.Text <- asyn  )

    member _.TextBlock = tb

#nowarn "44" //for obsolete grid.Log.AvalonLog.AvalonEdit

type SelectedEditorTextStatus (grid:TabsAndLog) =
    let tb = TextBlock()
    let noSelTxt = new Run ("no selection in Editor", Foreground = SelectionHighlighting.colorInactive) //Editor Selection Highlighting"
    let tipText = "Highlights and counts the occurrences of the currently selected Text in the current Editor.\r\nMinimum two characters and no line breaks.\r\nClick here to scroll through all occurrences."
    let mutable scrollToIdx = 0

    let fillStatusMarkLog triggerNext  =
        let sel = grid.Tabs.Current.Editor.DrawingServices.selection
        if triggerNext then
            match grid.Log.SelectionHighlighter with
            |Some logHiLi -> logHiLi.MarkInLog(sel.Word)
            |None         -> IFeshLog.log.PrintfnAppErrorMsg "Log.SelectionHighlighter not set up"

        if sel.Offsets.Count = 0 then
            tb.Inlines.Clear()
            tb.Inlines.Add noSelTxt
        else
            tb.Inlines.Clear()
            tb.Inlines.Add( $"%d{sel.Offsets.Count} of "  )
            tb.Inlines.Add( new Run (sel.Word, FontFamily = StyleState.fontEditor, Background = SelectionHighlighting.selColorEditor))
            tb.Inlines.Add( $" (%d{sel.Word.Length} Chars) " )

    do
        tb.Padding <- textPadding
        tb.ToolTip <- tipText
        tb.Inlines.Add noSelTxt
        SelectionHighlighting.GlobalFoundSelectionsEditor.Add(fillStatusMarkLog)

        // on each click loop through all locations where text appears
        tb.PointerPressed.Add ( fun _ -> // press mouse to scroll to them
            let ed = grid.Tabs.Current.Editor
            let sel = ed.DrawingServices.selection
            if sel.Offsets.Count > 0 then
                //ed.AvaEdit.Focus() |> ignore
                if scrollToIdx >= sel.Offsets.Count then scrollToIdx <- 0
                let ed = grid.Tabs.Current.Editor
                let off = sel.Offsets.[scrollToIdx]
                if off < ed.AvaEdit.Document.TextLength then
                    ed.Folds.GoToOffsetAndUnfold(off, sel.Word.Length,  true )
                    scrollToIdx <- scrollToIdx + 1
                else
                    scrollToIdx <- 0
            )
    member _.TextBlock = tb

type SelectedLogTextStatus (grid:TabsAndLog) =
    let tb =  TextBlock()
    let log = grid.Log
    let noSelTxt = new Run ("no selection in Log", Foreground = SelectionHighlighting.colorInactive) //Log Selection Highlighting "
    let tipText = "Highlights and counts the occurrences of the currently selected Text in the Log output.\r\nMinimum two characters and no line breaks.\r\nClick here to scroll through all occurrences."
    let mutable scrollToIdx = 0

    let setStatusMarkEd triggerNext =
        match log.SelectionHighlighter with
        |None -> IFeshLog.log.PrintfnAppErrorMsg "Log.SelectionHighlighter not set up"
        |Some hiLi ->
            if triggerNext then
                grid.Tabs.Current.Editor.DrawingServices.selection.RedrawMarksInEditor(hiLi.Word)

            if hiLi.Offsets.Count = 0 then
                tb.Inlines.Clear()
                tb.Inlines.Add noSelTxt
            else
                tb.Inlines.Clear()
                tb.Inlines.Add( sprintf $"%d{hiLi.Offsets.Count} of ")
                tb.Inlines.Add( new Run (hiLi.Word, FontFamily = StyleState.fontEditor, Background = SelectionHighlighting.selColorLog))
                tb.Inlines.Add( sprintf " (%d Chars) " hiLi.Word.Length)

    do
        tb.Padding <- textPadding
        tb.ToolTip <- tipText
        tb.Inlines.Add noSelTxt
        SelectionHighlighting.FoundSelectionsLog.Add(setStatusMarkEd)

        // on each click loop through all locations where text appears
        tb.PointerPressed.Add ( fun _ -> // press mouse to scroll to them
            match log.SelectionHighlighter with
            |None      -> ()
            |Some hiLi ->
                if hiLi.Offsets.Count > 0 then
                    if scrollToIdx >= hiLi.Offsets.Count then scrollToIdx <- 0
                    let off = hiLi.Offsets.[scrollToIdx]
                    let doc = log.AvaloniaEditLog.Document
                    if off < doc.TextLength then
                        //log.AvalonEditLog.Focus() |> ignore
                        //match log.Folds with Some f -> f.GoToOffsetAndUnfold(off, sel.Word.Length, true )  |None ->()
                        let ln = doc.GetLineByOffset(off)
                        log.AvaloniaEditLog.ScrollTo(ln.LineNumber,1)
                        log.AvaloniaEditLog.Select(off, hiLi.Word.Length)
                        scrollToIdx <- scrollToIdx + 1
                    else
                        scrollToIdx <- 0
            )

        // this event is added in Log highLighter constructor, so that this happens only once !
        grid.Tabs.OnTabChanged.Add ( fun _ -> grid.Tabs.Current.Editor.DrawingServices.selection.TriggerGlobalFoundSelectionEditorEv())

    member _.TextBlock = tb

type FeshStatusBar (grid:TabsAndLog)  =
    let bar = new DockPanel()

    let addSep (side:Dock) =
        let s = new Separator()
        DockPanel.SetDock(s,side)
        bar.Children.Add s |> ignore


    let add (side:Dock) (e:Control) =
        DockPanel.SetDock(e,side)
        bar.Children.Add e |> ignore


    let fsi = FsiRunStatus grid
    let errs = CheckerStatus grid
    let edSel = SelectedEditorTextStatus grid
    let logSel = SelectedLogTextStatus grid
    let fsiOutput = FsiOutputStatus grid
    let asyncStatus = AsyncStatus grid


    do
        add    Dock.Left  errs.TextBlock // on very left
        addSep Dock.Left
        add    Dock.Left  edSel.TextBlock // on left
        addSep Dock.Left
        add    Dock.Left  logSel.TextBlock // on left
        addSep Dock.Left

        add    Dock.Right fsi.TextBlock // on very right
        addSep Dock.Right
        add    Dock.Right fsiOutput.TextBlock // on right
        addSep Dock.Right

        if grid.Config.RunContext.CanRunAsync then
            add    Dock.Right  asyncStatus.TextBlock // on right
            addSep Dock.Right

        bar.Children.Add (new ContentControl()) |> ignore // needed to fill remaining gap


    member this.Bar =  bar

    member this.FsiStatus = fsi

    member this.CheckerStatus = errs



