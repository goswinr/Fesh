namespace Fesh

open System
open System.Collections.Generic
open System.Windows.Media
open System.Xml
open AvalonLog.Brush

// All colors of Fesh that are not in SyntaxHighlightingFSharp.xshd.
// The xshd file has the syntax highlighting colors (including the semantic highlighting) and the 'Editor.' and 'Log.' colors,
// that is the colors of the code editor and of the Log window, including their line number margins. See EditorColors and LogWindowColors.
// The dark styles of the WPF controls (menus, tabs, scrollbars, ...) are in ThemeChrome.xaml, loaded by ThemeChrome.fs.

/// Colors of the code editor that are not part of the syntax highlighting.
/// They are read from the 'Editor.' colors in SyntaxHighlightingFSharp.xshd
type EditorColors = {
    background          : SolidColorBrush
    foreground          : SolidColorBrush
    currentLine         : SolidColorBrush
    link                : SolidColorBrush
    /// Background of the line number and folding margin
    margin              : SolidColorBrush
    lineNumbers         : SolidColorBrush
    /// The line number of the line with the caret
    currentLineNumber   : SolidColorBrush
    /// The first column ruler, the following ones fade out
    columnRuler         : SolidColorBrush
    foldingMarker       : SolidColorBrush
    foldingMarkerBg     : SolidColorBrush
    foldingMarkerSel    : SolidColorBrush
    foldingMarkerSelBg  : SolidColorBrush
    /// Background of the results of the search panel (Ctrl + F)
    searchMarker        : SolidColorBrush
    /// Background of the text selection, null to use the AvalonEdit default
    selection           : SolidColorBrush
    /// Background of all other occurrences of the selected word
    selectionHighlight  : SolidColorBrush
    /// Background of the code that was evaluated in FSI already
    evaluatedBg         : SolidColorBrush
    evaluatedBorder     : Pen
    /// Background of the matching bracket pair at the caret
    bracketPair         : SolidColorBrush
    bracketError        : SolidColorBrush
    bracketErrorBg      : SolidColorBrush
    /// The colors of nested brackets. The first one is null to keep the color from the xshd file.
    bracketNesting      : SolidColorBrush[]
    }

/// Colors to highlight errors, warnings and infos in the editor
type ErrorColors = {
    errBackGr           : SolidColorBrush
    errSquiggle         : SolidColorBrush
    errSquigglePen      : Pen
    warnBackGr          : SolidColorBrush
    warnSquiggle        : SolidColorBrush
    warnSquigglePen     : Pen
    infoBackGr          : SolidColorBrush
    infoSquiggle        : SolidColorBrush
    infoSquigglePen     : Pen
    }

/// Colors of the Log window, not of the printed text.
/// They are read from the 'Log.' colors in SyntaxHighlightingFSharp.xshd
type LogWindowColors = {
    background          : SolidColorBrush
    foreground          : SolidColorBrush
    link                : SolidColorBrush
    border              : SolidColorBrush
    /// Background of all other occurrences of the selected word
    selectionHighlight  : SolidColorBrush
    /// Background of the line number margin
    margin              : SolidColorBrush
    lineNumbers         : SolidColorBrush
    }

/// The colors of the text printed to the Log window
type LogTextColors = {
    consoleOut          : SolidColorBrush
    /// Values printed by fsi itself like "val it = ...."
    fsiStdOut           : SolidColorBrush
    fsiErrorOut         : SolidColorBrush
    /// Used by eprintfn
    consoleError        : SolidColorBrush
    infoMsg             : SolidColorBrush
    fsiErrorMsg         : SolidColorBrush
    appErrorMsg         : SolidColorBrush
    iOErrorMsg          : SolidColorBrush
    debugMsg            : SolidColorBrush
    runtimeErr          : SolidColorBrush
    }

/// Background colors of the status bar items, depending on their state
type StatusBarColors = {
    err                 : SolidColorBrush
    warn                : SolidColorBrush
    ok                  : SolidColorBrush
    active              : SolidColorBrush
    compile             : SolidColorBrush
    wait                : SolidColorBrush
    grayText            : SolidColorBrush
    inactiveText        : SolidColorBrush
    }

