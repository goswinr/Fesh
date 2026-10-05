namespace Fesh.Editor

open System
open System.Windows
open System.Windows.Controls
open System.Windows.Input
open System.Windows.Media
open System.Windows.Threading
open System.Collections.Generic

open AvalonEditB
open AvalonEditB.Document
open AvalonEditB.Rendering

open AvalonLog.Brush

open Fesh.Model


module private NugetHintStyle =

    let upToDateFg      = SolidColorBrush(Color.FromRgb( 20uy, 150uy,  50uy)) |> freeze
    let notFoundFg      = SolidColorBrush(Color.FromRgb(150uy, 150uy, 150uy)) |> freeze

    let updateFg        = SolidColorBrush(Color.FromRgb(170uy,  85uy,   0uy)) |> freeze
    let updateBg        = SolidColorBrush(Color.FromRgb(255uy, 240uy, 215uy)) |> freeze
    let updateBgHover   = SolidColorBrush(Color.FromRgb(255uy, 220uy, 165uy)) |> freeze

    let pinFg           = SolidColorBrush(Color.FromRgb( 40uy,  90uy, 160uy)) |> freeze
    let pinBg           = SolidColorBrush(Color.FromRgb(228uy, 238uy, 252uy)) |> freeze
    let pinBgHover      = SolidColorBrush(Color.FromRgb(200uy, 220uy, 248uy)) |> freeze

    let linkFg          = SolidColorBrush(Color.FromRgb(120uy, 130uy, 150uy)) |> freeze
    let linkFgHover     = SolidColorBrush(Color.FromRgb( 30uy,  90uy, 200uy)) |> freeze

    /// Two chain links, to be drawn as a stroke.
    let linkGeometry =
        let g = GeometryGroup()
        g.Children.Add(RectangleGeometry(Rect(0.0, 0.0, 9.0, 6.0), 3.0, 3.0))
        g.Children.Add(RectangleGeometry(Rect(6.0, 0.0, 9.0, 6.0), 3.0, 3.0))
        g.Transform <- RotateTransform(-45.0, 7.5, 3.0)
        g.Freeze()
        g

open NugetHintStyle


/// A layer on top of the text that places one element after the end of each given visual line.
/// It is not part of the text flow, so it does not change the caret positions, the selection or the line height.
type NugetHintLayer(tv:TextView) =
    inherit Panel()

    let placed = ResizeArray<struct(VisualLine * FrameworkElement)>()

    /// The elements to arrange and the visual line each one goes after.
    /// The elements must also be in the Children collection.
    member _.Placed = placed

    override this.MeasureOverride(_availableSize) =
        let maxSize = Size(Double.PositiveInfinity, tv.DefaultLineHeight)
        for struct(_, el) in placed do
            el.Measure(maxSize)
        Size(0.0, 0.0) // so that this layer never changes the size of the TextView

    override this.ArrangeOverride(finalSize) =
        let scroll = tv.ScrollOffset
        let gap = tv.WideSpaceWidth * 2.0
        let lineHeight = tv.DefaultLineHeight
        for struct(vl, el) in placed do
            if not vl.IsDisposed then
                let p = vl.GetVisualPosition(vl.VisualLength, VisualYPosition.LineTop) // top of the last text line, relative to the document
                let sz = el.DesiredSize
                let x = p.X + gap - scroll.X
                let y = p.Y + (lineHeight - sz.Height) * 0.5 - scroll.Y
                el.Arrange(Rect(Point(x, y), sz))
        finalSize


