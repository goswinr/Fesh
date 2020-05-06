namespace Seff.Util

open System
open System.Windows
open System.Windows.Controls
open System.Windows.Media



module WPF = 

    ///Adds bytes to each color channel to increase brightness, negative values to make darker
    /// result will be clamped between 0 and 255
    let changeLuminace (amount:int) (br:SolidColorBrush)=
        let inline clamp x = if x<0 then 0uy elif x>255 then 255uy else byte(x)
        let r = int br.Color.R + amount |> clamp      
        let g = int br.Color.G + amount |> clamp
        let b = int br.Color.B + amount |> clamp
        SolidColorBrush(Color.FromArgb(br.Color.A, r,g,b))
    
    ///Adds bytes to each color channel to increase brightness
    /// result will be clamped between 0 and 255
    let brighter (amount:int) (br:SolidColorBrush)  = changeLuminace amount br 
    
    ///Removes bytes from each color channel to increase darkness, 
    /// result will be clamped between 0 and 255
    let darker  (amount:int) (br:SolidColorBrush)  = changeLuminace -amount br


    //see: http://www.fssnip.net/4W/title/Calculator
    //http://trelford.com/blog/post/F-operator-overloads-for-WPF-dependency-properties.aspx
    //http://trelford.com/blog/post/Exposing-F-Dynamic-Lookup-to-C-WPF-Silverlight.aspx !!!

    type DependencyPropertyBindingPair(dp:DependencyProperty,binding:Data.BindingBase) =
        member this.Property = dp
        member this.Binding = binding
        static member ( <++> ) (target:#FrameworkElement, pair:DependencyPropertyBindingPair) =
            target.SetBinding(pair.Property,pair.Binding) |> ignore
            target

    type DependencyPropertyValuePair(dp:DependencyProperty,value:obj) =
        member this.Property = dp
        member this.Value = value
        static member ( <+> )  (target:#UIElement, pair:DependencyPropertyValuePair) =
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

    let makeGridLength len = new GridLength(len, GridUnitType.Star)

    let makeMenu (xss:list<MenuItem*list<Control>>)=
        let menu = new Menu()
        for h,xs in xss do
            menu.Items.Add (h) |> ignore
            for x in xs do
                h.Items.Add (x) |> ignore            
        menu
    
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
    let setGridHorizontal (grid:Grid) (xs:list<UIElement*RowDefinition>)= 
        grid.Children.Clear()
        grid.RowDefinitions.Clear()
        grid.ColumnDefinitions.Clear()
        for i , (e,rd) in List.indexed xs do    
            grid.RowDefinitions.Add (rd)
            grid.Children.Add  ( e <+> Grid.Row i ) |> ignore     
            
    
    /// clear Grid first and then set with new elements
    let setGridVertical (grid:Grid) (xs:list<UIElement*ColumnDefinition>)= 
        grid.Children.Clear()
        grid.RowDefinitions.Clear()
        grid.ColumnDefinitions.Clear()
        for i , (e,cd) in List.indexed xs do    
            grid.ColumnDefinitions.Add (cd)
            grid.Children.Add  ( e <+> Grid.Column i ) |> ignore 
     
    
    let makeGrid (xs:list<UIElement>)= 
        let grid = new Grid()
        for i , e in List.indexed xs do 
            grid.Children.Add  ( e <+> Grid.Row i ) |> ignore  
        grid

    let makePanelVert (xs:list<#UIElement>) =
        let p = new StackPanel(Orientation= Orientation.Vertical)
        for x in xs do
            p.Children.Add x |> ignore
        p
     
    let makePanelHor (xs:list<#UIElement>) =
        let p = new StackPanel(Orientation= Orientation.Horizontal)
        for x in xs do
            p.Children.Add x |> ignore
        p

    let dockPanelVert (top:UIElement, center: UIElement, bottom:UIElement)=
        let d = new DockPanel()
        DockPanel.SetDock(top,Dock.Top)
        DockPanel.SetDock(bottom,Dock.Bottom)
        d.Children.Add(top) |> ignore         
        d.Children.Add(bottom) |> ignore 
        d.Children.Add(center) |> ignore 
        d
    