/// Colors of the tab headers
type TabColors = {
    saved               : SolidColorBrush
    changed             : SolidColorBrush
    deleted             : SolidColorBrush
    unsaved             : SolidColorBrush
    border              : SolidColorBrush
    closeButton         : SolidColorBrush
    closeButtonHover    : SolidColorBrush
    }

/// Colors of the type info and completion tooltips
type ToolTipColors = {
    text                : SolidColorBrush
    code                : SolidColorBrush
    codeBg              : SolidColorBrush
    gray                : SolidColorBrush
    errMsg              : SolidColorBrush
    purple              : SolidColorBrush
    blue                : SolidColorBrush
    red                 : SolidColorBrush
    fullRed             : SolidColorBrush
    cyan                : SolidColorBrush
    darkGray            : SolidColorBrush
    darkBlue            : SolidColorBrush
    glyph               : SolidColorBrush
    border              : SolidColorBrush
    }

/// Colors of the hints after #r "nuget: ..." lines
type NugetHintColors = {
    upToDateFg          : SolidColorBrush
    notFoundFg          : SolidColorBrush
    checkingFg          : SolidColorBrush
    updateFg            : SolidColorBrush
    updateBg            : SolidColorBrush
    updateBgHover       : SolidColorBrush
    pinFg               : SolidColorBrush
    pinBg               : SolidColorBrush
    pinBgHover          : SolidColorBrush
    linkFg              : SolidColorBrush
    linkFgHover         : SolidColorBrush
    }

/// Colors of the WPF controls in the dark theme. In the light theme the default WPF styles are used.
type ChromeColors = {
    background          : SolidColorBrush
    hover               : SolidColorBrush
    pressed             : SolidColorBrush
    popup               : SolidColorBrush
    border              : SolidColorBrush
    text                : SolidColorBrush
    textDim             : SolidColorBrush
    textDisabled        : SolidColorBrush
    accent              : SolidColorBrush
    /// Selected items in lists, e.g. in the completion window
    highlight           : SolidColorBrush
    scrollThumb         : SolidColorBrush
    scrollThumbHover    : SolidColorBrush
    scrollThumbDrag     : SolidColorBrush
    tabHover            : SolidColorBrush
    inputBg             : SolidColorBrush
    inputBorder         : SolidColorBrush
    buttonBg            : SolidColorBrush
    }