/// Shows a small control after each  #r "nuget: ..."  line:
/// A green check mark if the version is the latest one, a button to update it, or a button to pin the latest version if none is given.
/// getFolder returns the folder of the script, to find the NuGet.Config files. Or "" if the file is not saved yet.
/// tryRecheck starts a type check that also resolves the packages on the line of the last edit.
/// (The normal check after an edit skips them, so that they are not restored while typing.)
/// It returns false if no check can be started now, e.g. while the completion window is open.
/// It is called after a click changed a version, and when the caret leaves an edited #r "nuget: ..." line.
type NugetHints (ed:TextEditor, getFolder: unit -> string, tryRecheck: unit -> bool) =

    /// Shared by all editors: set when the NuGet client assemblies can't be loaded, e.g. because of version conflicts in a hosting app.
    static let mutable disabled = false

    static let disable (e:exn) =
        if not disabled then
            disabled <- true
            IFeshLog.log.PrintfnAppErrorMsg "Version hints for #r \"nuget: ...\" lines are turned off because the NuGet client libraries could not be loaded:\r\n%s" e.Message

    let tv = ed.TextArea.TextView

    let layer = new NugetHintLayer(tv, ClipToBounds = true)

    /// The shown hints per document line, so that an element is only recreated when its content changes.
    let shown = Dictionary<DocumentLine, struct(string * FrameworkElement)>()

    /// To wait with the lookups till typing pauses, so that partially typed package names are not looked up.
    let debounce = DispatcherTimer(DispatcherPriority.Background, Interval = TimeSpan.FromMilliseconds 800.0)

    /// The #r "nuget: ..." line that was edited last and not yet included in a type check, or null.
    let mutable editedNugetLine : DocumentLine = null

    /// The type check after an edit skips the packages on the edited line.
    /// So remember the line, to check again when the caret leaves it.
    let markEditedLine (a:DocumentChangeEventArgs) =
        let doc = ed.Document
        let line = doc.GetLineByOffset a.Offset // the line that the checker treats as the caret line
        if NugetDirective.isCandidate doc line && (doc.GetText line).Contains "\"nuget:" then
            editedNugetLine <- line

    let recheckIfCaretLeft () =
        let line = editedNugetLine
        if not (isNull line) then
            if line.IsDeleted then
                editedNugetLine <- null
            elif ed.TextArea.Caret.Line <> line.LineNumber then
                // Run it after the current edit is fully processed:
                // The caret also moves inside the Document.Changed event, possibly before the regular check of that edit got its change ID.
                ed.Dispatcher.BeginInvoke(DispatcherPriority.Background, Action(fun () ->
                    if Object.ReferenceEquals(editedNugetLine, line) && not line.IsDeleted && ed.TextArea.Caret.Line <> line.LineNumber then
                        if tryRecheck() then
                            editedNugetLine <- null
                    )) |> ignore

    /// Re-parse the line on click, it might have changed since the element was created.
    let applyVersion (line:DocumentLine) (version:string) =
        if not line.IsDeleted then
            let doc = ed.Document
            match NugetDirective.tryParse (doc.GetText line) with
            | None -> ()
            | Some d ->
                if d.versionStart >= 0 then
                    doc.Replace(line.Offset + d.versionStart, d.versionEnd - d.versionStart, version)
                elif d.isKeyed then
                    doc.Insert(line.Offset + d.nameEnd, ", Version=" + version)
                else
                    doc.Insert(line.Offset + d.nameEnd, ", " + version)
                // The Document.Changed handlers have run, so the regular check of this edit has its change ID already.
                // Check again right away, the caret might not even be on this line:
                if tryRecheck() then
                    editedNugetLine <- null
        ed.TextArea.Focus() |> ignore

    let makeElement (line:DocumentLine) (h:NugetHint) : FrameworkElement =
        let tb = TextBlock(FontSize = ed.FontSize * 0.85, VerticalAlignment = VerticalAlignment.Center)
        let b = Border(Child = tb, CornerRadius = CornerRadius 3.0, Padding = Thickness(4.0, 0.0, 4.0, 0.0), ToolTip = h.tip, Cursor = Cursors.Arrow)
        ToolTipService.SetInitialShowDelay(b, 300)
        ToolTipService.SetShowDuration(b, 30_000)

        let asButton (text:string) (fg:Brush) (bg:Brush) (bgHover:Brush) =
            tb.Text <- text
            tb.Foreground <- fg
            b.Background <- bg
            b.Cursor <- Cursors.Hand
            b.MouseEnter.Add(fun _ -> b.Background <- bgHover)
            b.MouseLeave.Add(fun _ -> b.Background <- bg)
            b.MouseLeftButtonDown.Add(fun e ->
                e.Handled <- true // so that the TextArea does not start a selection
                applyVersion line h.version)

        match h.kind with
        | NugetHintKind.UpToDate -> tb.Text <- "✔"; tb.Foreground <- upToDateFg
        | NugetHintKind.NotFound -> tb.Text <- "?"; tb.Foreground <- notFoundFg
        | NugetHintKind.UpdateTo -> asButton ("↑ " + h.version)   updateFg updateBg updateBgHover
        | NugetHintKind.PinTo    -> asButton ("pin " + h.version) pinFg    pinBg    pinBgHover
        | NugetHintKind.NoHint   -> ()

        if h.projectUrl = "" then
            b :> FrameworkElement
        else
            let size = ed.FontSize * 0.8
            let icon = Shapes.Path(Data = linkGeometry, Stroke = linkFg, StrokeThickness = 1.3, Stretch = Stretch.Uniform, Width = size, Height = size)
            let link = Border(Child = icon, Background = Brushes.Transparent, Padding = Thickness(3.0, 0.0, 3.0, 0.0), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center)
            link.ToolTip <- "Open the project website:\r\n" + h.projectUrl
            ToolTipService.SetInitialShowDelay(link, 300)
            link.MouseEnter.Add(fun _ -> icon.Stroke <- linkFgHover)
            link.MouseLeave.Add(fun _ -> icon.Stroke <- linkFg)
            link.MouseLeftButtonDown.Add(fun e ->
                e.Handled <- true // so that the TextArea does not start a selection
                Fesh.Util.General.browseTo h.projectUrl)
            let panel = StackPanel(Orientation = Orientation.Horizontal)
            panel.Children.Add b    |> ignore
            panel.Children.Add link |> ignore
            panel :> FrameworkElement

    let clearAll () =
        layer.Placed.Clear()
        layer.Children.Clear()
        shown.Clear()

    /// Runs on each VisualLinesChanged event, so during the measure pass of the TextView.
    let rebuild () =
        if disabled then
            if shown.Count > 0 then clearAll()
        elif tv.VisualLinesValid then
            try
                let folder = getFolder()
                let doc = ed.Document
                let placed = layer.Placed
                placed.Clear()
                let mutable needsFetch = false
                let stillShown = HashSet<DocumentLine>()
                for vl in tv.VisualLines do
                    let line = vl.FirstDocumentLine // lines hidden in a folding are skipped
                    if NugetDirective.isCandidate doc line then
                        match NugetDirective.tryParse (doc.GetText line) with
                        | None -> ()
                        | Some d ->
                            let struct(hint, isStale) = NugetVersions.TryGetHint(folder, d)
                            if isStale then
                                needsFetch <- true
                            if hint.kind <> NugetHintKind.NoHint then
                                let key = $"{hint.kind}|{hint.version}|{hint.tip}|{hint.projectUrl}|{ed.FontSize}"
                                let el =
                                    match shown.TryGetValue line with
                                    | true, struct(k, el) when k = key ->
                                        el
                                    | found, struct(_, old) ->
                                        if found then layer.Children.Remove old
                                        let el = makeElement line hint
                                        layer.Children.Add el |> ignore
                                        shown[line] <- struct(key, el)
                                        el
                                stillShown.Add line |> ignore
                                placed.Add(struct(vl, el))

                // remove the hints of lines that are not visible anymore or have no hint anymore:
                if shown.Count > stillShown.Count then
                    for line in List<DocumentLine>(shown.Keys) do
                        if not (stillShown.Contains line) then
                            let struct(_, el) = shown[line]
                            layer.Children.Remove el
                            shown.Remove line |> ignore

                layer.InvalidateMeasure()
                layer.InvalidateArrange()

                if needsFetch then // restart the timer
                    debounce.Stop()
                    debounce.Start()
            with e ->
                if NugetLoad.isLoadError e then
                    disable e
                    clearAll()
                else
                    IFeshLog.log.PrintfnAppErrorMsg "Error in NugetHints.rebuild:\r\n%A" e

    /// Looks up all packages of the document, then shows the hints.
    let fetchAll () =
        debounce.Stop()
        if not disabled then
            let folder = getFolder()
            let doc = ed.Document
            let names =
                [| for line in doc.Lines do
                    if NugetDirective.isCandidate doc line then
                        match NugetDirective.tryParse (doc.GetText line) with
                        | Some d -> yield d.name
                        | None   -> () |]
            if names.Length > 0 then
                async {
                    try
                        do! NugetVersions.Fetch(folder, names)
                    with e ->
                        if NugetLoad.isLoadError e then
                            disable e
                        else
                            IFeshLog.log.PrintfnAppErrorMsg "Error in NugetHints.fetchAll:\r\n%A" e
                    do! Async.SwitchToContext Fittings.SyncWpf.context
                    match NugetVersions.LoadError with
                    | Some e -> disable e
                    | None -> ()
                    rebuild()
                }
                |> Async.Start

    do
        tv.InsertLayer(layer, KnownLayer.Text, LayerInsertionPosition.Above)
        tv.VisualLinesChanged.Add (fun _ -> rebuild())
        tv.ScrollOffsetChanged.Add(fun _ -> layer.InvalidateArrange())
        debounce.Tick.Add         (fun _ -> fetchAll())
        ed.Document.Changed.Add   markEditedLine
        ed.TextArea.Caret.PositionChanged.Add(fun _ -> recheckIfCaretLeft())

    /// The string used in the settings file: "CheckNugetVersions"
    static member SettingsStr = "CheckNugetVersions"

    static member onByDefault = true
