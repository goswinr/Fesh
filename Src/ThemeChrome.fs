namespace Fesh

open System
open System.Windows
open System.Windows.Controls
open System.Windows.Interop
open System.Windows.Markup
open System.Windows.Media
open System.Runtime.InteropServices
open Fesh.Util.General

/// Applies the dark theme to the WPF controls: menus, tabs, scrollbars, tooltips, status bar, ...
/// In the light theme the default WPF styles are used.
/// The resources are only added to the windows and tooltips of Fesh, not to the Application, so a hosting app is not affected.
module ThemeChrome =

    module private Native =
        [<DllImport("dwmapi.dll")>]
        extern int DwmSetWindowAttribute(nativeint hwnd, int attr, int& attrValue, int attrSize)

        [<DllImport("user32.dll")>]
        extern bool SetWindowPos(nativeint hWnd, nativeint hWndInsertAfter, int x, int y, int cx, int cy, uint32 uFlags)

    /// The styles for the dark theme, from ThemeChrome.xaml (an embedded resource, F# can't compile XAML).
    /// The colors are referenced as DynamicResource, they are added from Theme.darkChrome in brushes() below.
    let private loadStyles () =
        use stream = Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream "ThemeChrome.xaml"
        XamlReader.Load stream :?> ResourceDictionary

    /// The brushes used by the styles in ThemeChrome.xaml.
    /// Also overrides the system colors, they are used by many default WPF styles and by AvalonEdit.
    let private brushes (c:ChromeColors) (e:EditorColors) =
        let d = ResourceDictionary()
        let add (key:obj) (b:SolidColorBrush) = d.[key] <- b
        add "Fesh.Background"   c.background
        add "Fesh.Hover"        c.hover
        add "Fesh.Pressed"      c.pressed
        add "Fesh.Popup"        c.popup
        add "Fesh.Border"       c.border
        add "Fesh.Text"         c.text
        add "Fesh.TextDim"      c.textDim
        add "Fesh.TextDisabled" c.textDisabled
        add "Fesh.Accent"       c.accent
        add "Fesh.Thumb"        c.scrollThumb
        add "Fesh.ThumbHover"   c.scrollThumbHover
        add "Fesh.ThumbDrag"    c.scrollThumbDrag
        add "Fesh.TabHover"     c.tabHover
        add "Fesh.InputBg"      c.inputBg
        add "Fesh.InputBorder"  c.inputBorder
        add "Fesh.ButtonBg"     c.buttonBg
        add "Fesh.EditorBg"     e.background

        add SystemColors.WindowBrushKey                         e.background
        add SystemColors.WindowTextBrushKey                     e.foreground
        add SystemColors.ControlBrushKey                        c.background
        add SystemColors.ControlTextBrushKey                    c.text
        add SystemColors.ControlLightBrushKey                   c.hover
        add SystemColors.ControlLightLightBrushKey              c.pressed
        add SystemColors.ControlDarkBrushKey                    c.border
        add SystemColors.ControlDarkDarkBrushKey                c.popup
        add SystemColors.GrayTextBrushKey                       c.textDisabled
        add SystemColors.HighlightBrushKey                      c.highlight
        add SystemColors.HighlightTextBrushKey                  c.text
        add SystemColors.InactiveSelectionHighlightBrushKey     c.hover
        add SystemColors.InactiveSelectionHighlightTextBrushKey c.text
        add SystemColors.MenuBrushKey                           c.popup
        add SystemColors.MenuBarBrushKey                        c.background
        add SystemColors.MenuTextBrushKey                       c.text
        add SystemColors.MenuHighlightBrushKey                  c.hover
        add SystemColors.InfoBrushKey                           c.popup
        add SystemColors.InfoTextBrushKey                       c.text
        add SystemColors.WindowFrameBrushKey                    c.border
        add SystemColors.ActiveBorderBrushKey                   c.border
        d.[SystemColors.HighlightColorKey]   <- c.highlight.Color
        d.[SystemColors.WindowColorKey]      <- e.background.Color
        d.[SystemColors.ControlColorKey]     <- c.background.Color
        d.[SystemColors.ControlTextColorKey] <- c.text.Color
        d

    let mutable private darkDict : ResourceDictionary = null

    /// The resources for the dark theme
    let darkResources () =
        if isNull darkDict then
            let d = ResourceDictionary()
            d.MergedDictionaries.Add(brushes Theme.darkChrome Theme.darkEditor)
            d.MergedDictionaries.Add(loadStyles())
            darkDict <- d
        darkDict

    /// Use the dark title bar of Windows 10 and 11
    let private setTitleBar (w:Window) =
        let apply () =
            let hwnd = WindowInteropHelper(w).Handle
            if hwnd <> IntPtr.Zero then
                try
                    let mutable v = if Theme.isDark then 1 else 0
                    if Native.DwmSetWindowAttribute(hwnd, 20, &v, 4) <> 0 then // DWMWA_USE_IMMERSIVE_DARK_MODE
                        Native.DwmSetWindowAttribute(hwnd, 19, &v, 4) |> ignore // the same on Windows 10 before build 19041
                    // redraw the title bar: SWP_NOSIZE ||| SWP_NOMOVE ||| SWP_NOZORDER ||| SWP_NOACTIVATE ||| SWP_FRAMECHANGED
                    Native.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0001u ||| 0x0002u ||| 0x0004u ||| 0x0010u ||| 0x0020u) |> ignore
                with _ ->
                    () // e.g. on Windows 7
        if WindowInteropHelper(w).Handle = IntPtr.Zero then
            w.SourceInitialized.Add(fun _ -> apply())
        else
            apply()

    let private applyTo (fe:FrameworkElement) (lightBg:Brush) (darkBg:Brush) =
        let merged = fe.Resources.MergedDictionaries
        if Theme.isDark then
            let d = darkResources()
            if not (merged.Contains d) then merged.Add d
        elif notNull darkDict then
            merged.Remove darkDict |> ignore

        match fe with
        | :? ToolTip as t ->
            // a ToolTip that is opened from code has no parent to find its implicit style
            if Theme.isDark then t.Style <- darkResources().[typeof<ToolTip>] :?> Style
            else t.ClearValue FrameworkElement.StyleProperty
        | :? Window as w ->
            if Theme.isDark then
                w.Background <- darkBg
                w.Foreground <- Theme.darkChrome.text
            else
                if isNull lightBg then w.ClearValue Control.BackgroundProperty else w.Background <- lightBg
                w.ClearValue Control.ForegroundProperty
            setTitleBar w
        | _ -> ()

    [<NoEquality; NoComparison>]
    type private Tracked = {
        element : WeakReference<FrameworkElement>
        lightBg : Brush
        darkBg  : Brush
        }

    /// Weak references, so that closed windows and tooltips can be garbage collected
    let private tracked = ResizeArray<Tracked>()

    let private isAlive (t:Tracked) =
        match t.element.TryGetTarget() with
        | true, _ -> true
        | _ -> false

    let private track (fe:FrameworkElement) lightBg darkBg =
        if tracked.Count > 0 && tracked.Count % 32 = 0 then
            tracked.RemoveAll(fun t -> not (isAlive t)) |> ignore
        tracked.Add { element = WeakReference<FrameworkElement>(fe); lightBg = lightBg; darkBg = darkBg }
        applyTo fe lightBg darkBg

    do
        Theme.Changed.Add (fun () ->
            tracked.RemoveAll(fun t ->
                match t.element.TryGetTarget() with
                | true, fe -> applyTo fe t.lightBg t.darkBg; false
                | _ -> true
                ) |> ignore
            )

    /// Applies the current theme to the given element and its children, also on later theme changes.
    /// Needed for elements that are not inside a registered window, like ToolTips that are opened from code.
    let register (fe:FrameworkElement) =
        track fe null null

    /// Applies the current theme to the given window, its title bar and its children, also on later theme changes.
    /// lightBg may be null to use the default window background.
    let registerWindow (w:Window) (lightBg:Brush) (darkBg:Brush) =
        track w lightBg darkBg
