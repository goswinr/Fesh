namespace Fesh.Editor

open System
open System.Windows
open System.Windows.Media
open Fesh
open Fesh.Model
open Fesh.Util.General

/// The colors for the semantic highlighting.
/// They are loaded from the xshd file too, see the 'Semantic.' colors in SyntaxHighlightingFSharp.xshd
module SemanticColors =
    open AvalonEditB.Highlighting
    open AvalonEditB.Rendering

    [<NoComparison; NoEquality>]
    type SemStyle = {
        foreground : Brush
        background : Brush
        bold       : bool
        italic     : bool
        underline  : bool
        }

    /// The names of the colors in the xshd file, without the 'Semantic.' prefix
    let names = [|
        "ReferenceType"; "ValueType"; "UnionCase"; "UnionCaseField"; "Function"; "Property"; "MutableVar"; "Module"; "Namespace"
        "ComputationExpression"; "IntrinsicFunction"; "Enumeration"; "Interface"; "TypeArgument"; "Operator"; "DisposableType"
        "DisposableTopLevelValue"; "DisposableLocalValue"; "Method"; "ExtensionMethod"; "ConstructorForReferenceType"
        "ConstructorForValueType"; "Literal"; "RecordField"; "MutableRecordField"; "RecordFieldAsFunction"; "Exception"; "Field"
        "Event"; "Delegate"; "NamedArgument"; "Value"; "LocalValue"; "Type"; "TypeDef"; "Plaintext"
        "Unused"; "BadIndent" |]

    let private empty = { foreground = null; background = null; bold = false; italic = false; underline = false }

    /// Replaced when the xshd file is loaded for another theme or reloaded after an edit
    let mutable private styles : SemStyle[] = Array.create names.Length empty

    /// Returns an Action that applies the style of this name.
    /// The style is looked up when the Action is invoked, so the Action does not need to be recreated when the theme changes.
    let action (name:string) =
        let i =
            match Array.IndexOf(names, name) with
            | -1 -> failwithf "SemanticColors.action: unknown name '%s'" name
            | i -> i
        new Action<VisualLineElement>(fun el ->
            let s = styles.[i]
            let p = el.TextRunProperties
            if notNull s.foreground then p.SetForegroundBrush s.foreground
            if notNull s.background then p.SetBackgroundBrush s.background
            if s.bold || s.italic then
                p.SetTypeface(
                    if   s.bold && s.italic then StyleState.italicBoldEditorTf
                    elif s.bold             then StyleState.boldEditorTf
                    else                         StyleState.italicEditorTf )
            if s.underline then p.SetTextDecorations TextDecorations.Underline
            )

    /// Reads the 'Semantic.' colors from the highlighting definition
    let update (def:IHighlightingDefinition) =
        styles <-
            names |> Array.map (fun n ->
                match def.GetNamedColor("Semantic." + n) with
                | null ->
                    IFeshLog.log.PrintfnAppErrorMsg "The color 'Semantic.%s' is missing in the syntax highlighting file SyntaxHighlightingFSharp.xshd" n
                    empty
                | c ->
                    {
                    foreground = if isNull c.Foreground then null else c.Foreground.GetBrush(null)
                    background = if isNull c.Background then null else c.Background.GetBrush(null)
                    bold       = c.FontWeight.HasValue && c.FontWeight.Value.ToOpenTypeWeight() >= 600
                    italic     = c.FontStyle.HasValue  && c.FontStyle.Value <> FontStyles.Normal
                    underline  = c.Underline.HasValue  && c.Underline.Value
                    }
                )


