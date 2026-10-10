namespace Fesh

open System
open System.Windows.Media
open AvalonLog.Brush

// All colors of Fesh, except the ones for syntax highlighting.
// The syntax highlighting colors (including the semantic highlighting) are in SyntaxHighlightingFSharp.xshd.
// The dark versions of the WPF controls (menus, tabs, scrollbars, ...) are in ThemeChrome.fs.

/// Colors of the code editor that are not part of the syntax highlighting
type EditorColors = {
    background          : SolidColorBrush
    foreground          : SolidColorBrush
    currentLine         : SolidColorBrush
    link                : SolidColorBrush
    /// Background of the line number and folding margin
    margin              : SolidColorBrush
    lineNumbers         : SolidColorBrush
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

/// Colors of the Log window, not of the printed text
type LogWindowColors = {
    background          : SolidColorBrush
    foreground          : SolidColorBrush
    link                : SolidColorBrush
    border              : SolidColorBrush
    /// Background of all other occurrences of the selected word
    selectionHighlight  : SolidColorBrush
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

    // -------------------- Editor --------------------

    let lightEditor : EditorColors = {
        background          = Brushes.White
        foreground          = Brushes.Black
        currentLine         = Brushes.LightSteelBlue |> brighter 70 |> freeze
        link                = Brushes.DarkGreen
        margin              = Brushes.White          |> darker 8    |> freeze
        lineNumbers         = Brushes.Gray
        columnRuler         = Brushes.White          |> darker 24   |> freeze
        foldingMarker       = Brushes.Gray
        foldingMarkerBg     = Brushes.White
        foldingMarkerSel    = Brushes.Black
        foldingMarkerSelBg  = Brushes.White
        searchMarker        = Brushes.LightGreen
        selection           = null
        selectionHighlight  = Brushes.PaleTurquoise  |> brighter 30  |> freeze
        evaluatedBg         = Brushes.Gray           |> brighter 110 |> freeze
        evaluatedBorder     = Brushes.Gray           |> darker 20    |> freeze |> pen 1.0
        bracketPair         = Brushes.Green          |> brighter 160 |> freeze
        bracketError        = Brushes.Red
        bracketErrorBg      = ofARGB 90 255 150 0
        bracketNesting      = [|
                                null
                                Brushes.Purple |> brighter 40 |> freeze
                                Brushes.Orange |> darker 30   |> freeze
                                Brushes.Green  |> brighter 30 |> freeze
                                Brushes.Cyan   |> darker 40   |> freeze
                                |]
        }

    let darkEditor : EditorColors = {
        background          = hex "#1E1E1E"
        foreground          = hex "#D4D4D4"
        currentLine         = hex "#2A2D2E"
        link                = hex "#5DBB63"
        margin              = hex "#252526"
        lineNumbers         = hex "#858585"
        columnRuler         = hex "#383838"
        foldingMarker       = hex "#808080"
        foldingMarkerBg     = hex "#252526"
        foldingMarkerSel    = hex "#D4D4D4"
        foldingMarkerSelBg  = hex "#252526"
        searchMarker        = hex "#3B5E3B"
        selection           = hex "#264F78"
        selectionHighlight  = hex "#234B50"
        evaluatedBg         = hex "#2A2A2A"
        evaluatedBorder     = hex "#505050" |> pen 1.0
        bracketPair         = hex "#2D5A2D"
        bracketError        = hex "#FF5050"
        bracketErrorBg      = hex "#6EC85A00"
        bracketNesting      = [|
                                null
                                hex "#D070D0"
                                hex "#E5A040"
                                hex "#4CC04C"
                                hex "#30C8D0"
                                |]
        }

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

    let lightLog : LogWindowColors = {
        background          = Brushes.White
        foreground          = Brushes.Black
        link                = Brushes.Blue
        border              = Brushes.Black
        selectionHighlight  = Brushes.Blue |> brighter 220 |> freeze
        }

    let darkLog : LogWindowColors = {
        background          = hex "#1B1B1C"
        foreground          = hex "#D4D4D4"
        link                = hex "#4FA3FF"
        border              = hex "#3F3F46"
        selectionHighlight  = hex "#2E2E5E"
        }

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

    let mutable isDark      = false
    let mutable editor      = lightEditor
    let mutable errors      = lightErrors
    let mutable log         = lightLog
    let mutable statusBar   = lightStatusBar
    let mutable tabs        = lightTabs
    let mutable toolTip     = lightToolTip
    let mutable nugetHints  = lightNugetHints

    let private changedEv = new Event<unit>()

    /// Raised after the theme was switched between light and dark
    [<CLIEvent>]
    let Changed = changedEv.Publish

    /// Switch between light and dark theme. Call from the UI thread.
    /// Raises the Changed event if the theme is different from the current one.
    let set (dark:bool) =
        if dark <> isDark then
            isDark      <- dark
            editor      <- if dark then darkEditor     else lightEditor
            errors      <- if dark then darkErrors     else lightErrors
            log         <- if dark then darkLog        else lightLog
            statusBar   <- if dark then darkStatusBar  else lightStatusBar
            tabs        <- if dark then darkTabs       else lightTabs
            toolTip     <- if dark then darkToolTip    else lightToolTip
            nugetHints  <- if dark then darkNugetHints else lightNugetHints
            changedEv.Trigger()

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
