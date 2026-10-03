namespace Fittings

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Layout


/// A Popup that looks like a ToolTip.
/// Other than with a ToolTip its position and its open state can be controlled explicitly.
/// (In WPF this was done with a ToolTip instance.)
/// The logicalParent is needed so that the styles of the application are applied to the content of the popup.
type TipPopup (logicalParent:Control) as this =
    inherit Primitives.Popup()

    let border =
        new Border(
            Background      = Media.Immutable.ImmutableSolidColorBrush(Media.Color.FromRgb(250uy, 250uy, 250uy)),
            BorderBrush     = Media.Immutable.ImmutableSolidColorBrush(Media.Color.FromRgb(118uy, 118uy, 118uy)),
            BorderThickness = Thickness 1.0,
            Padding         = Thickness 5.0
            )

    do
        this.Child <- border
        this.IsLightDismissEnabled <- false // stays open until closed explicitly
        (this :> ISetLogicalParent).SetParent logicalParent

    /// The control shown inside the border of this tooltip.
    member _.Content
        with get() : Control = border.Child
        and set (v:Control)  = border.Child <- v

    override _.StyleKeyOverride = typeof<Primitives.Popup> // see https://github.com/AvaloniaUI/Avalonia/discussions/18697


[<AutoOpen>]
module AutoOpenToolTip =

    type Control with
        member this.ToolTip
            with get()        = this.GetValue(ToolTip.TipProperty) :?> string
            and set(v:string) = this.SetValue(ToolTip.TipProperty,v) |> ignore
        member this.ToolTipControl
            with get()         = this.GetValue(ToolTip.TipProperty) :?> Control
            and set(v:Control) = this.SetValue(ToolTip.TipProperty,v) |> ignore

/// A module to provide DependencyProperties and their bindings
/// Includes extension methods for Button, Grid, TextBox
module DependencyProps = // TODO rename to Controls , clean up
    (*
    //---------- creating UIElements --------------

    // see: http://www.fssnip.net/4W/title/Calculator
    // http://trelford.com/blog/post/F-operator-overloads-for-WPF-dependency-properties.aspx
    // http://trelford.com/blog/post/Exposing-F-Dynamic-Lookup-to-C-WPF-Silverlight.aspx !!!

    type DependencyPropertyBindingPair(dp:DependencyProperty,binding:Data.BindingBase) =
        member this.Property = dp
        member this.Binding = binding
        static member ( <++> ) (target:#FrameworkElement, pair:DependencyPropertyBindingPair) =
            target.SetBinding(pair.Property,pair.Binding) |> ignore
            target

    type DependencyPropertyValuePair(dp:DependencyProperty,value:obj) =
        member this.Property = dp
        member this.Value = value
        static member ( <+> )  (target:#Control, pair:DependencyPropertyValuePair) =
            target.SetValue(pair.Property,pair.Value)
            target

    type Button with
        static member CommandBinding (binding:Data.BindingBase) =
            DependencyPropertyBindingPair(Button.CommandProperty,binding)

    type Grid with
        static member Column (value:int) =
            DependencyPropertyValuePair(Grid.ColumnProperty,value)
        static member Row (value:int) =
            DependencyPropertyValuePair(Grid.RowProperty,value)

    type TextBox with
        static member TextBinding (binding:Data.BindingBase) =
            DependencyPropertyBindingPair(TextBox.TextProperty,binding)
    *)

    let makeGridLength len = new GridLength(len, GridUnitType.Star)


    // let makeMenu (xss:list<MenuItem*list<Control>>)=
    //     let menu = new Menu()
    //     for h,xs in xss do
    //         menu.Items.Add (h) |> ignore
    //         for x in xs do
    //             h.Items.Add (x) |> ignore
    //     menu

    let updateMenu (menu:Menu) (xss:list<MenuItem*list<Control>>)=
        for h,xs in xss do
            menu.Items.Add (h) |> ignore
            for x in xs do
                h.Items.Add (x) |> ignore

    let makeContextMenu (xs:list<#Control>)=
        let menu = new ContextMenu()
        for x in xs do menu.Items.Add (x) |> ignore
        menu


    /// clear Grid first and then set with new elements
    let setGridHorizontal (grid:Grid) (xs:list<Control*RowDefinition>)=
        grid.Children.Clear()
        grid.RowDefinitions.Clear()
        grid.ColumnDefinitions.Clear()
        for i , (e,rd) in List.indexed xs do
            grid.RowDefinitions.Add rd
            e.SetValue(Grid.RowProperty,i) |> ignore
            grid.Children.Add e
            // grid.Children.Add  ( e <+> Grid.Row i ) |> ignore


    /// clear Grid first and then set with new elements
    let setGridVertical (grid:Grid) (xs:list<Control*ColumnDefinition>)=
        grid.Children.Clear()
        grid.RowDefinitions.Clear()
        grid.ColumnDefinitions.Clear()
        for i , (e,cd) in List.indexed xs do
            grid.ColumnDefinitions.Add cd
            e.SetValue(Grid.ColumnProperty,i) |> ignore
            grid.Children.Add e
            // grid.Children.Add  ( e <+> Grid.Column i ) |> ignore


    // let makeGrid (xs:list<Control>)=
    //     let grid = new Grid()
    //     for i , e in List.indexed xs do
    //         e.SetValue(Grid.RowProperty,i) |> ignore
    //         grid.Children.Add e
    //         // grid.Children.Add  ( e <+> Grid.Row i ) |> ignore
    //     grid

    let makePanelVert (xs:list<Control>) =
        let p = new StackPanel(Orientation = Orientation.Vertical)
        for x in xs do
            p.Children.Add x |> ignore
        p

    // let makePanelHor (xs:list<#Control>) =
    //     let p = new StackPanel(Orientation = Orientation.Horizontal)
    //     for x in xs do
    //         p.Children.Add x |> ignore
    //     p

    let dockPanelVert (top:Control, center: Control, bottom:Control)=
        let d = new DockPanel()
        DockPanel.SetDock(top,Dock.Top)
        DockPanel.SetDock(bottom,Dock.Bottom)
        d.Children.Add(top) |> ignore
        d.Children.Add(bottom) |> ignore
        d.Children.Add(center) |> ignore // add the element to claim all the space last
        d