[<RequireQualifiedAccess>]
module Theme =

    /// A frozen brush from a hex string like "#1E1E1E" or "#801E1E1E" with alpha
    let private hex (s:string) = SolidColorBrush(ColorConverter.ConvertFromString s :?> Color) |> freeze

    /// A new frozen brush instance, not shared with the static brushes in System.Windows.Media.Brushes
    let private copy (b:SolidColorBrush) = SolidColorBrush(b.Color) |> freeze

    let private pen (width:float) (b:SolidColorBrush) =
        let p = Pen(b, width)
        p.Freeze()
        p

    let private squiggle = pen 1.2

    // -------------------- Editor and Log window, read from the xshd file --------------------

    /// The syntax highlighting file. It has the 'Editor.' and 'Log.' colors too.
    let xshdPath = IO.Path.Combine(IO.Path.GetDirectoryName(Reflection.Assembly.GetExecutingAssembly().Location), "SyntaxHighlightingFSharp.xshd")

    /// Replaces each attribute that has a 'dark' version, e.g. 'foreground' with 'darkForeground', if dark is true.
    /// Then removes all these 'dark...' attributes, they are not part of the xshd schema.
    let applyDarkAttributes (dark:bool) (doc:XmlDocument) =
        let isDarkVersion (name:string) = name.Length > 4 && name.StartsWith "dark" && Char.IsUpper name.[4]
        for el in doc.GetElementsByTagName "*" |> Seq.cast<XmlElement> do
            let darkAttributes = el.Attributes |> Seq.cast<XmlAttribute> |> Seq.filter (fun a -> isDarkVersion a.LocalName) |> Seq.toArray
            for a in darkAttributes do
                if dark then
                    let n = a.LocalName.Substring 4
                    el.SetAttribute(string (Char.ToLowerInvariant n.[0]) + n.Substring 1, a.Value)
                el.Attributes.Remove a |> ignore

    type private XshdColors = {
        editor   : EditorColors
        log      : LogWindowColors
        /// False if the file could not be read
        ok       : bool
        problems : string list
        }

    /// Reads the 'Editor.' and 'Log.' colors from the xshd file for the light or the dark theme.
    /// A color is taken from the 'foreground' attribute, or from the 'background' attribute if there is no 'foreground'.
    /// A missing color is magenta and gets reported in problems.
    let private readColors (dark:bool) : XshdColors =
        let problems = ResizeArray<string>()
        let colors   = Dictionary<string, SolidColorBrush>()
        let ok =
            try
                let doc = XmlDocument()
                doc.Load xshdPath
                applyDarkAttributes dark doc
                for el in doc.GetElementsByTagName "Color" |> Seq.cast<XmlElement> do
                    let name = el.GetAttribute "name"
                    if name.StartsWith "Editor." || name.StartsWith "Log." then
                        let value = match el.GetAttribute "foreground" with | "" -> el.GetAttribute "background" | v -> v
                        if value <> "" then
                            try colors.[name] <- hex value
                            with _ -> problems.Add $"The color '{name}' in SyntaxHighlightingFSharp.xshd has an invalid value: '{value}'"
                true
            with e ->
                problems.Add $"Failed to read the colors of the editor and the Log from {xshdPath}: {e.Message}"
                false
        let missing = hex "#FF00FF"
        let get (name:string) =
            match colors.TryGetValue name with
            | true, b -> b
            | _ ->
                if ok then problems.Add $"The color '{name}' is missing in the syntax highlighting file SyntaxHighlightingFSharp.xshd"
                missing
        let opt (name:string) =
            match colors.TryGetValue name with
            | true, b -> b
            | _ -> null
        let editor : EditorColors = {
            background          = get "Editor.Background"
            foreground          = get "Editor.Foreground"
            currentLine         = get "Editor.CurrentLine"
            link                = get "Editor.Link"
            margin              = get "Editor.Margin"
            lineNumbers         = get "Editor.LineNumbers"
            currentLineNumber   = get "Editor.CurrentLineNumber"
            columnRuler         = get "Editor.ColumnRuler"
            foldingMarker       = get "Editor.FoldingMarker"
            foldingMarkerBg     = get "Editor.FoldingMarkerBg"
            foldingMarkerSel    = get "Editor.FoldingMarkerSelected"
            foldingMarkerSelBg  = get "Editor.FoldingMarkerSelectedBg"
            searchMarker        = get "Editor.SearchMarker"
            selection           = opt "Editor.Selection"
            selectionHighlight  = get "Editor.SelectionHighlight"
            evaluatedBg         = get "Editor.EvaluatedBg"
            evaluatedBorder     = get "Editor.EvaluatedBorder" |> pen 1.0
            bracketPair         = get "Editor.BracketPair"
            bracketError        = get "Editor.BracketError"
            bracketErrorBg      = get "Editor.BracketErrorBg"
            bracketNesting      = [| null; get "Editor.BracketNesting1"; get "Editor.BracketNesting2"; get "Editor.BracketNesting3"; get "Editor.BracketNesting4" |]
            }
        let log : LogWindowColors = {
            background          = get "Log.Background"
            foreground          = get "Log.Foreground"
            link                = get "Log.Link"
            border              = get "Log.Border"
            selectionHighlight  = get "Log.SelectionHighlight"
            margin              = get "Log.Margin"
            lineNumbers         = get "Log.LineNumbers"
            }
        { editor = editor; log = log; ok = ok; problems = List.ofSeq problems }

    // -------------------- Errors --------------------

    let lightErrors : ErrorColors =
        let err  = Brushes.Red   |> darker 10  |> freeze
        let warn = Brushes.Gold
        let info = Brushes.Green |> darker 5   |> freeze
        {
        errBackGr           = Brushes.Red    |> brighter 230 |> freeze
        errSquiggle         = err
        errSquigglePen      = squiggle err
        warnBackGr          = Brushes.Yellow |> brighter 200 |> freeze
        warnSquiggle        = warn
        warnSquigglePen     = squiggle warn
        infoBackGr          = Brushes.Green  |> brighter 220 |> freeze
        infoSquiggle        = info
        infoSquigglePen     = squiggle info
        }

    let darkErrors : ErrorColors =
        let err  = hex "#F14C4C"
        let warn = hex "#CCA700"
        let info = hex "#3FA53F"
        {
        errBackGr           = hex "#3C1F1F"
        errSquiggle         = err
        errSquigglePen      = squiggle err
        warnBackGr          = hex "#38361C"
        warnSquiggle        = warn
        warnSquigglePen     = squiggle warn
        infoBackGr          = hex "#1E3522"
        infoSquiggle        = info
        infoSquigglePen     = squiggle info
        }

    // -------------------- Log --------------------

    /// The Log uses these brushes for printing in both themes.
    /// In the dark theme they get replaced by the ones from darkLogText when rendering.
    let lightLogText : LogTextColors = {
        consoleOut          = Brushes.Black                     |> copy
        fsiStdOut           = Brushes.DarkGray    |> darker 20  |> freeze
        fsiErrorOut         = Brushes.DarkMagenta               |> copy
        consoleError        = Brushes.OrangeRed                 |> copy
        infoMsg             = Brushes.LightSteelBlue            |> copy
        fsiErrorMsg         = Brushes.Magenta                   |> copy
        appErrorMsg         = Brushes.LightSalmon |> darker 20  |> freeze
        iOErrorMsg          = Brushes.DarkRed                   |> copy
        debugMsg            = Brushes.LightSeaGreen             |> copy
        runtimeErr          = Brushes.Red         |> darker 55  |> freeze
        }

    let darkLogText : LogTextColors = {
        consoleOut          = hex "#D4D4D4"
        fsiStdOut           = hex "#8C8C8C"
        fsiErrorOut         = hex "#D670D6"
        consoleError        = hex "#FF7043"
        infoMsg             = hex "#6F86A6"
        fsiErrorMsg         = hex "#FF70FF"
        appErrorMsg         = hex "#E8906C"
        iOErrorMsg          = hex "#E85A5A"
        debugMsg            = hex "#3CC4BA"
        runtimeErr          = hex "#FF5C5C"
        }

    // -------------------- Status bar --------------------

    let lightStatusBar : StatusBarColors = {
        err                 = Brushes.Red     |> brighter 160 |> freeze
        warn                = Brushes.Yellow  |> brighter 40  |> freeze
        ok                  = Brushes.Green   |> brighter 140 |> freeze
        active              = Brushes.Orange  |> brighter 20  |> freeze
        compile             = Brushes.Magenta
        wait                = Brushes.HotPink |> brighter 80  |> freeze
        grayText            = Brushes.Gray    |> darker 60    |> freeze
        inactiveText        = Brushes.Gray
        }

    let darkStatusBar : StatusBarColors = {
        err                 = hex "#6E2A2A"
        warn                = hex "#5E5718"
        ok                  = hex "#2A5A2A"
        active              = hex "#7A5210"
        compile             = hex "#7A2A7A"
        wait                = hex "#5E3458"
        grayText            = hex "#B0B0B0"
        inactiveText        = hex "#808080"
        }

    // -------------------- Tabs --------------------

    let lightTabs : TabColors = {
        saved               = Brushes.Black
        changed             = Brushes.Red  |> darker 90   |> freeze
        deleted             = Brushes.Red  |> darker 20   |> freeze
        unsaved             = Brushes.Gray |> brighter 40 |> freeze
        border              = Brushes.Black
        closeButton         = ofRGB 150 150 150 // for gray cross inside red button
        closeButtonHover    = ofRGB 232 17 35   // same red color as default for the main window
        }

    let darkTabs : TabColors = {
        saved               = hex "#D4D4D4"
        changed             = hex "#F07878"
        deleted             = hex "#E05050"
        unsaved             = hex "#8A8A8A"
        border              = hex "#3F3F46"
        closeButton         = hex "#A0A0A0"
        closeButtonHover    = hex "#E81123"
        }

    // -------------------- ToolTips --------------------

    let lightToolTip : ToolTipColors = {
        text                = Brushes.Black
        code                = Brushes.Brown
        codeBg              = Brushes.White         |> darker 5    |> freeze
        gray                = Brushes.Gray
        errMsg              = Brushes.LightGray
        purple              = Brushes.Purple        |> brighter 40 |> freeze
        blue                = Brushes.Blue          |> darker 90   |> freeze
        red                 = Brushes.DarkSalmon    |> darker 120  |> freeze
        fullRed             = Brushes.Red           |> darker 60   |> freeze
        cyan                = Brushes.DarkCyan      |> darker 60   |> freeze
        darkGray            = Brushes.Gray          |> darker 40   |> freeze
        darkBlue            = Brushes.DarkSlateBlue |> darker 20   |> freeze
        glyph               = Brushes.DarkOrange    |> darker 10   |> freeze
        border              = Brushes.LightGray
        }

    let darkToolTip : ToolTipColors = {
        text                = hex "#D4D4D4"
        code                = hex "#D69D85"
        codeBg              = hex "#2D2D2D"
        gray                = hex "#9A9A9A"
        errMsg              = hex "#707070"
        purple              = hex "#C586C0"
        blue                = hex "#6A9CE6"
        red                 = hex "#E2836A"
        fullRed             = hex "#F06060"
        cyan                = hex "#4EC9B0"
        darkGray            = hex "#A6A6A6"
        darkBlue            = hex "#A0A8F0"
        glyph               = hex "#E8A040"
        border              = hex "#454545"
        }

    // -------------------- NuGet hints --------------------

    let lightNugetHints : NugetHintColors = {
        upToDateFg          = ofRGB  20 150  50
        notFoundFg          = ofRGB 150 150 150
        checkingFg          = ofRGB 110 130 160
        updateFg            = ofRGB 170  85   0
        updateBg            = ofRGB 255 240 215
        updateBgHover       = ofRGB 255 220 165
        pinFg               = ofRGB  40  90 160
        pinBg               = ofRGB 228 238 252
        pinBgHover          = ofRGB 200 220 248
        linkFg              = ofRGB 120 130 150
        linkFgHover         = ofRGB  30  90 200
        }

    let darkNugetHints : NugetHintColors = {
        upToDateFg          = ofRGB  90 190 100
        notFoundFg          = ofRGB 140 140 140
        checkingFg          = ofRGB 150 165 190
        updateFg            = ofRGB 235 165  90
        updateBg            = ofRGB  75  52  25
        updateBgHover       = ofRGB 105  72  32
        pinFg               = ofRGB 120 170 240
        pinBg               = ofRGB  32  48  75
        pinBgHover          = ofRGB  45  68 105
        linkFg              = ofRGB 140 150 170
        linkFgHover         = ofRGB 100 160 250
        }

    // -------------------- WPF controls --------------------

    let darkChrome : ChromeColors = {
        background          = hex "#2D2D30"
        hover               = hex "#3E3E42"
        pressed             = hex "#505055"
        popup               = hex "#252526"
        border              = hex "#3F3F46"
        text                = hex "#E8E8E8"
        textDim             = hex "#9D9D9D"
        textDisabled        = hex "#6D6D6D"
        accent              = hex "#3794FF"
        highlight           = hex "#094771"
        scrollThumb         = hex "#4A4A4A"
        scrollThumbHover    = hex "#5E5E5E"
        scrollThumbDrag     = hex "#7A7A7A"
        tabHover            = hex "#3A3A3D"
        inputBg             = hex "#3C3C3C"
        inputBorder         = hex "#555555"
        buttonBg            = hex "#3A3A3D"
        }

    // -------------------- current theme --------------------

    /// The key in the settings file
    let SettingsStr = "DarkTheme"

    let private lightColors = readColors false

    let mutable isDark      = false
    let mutable editor      = lightColors.editor
    let mutable errors      = lightErrors
    let mutable log         = lightColors.log
    let mutable statusBar   = lightStatusBar
    let mutable tabs        = lightTabs
    let mutable toolTip     = lightToolTip
    let mutable nugetHints  = lightNugetHints

    let private changedEv = new Event<unit>()

    /// Raised after the theme was switched between light and dark
    [<CLIEvent>]
    let Changed = changedEv.Publish

    /// Problems found when the colors were read from the xshd file the last time, e.g. missing colors.
    /// Empty if all colors were read.
    let mutable colorProblems = lightColors.problems

    /// Switch between light and dark theme. Call from the UI thread.
    /// Raises the Changed event if the theme is different from the current one.
    let set (dark:bool) =
        if dark <> isDark then
            let c = if dark then readColors true else lightColors
            colorProblems <- c.problems
            isDark      <- dark
            editor      <- c.editor
            errors      <- if dark then darkErrors     else lightErrors
            log         <- c.log
            statusBar   <- if dark then darkStatusBar  else lightStatusBar
            tabs        <- if dark then darkTabs       else lightTabs
            toolTip     <- if dark then darkToolTip    else lightToolTip
            nugetHints  <- if dark then darkNugetHints else lightNugetHints
            changedEv.Trigger()

    /// Reads the colors of the editor and the Log window from the xshd file again, e.g. after it was edited.
    /// Raises the Changed event. Keeps the current colors if the file can not be read. Call from the UI thread.
    /// Returns the problems found, see colorProblems.
    let reloadColors () : string list =
        let c = readColors isDark
        colorProblems <- c.problems
        if c.ok then
            editor <- c.editor
            log    <- c.log
            changedEv.Trigger()
        c.problems

    /// For colors that come from outside of Fesh, e.g. the ones printed with a custom RGB color to the log.
    /// In the dark theme, dark colors get lighter, so that they are readable on a dark background.
    /// The hue stays the same.
    let adaptToDark (c:Color) : Color =
        let r = float c.R / 255.0
        let g = float c.G / 255.0
        let b = float c.B / 255.0
        let mx = max r (max g b)
        let mn = min r (min g b)
        let l = (mx + mn) * 0.5
        let l2 = max l (max 0.62 (min 0.85 (1.0 - l))) // flip the lightness of dark colors
        if l2 = l then
            c
        else
            // HSL to RGB with the same hue and saturation:
            let d = mx - mn
            let s = if d = 0.0 then 0.0 elif l > 0.5 then d / (2.0 - mx - mn) else d / (mx + mn)
            let h =
                if   d = 0.0 then 0.0
                elif mx = r  then ((g - b) / d + (if g < b then 6.0 else 0.0)) / 6.0
                elif mx = g  then ((b - r) / d + 2.0) / 6.0
                else              ((r - g) / d + 4.0) / 6.0
            let q = if l2 < 0.5 then l2 * (1.0 + s) else l2 + s - l2 * s
            let p = 2.0 * l2 - q
            let hue2rgb (t:float) =
                let t = if t < 0.0 then t + 1.0 elif t > 1.0 then t - 1.0 else t
                if   t < 1.0/6.0 then p + (q - p) * 6.0 * t
                elif t < 0.5     then q
                elif t < 2.0/3.0 then p + (q - p) * (2.0/3.0 - t) * 6.0
                else                  p
            let toByte (v:float) = byte (Math.Round(v * 255.0))
            Color.FromArgb(c.A, toByte (hue2rgb (h + 1.0/3.0)), toByte (hue2rgb h), toByte (hue2rgb (h - 1.0/3.0)))
