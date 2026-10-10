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

    /// The styles for the dark theme.
    /// The colors are referenced as DynamicResource, they are added from Theme.darkChrome in brushes() below.
    let private stylesXaml = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:cc="clr-namespace:AvalonEditB.CodeCompletion;assembly=AvalonEditB">

    <!-- ScrollBar: a thin thumb, no arrow buttons -->
    <Style x:Key="Fesh.ScrollBarPageButton" TargetType="{x:Type RepeatButton}">
        <Setter Property="OverridesDefaultStyle" Value="True"/>
        <Setter Property="Focusable" Value="False"/>
        <Setter Property="IsTabStop" Value="False"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="{x:Type RepeatButton}">
                    <Rectangle Fill="Transparent"/>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key="Fesh.ScrollBarThumb" TargetType="{x:Type Thumb}">
        <Setter Property="OverridesDefaultStyle" Value="True"/>
        <Setter Property="IsTabStop" Value="False"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="{x:Type Thumb}">
                    <Border x:Name="Bd" Background="{DynamicResource Fesh.Thumb}" Margin="3"/>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Bd" Property="Background" Value="{DynamicResource Fesh.ThumbHover}"/>
                        </Trigger>
                        <Trigger Property="IsDragging" Value="True">
                            <Setter TargetName="Bd" Property="Background" Value="{DynamicResource Fesh.ThumbDrag}"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <ControlTemplate x:Key="Fesh.VerticalScrollBar" TargetType="{x:Type ScrollBar}">
        <Grid Background="{TemplateBinding Background}" SnapsToDevicePixels="True">
            <Track x:Name="PART_Track" IsDirectionReversed="True">
                <Track.DecreaseRepeatButton>
                    <RepeatButton Style="{StaticResource Fesh.ScrollBarPageButton}" Command="{x:Static ScrollBar.PageUpCommand}"/>
                </Track.DecreaseRepeatButton>
                <Track.IncreaseRepeatButton>
                    <RepeatButton Style="{StaticResource Fesh.ScrollBarPageButton}" Command="{x:Static ScrollBar.PageDownCommand}"/>
                </Track.IncreaseRepeatButton>
                <Track.Thumb>
                    <Thumb Style="{StaticResource Fesh.ScrollBarThumb}"/>
                </Track.Thumb>
            </Track>
        </Grid>
    </ControlTemplate>

    <ControlTemplate x:Key="Fesh.HorizontalScrollBar" TargetType="{x:Type ScrollBar}">
        <Grid Background="{TemplateBinding Background}" SnapsToDevicePixels="True">
            <Track x:Name="PART_Track" IsDirectionReversed="False">
                <Track.DecreaseRepeatButton>
                    <RepeatButton Style="{StaticResource Fesh.ScrollBarPageButton}" Command="{x:Static ScrollBar.PageLeftCommand}"/>
                </Track.DecreaseRepeatButton>
                <Track.IncreaseRepeatButton>
                    <RepeatButton Style="{StaticResource Fesh.ScrollBarPageButton}" Command="{x:Static ScrollBar.PageRightCommand}"/>
                </Track.IncreaseRepeatButton>
                <Track.Thumb>
                    <Thumb Style="{StaticResource Fesh.ScrollBarThumb}"/>
                </Track.Thumb>
            </Track>
        </Grid>
    </ControlTemplate>

    <Style TargetType="{x:Type ScrollBar}">
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="Stylus.IsFlicksEnabled" Value="False"/>
        <Setter Property="Width" Value="14"/>
        <Setter Property="MinWidth" Value="14"/>
        <Setter Property="Template" Value="{StaticResource Fesh.VerticalScrollBar}"/>
        <Style.Triggers>
            <Trigger Property="Orientation" Value="Horizontal">
                <Setter Property="Width" Value="Auto"/>
                <Setter Property="MinWidth" Value="0"/>
                <Setter Property="Height" Value="14"/>
                <Setter Property="MinHeight" Value="14"/>
                <Setter Property="Template" Value="{StaticResource Fesh.HorizontalScrollBar}"/>
            </Trigger>
        </Style.Triggers>
    </Style>

    <!-- Menu -->
    <Style TargetType="{x:Type Menu}">
        <Setter Property="Background" Value="{DynamicResource Fesh.Background}"/>
        <Setter Property="Foreground" Value="{DynamicResource Fesh.Text}"/>
    </Style>

    <Style x:Key="{x:Static MenuItem.SeparatorStyleKey}" TargetType="{x:Type Separator}">
        <Setter Property="OverridesDefaultStyle" Value="True"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="{x:Type Separator}">
                    <Border Height="1" Margin="28,3,4,3" Background="{DynamicResource Fesh.Border}"/>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <ControlTemplate x:Key="Fesh.TopLevelMenuItem" TargetType="{x:Type MenuItem}">
        <Border x:Name="Bd" Background="{TemplateBinding Background}" SnapsToDevicePixels="True">
            <Grid>
                <ContentPresenter ContentSource="Header" RecognizesAccessKey="True" Margin="7,2,7,3" VerticalAlignment="Center"/>
                <Popup x:Name="PART_Popup" Placement="Bottom" AllowsTransparency="True" Focusable="False" PopupAnimation="None">
                    <Popup.IsOpen>
                        <Binding Path="IsSubmenuOpen" RelativeSource="{RelativeSource TemplatedParent}"/>
                    </Popup.IsOpen>
                    <Border Background="{DynamicResource Fesh.Popup}" BorderBrush="{DynamicResource Fesh.Border}" BorderThickness="1" Padding="2">
                        <ScrollViewer VerticalScrollBarVisibility="Auto">
                            <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Cycle" KeyboardNavigation.TabNavigation="Cycle" Grid.IsSharedSizeScope="True"/>
                        </ScrollViewer>
                    </Border>
                </Popup>
            </Grid>
        </Border>
        <ControlTemplate.Triggers>
            <Trigger Property="IsHighlighted" Value="True">
                <Setter TargetName="Bd" Property="Background" Value="{DynamicResource Fesh.Hover}"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
                <Setter Property="Foreground" Value="{DynamicResource Fesh.TextDisabled}"/>
            </Trigger>
        </ControlTemplate.Triggers>
    </ControlTemplate>

    <ControlTemplate x:Key="Fesh.SubMenuItem" TargetType="{x:Type MenuItem}">
        <Border x:Name="Bd" Background="{TemplateBinding Background}" SnapsToDevicePixels="True">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto" MinWidth="24" SharedSizeGroup="FeshMenuIcon"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto" SharedSizeGroup="FeshMenuGesture"/>
                    <ColumnDefinition Width="16"/>
                </Grid.ColumnDefinitions>
                <ContentPresenter x:Name="Icon" ContentSource="Icon" Margin="4,0" VerticalAlignment="Center" HorizontalAlignment="Center"/>
                <Path x:Name="CheckMark" Data="M 0,4 L 3,7 L 9,0" Stroke="{TemplateBinding Foreground}" StrokeThickness="1.5"
                      VerticalAlignment="Center" HorizontalAlignment="Center" Visibility="Collapsed"/>
                <ContentPresenter Grid.Column="1" ContentSource="Header" RecognizesAccessKey="True" Margin="4,3,8,3" VerticalAlignment="Center"/>
                <TextBlock x:Name="Gesture" Grid.Column="2" Text="{TemplateBinding InputGestureText}" Foreground="{DynamicResource Fesh.TextDim}"
                           Margin="16,3,4,3" VerticalAlignment="Center"/>
                <Path x:Name="Arrow" Grid.Column="3" Data="M 0,0 L 4,3.5 L 0,7 Z" Fill="{TemplateBinding Foreground}"
                      VerticalAlignment="Center" HorizontalAlignment="Center" Visibility="Collapsed"/>
                <Popup x:Name="PART_Popup" Placement="Right" HorizontalOffset="-1" VerticalOffset="-3" AllowsTransparency="True" Focusable="False" PopupAnimation="None">
                    <Popup.IsOpen>
                        <Binding Path="IsSubmenuOpen" RelativeSource="{RelativeSource TemplatedParent}"/>
                    </Popup.IsOpen>
                    <Border Background="{DynamicResource Fesh.Popup}" BorderBrush="{DynamicResource Fesh.Border}" BorderThickness="1" Padding="2">
                        <ScrollViewer VerticalScrollBarVisibility="Auto">
                            <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Cycle" KeyboardNavigation.TabNavigation="Cycle" Grid.IsSharedSizeScope="True"/>
                        </ScrollViewer>
                    </Border>
                </Popup>
            </Grid>
        </Border>
        <ControlTemplate.Triggers>
            <Trigger Property="Role" Value="SubmenuHeader">
                <Setter TargetName="Arrow" Property="Visibility" Value="Visible"/>
            </Trigger>
            <Trigger Property="IsChecked" Value="True">
                <Setter TargetName="CheckMark" Property="Visibility" Value="Visible"/>
            </Trigger>
            <Trigger Property="IsHighlighted" Value="True">
                <Setter TargetName="Bd" Property="Background" Value="{DynamicResource Fesh.Hover}"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
                <Setter Property="Foreground" Value="{DynamicResource Fesh.TextDisabled}"/>
                <Setter TargetName="Gesture" Property="Foreground" Value="{DynamicResource Fesh.TextDisabled}"/>
            </Trigger>
        </ControlTemplate.Triggers>
    </ControlTemplate>

    <Style TargetType="{x:Type MenuItem}">
        <Setter Property="Foreground" Value="{DynamicResource Fesh.Text}"/>
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="Template" Value="{StaticResource Fesh.SubMenuItem}"/>
        <Style.Triggers>
            <Trigger Property="Role" Value="TopLevelHeader">
                <Setter Property="Template" Value="{StaticResource Fesh.TopLevelMenuItem}"/>
            </Trigger>
            <Trigger Property="Role" Value="TopLevelItem">
                <Setter Property="Template" Value="{StaticResource Fesh.TopLevelMenuItem}"/>
            </Trigger>
        </Style.Triggers>
    </Style>

    <Style TargetType="{x:Type ContextMenu}">
        <Setter Property="Foreground" Value="{DynamicResource Fesh.Text}"/>
        <Setter Property="Background" Value="{DynamicResource Fesh.Popup}"/>
        <Setter Property="BorderBrush" Value="{DynamicResource Fesh.Border}"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="2"/>
        <Setter Property="Grid.IsSharedSizeScope" Value="True"/>
        <Setter Property="HasDropShadow" Value="False"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="{x:Type ContextMenu}">
                    <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}" Padding="{TemplateBinding Padding}">
                        <ScrollViewer VerticalScrollBarVisibility="Auto">
                            <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Cycle"/>
                        </ScrollViewer>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- ToolTip -->
    <Style TargetType="{x:Type ToolTip}">
        <Setter Property="Foreground" Value="{DynamicResource Fesh.Text}"/>
        <Setter Property="Background" Value="{DynamicResource Fesh.Popup}"/>
        <Setter Property="BorderBrush" Value="{DynamicResource Fesh.Border}"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="5,2,6,3"/>
        <Setter Property="HasDropShadow" Value="False"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="{x:Type ToolTip}">
                    <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}" Padding="{TemplateBinding Padding}" SnapsToDevicePixels="True">
                        <ContentPresenter/>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Tabs: only for TabStripPlacement Top -->
    <Style TargetType="{x:Type TabControl}">
        <Setter Property="Foreground" Value="{DynamicResource Fesh.Text}"/>
        <Setter Property="Background" Value="{DynamicResource Fesh.EditorBg}"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="{x:Type TabControl}">
                    <Grid KeyboardNavigation.TabNavigation="Local" SnapsToDevicePixels="True">
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="*"/>
                        </Grid.RowDefinitions>
                        <TabPanel x:Name="HeaderPanel" IsItemsHost="True" Panel.ZIndex="1" Margin="2,0,2,0" KeyboardNavigation.TabIndex="1"/>
                        <Border Grid.Row="1" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                                BorderThickness="{TemplateBinding BorderThickness}"
                                KeyboardNavigation.TabNavigation="Local" KeyboardNavigation.DirectionalNavigation="Contained" KeyboardNavigation.TabIndex="2">
                            <ContentPresenter x:Name="PART_SelectedContentHost" ContentSource="SelectedContent" Margin="{TemplateBinding Padding}"/>
                        </Border>
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style TargetType="{x:Type TabItem}">
        <Setter Property="Foreground" Value="{DynamicResource Fesh.Text}"/>
        <Setter Property="Background" Value="{DynamicResource Fesh.Background}"/>
        <Setter Property="BorderBrush" Value="{DynamicResource Fesh.Border}"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="{x:Type TabItem}">
                    <Grid x:Name="Root" Margin="0,2,0,0" SnapsToDevicePixels="True">
                        <Border x:Name="Bd" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                                BorderThickness="1,1,1,0" Margin="0,0,-1,0">
                            <ContentPresenter ContentSource="Header" RecognizesAccessKey="True" HorizontalAlignment="Center" VerticalAlignment="Center" Margin="2,0,2,0"/>
                        </Border>
                        <Border x:Name="Accent" Height="2" VerticalAlignment="Top" Margin="0,0,-1,0" Background="{DynamicResource Fesh.Accent}" Visibility="Collapsed"/>
                    </Grid>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Bd" Property="Background" Value="{DynamicResource Fesh.TabHover}"/>
                        </Trigger>
                        <Trigger Property="IsSelected" Value="True">
                            <Setter Property="Panel.ZIndex" Value="1"/>
                            <Setter TargetName="Root" Property="Margin" Value="0,0,0,-1"/>
                            <Setter TargetName="Bd" Property="Background" Value="{DynamicResource Fesh.EditorBg}"/>
                            <Setter TargetName="Accent" Property="Visibility" Value="Visible"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- StatusBar -->
    <Style TargetType="{x:Type StatusBar}">
        <Setter Property="Background" Value="{DynamicResource Fesh.Background}"/>
        <Setter Property="Foreground" Value="{DynamicResource Fesh.Text}"/>
    </Style>

    <Style x:Key="{x:Static StatusBar.SeparatorStyleKey}" TargetType="{x:Type Separator}">
        <Setter Property="OverridesDefaultStyle" Value="True"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="{x:Type Separator}">
                    <Border Width="1" Margin="2" Background="{DynamicResource Fesh.Border}"/>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style TargetType="{x:Type GridSplitter}">
        <Setter Property="Background" Value="{DynamicResource Fesh.Background}"/>
    </Style>

    <!-- Button, CheckBox and TextBox, e.g. for the search panel -->
    <Style TargetType="{x:Type Button}">
        <Setter Property="Foreground" Value="{DynamicResource Fesh.Text}"/>
        <Setter Property="Background" Value="{DynamicResource Fesh.ButtonBg}"/>
        <Setter Property="BorderBrush" Value="{DynamicResource Fesh.Border}"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Padding" Value="1"/>
        <Setter Property="HorizontalContentAlignment" Value="Center"/>
        <Setter Property="VerticalContentAlignment" Value="Center"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="{x:Type Button}">
                    <Border x:Name="Bd" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}" SnapsToDevicePixels="True">
                        <ContentPresenter Margin="{TemplateBinding Padding}" Focusable="False" RecognizesAccessKey="True"
                                          HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                                          VerticalAlignment="{TemplateBinding VerticalContentAlignment}"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Bd" Property="Background" Value="{DynamicResource Fesh.Hover}"/>
                        </Trigger>
                        <Trigger Property="IsPressed" Value="True">
                            <Setter TargetName="Bd" Property="Background" Value="{DynamicResource Fesh.Pressed}"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.5"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style TargetType="{x:Type CheckBox}">
        <Setter Property="Foreground" Value="{DynamicResource Fesh.Text}"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="{x:Type CheckBox}">
                    <StackPanel Orientation="Horizontal" Background="Transparent">
                        <Border x:Name="Box" Width="13" Height="13" VerticalAlignment="Center"
                                Background="{DynamicResource Fesh.InputBg}" BorderBrush="{DynamicResource Fesh.InputBorder}" BorderThickness="1">
                            <Path x:Name="Mark" Data="M 1.5,5.5 L 4.5,8.5 L 9.5,2" Stroke="{TemplateBinding Foreground}" StrokeThickness="1.6" Visibility="Collapsed"/>
                        </Border>
                        <ContentPresenter Margin="5,0,0,0" VerticalAlignment="Center" RecognizesAccessKey="True"/>
                    </StackPanel>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="Mark" Property="Visibility" Value="Visible"/>
                        </Trigger>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Box" Property="BorderBrush" Value="{DynamicResource Fesh.Accent}"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- the list in the completion window -->
    <Style TargetType="{x:Type cc:CompletionListBox}">
        <Setter Property="BorderBrush" Value="{DynamicResource Fesh.Border}"/>
    </Style>

    <Style TargetType="{x:Type TextBox}">
        <Setter Property="Background" Value="{DynamicResource Fesh.InputBg}"/>
        <Setter Property="Foreground" Value="{DynamicResource Fesh.Text}"/>
        <Setter Property="BorderBrush" Value="{DynamicResource Fesh.InputBorder}"/>
        <Setter Property="CaretBrush" Value="{DynamicResource Fesh.Text}"/>
        <Setter Property="SelectionBrush" Value="{DynamicResource Fesh.Accent}"/>
    </Style>

</ResourceDictionary>
"""

    /// The brushes used by the styles above.
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
            d.MergedDictionaries.Add(XamlReader.Parse stylesXaml :?> ResourceDictionary)
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