module SyntaxHighlighting =
    open AvalonEditB
    open AvalonEditB.Highlighting
    open System.IO
    open System.Xml

    // Use the same highlighting for all tabs. Load it just once per theme:
    let mutable private lightDef : IHighlightingDefinition = null
    let mutable private darkDef  : IHighlightingDefinition = null

    let private currentDef () = if Theme.isDark then darkDef else lightDef

    let mutable filePath = ""

    let mutable private isLoading = false

    /// Editors that wait for the first loading to finish
    let private waiting = ResizeArray<TextEditor>()

    let private loadDefinition () =
        //let stream = Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("FSharpSyntaxHighlighter.xshd") // Build action : Embedded Resource; Copy to output Dir: NO
        let path = Theme.xshdPath //will be copied there after compiling recompiling
        filePath <- path
        let doc = XmlDocument(PreserveWhitespace = true) // to keep the line numbers for error messages
        doc.Load path
        Theme.applyDarkAttributes Theme.isDark doc
        use reader = new XmlTextReader(new StringReader(doc.OuterXml))
        Xshd.HighlightingLoader.Load(reader, HighlightingManager.Instance)

    /// Loads the xshd file for the current theme on a background thread.
    /// Then calls 'apply' on the UI thread, if the theme is still the same.
    let private load (apply: IHighlightingDefinition -> unit) =
        let dark = Theme.isDark
        async{
            try
                let def = loadDefinition()
                do! Async.SwitchToContext Fittings.SyncWpf.context
                if dark then darkDef <- def else lightDef <- def
                if dark = Theme.isDark then
                    SemanticColors.update def
                    apply def
            with e ->
                IFeshLog.log.PrintfnAppErrorMsg "Error loading Syntax Highlighting: %A" e
                do! Async.SwitchToContext Fittings.SyncWpf.context
                waiting.Clear()
                isLoading <- false
            } |> Async.Start

    /// Sets the F# syntax highlighting on this editor. Loads it first if needed.
    let setFSharp (ed:TextEditor) =
        match currentDef() with
        | null ->
            waiting.Add ed
            if not isLoading then
                isLoading <- true
                load (fun def ->
                    for e in waiting do e.SyntaxHighlighting <- def
                    waiting.Clear()
                    isLoading <- false
                    )
        | def ->
            ed.SyntaxHighlighting <- def

    /// Sets the syntax highlighting for the current theme on all the given editors.
    /// Call after the theme was changed. Loads the xshd file if needed.
    let applyTheme (editors: unit -> seq<TextEditor>) =
        match currentDef() with
        | null ->
            load (fun def -> for e in editors() do e.SyntaxHighlighting <- def)
        | def ->
            SemanticColors.update def
            for e in editors() do e.SyntaxHighlighting <- def

    /// Loads the xshd file again, e.g. after it was edited, and sets it on all the given editors.
    let reload (editors: unit -> seq<TextEditor>) =
        lightDef <- null
        darkDef  <- null
        load (fun def ->
            for e in editors() do e.SyntaxHighlighting <- def
            for problem in Theme.reloadColors() do IFeshLog.log.PrintfnAppErrorMsg "%s" problem // the colors of the editor and the Log, raises Theme.Changed
            IFeshLog.log.PrintfnInfoMsg "loaded syntax highlighting from: %s" filePath
            )


    let private xlsWatcher = new FileSystemWatcher()
    let mutable private isWatching = false // to create only once the event

    /// includes file system watcher for FileChanged
    let watch(path:FileInfo, editors: unit -> seq<TextEditor>) =
        path.Refresh()
        if not isWatching && path.Exists  then
            let file = path.Name
            let folder = path.DirectoryName
            xlsWatcher.Path <- folder
            xlsWatcher.Filter <- file
            xlsWatcher.NotifyFilter  <-  NotifyFilters.LastWrite
            xlsWatcher.EnableRaisingEvents <- true // must be after setting path
            xlsWatcher.Changed.Add (fun _ ->
                xlsWatcher.EnableRaisingEvents <- false // to not raise events twice
                try
                    async{
                        do! Async.Sleep 200 // wait till file is really closed
                        reload editors
                        } |> Async.StartImmediate
                finally
                    async{
                        do! Async.Sleep 500
                        xlsWatcher.EnableRaisingEvents <- true // to not raise events twice
                        } |> Async.StartImmediate
                )
            isWatching <-true

    let openVSCode(editors: unit -> seq<TextEditor>) =
        try
            if IO.File.Exists filePath then
                if VSCode.openPath(filePath, false) then
                    watch(FileInfo filePath, editors)
            else
                IFeshLog.log.PrintfnIOErrorMsg "File not found: %s" filePath
        with e ->
            IFeshLog.log.PrintfnIOErrorMsg "Open SyntaxHighlighting with VScode failed: %A" e
