namespace Seff

open System
open System.Windows
open System.Windows.Controls


module UtilWPF =    

    //see: http://www.fssnip.net/4W/title/Calculator
    //http://trelford.com/blog/post/F-operator-overloads-for-WPF-dependency-properties.aspx
    //http://trelford.com/blog/post/Exposing-F-Dynamic-Lookup-to-C-WPF-Silverlight.aspx !!!

    type DependencyPropertyBindingPair(dp:DependencyProperty,binding:Data.BindingBase) =
        member this.Property = dp
        member this.Binding = binding
        static member (++) 
            (target:#FrameworkElement,pair:DependencyPropertyBindingPair) =
            target.SetBinding(pair.Property,pair.Binding) |> ignore
            target

    type DependencyPropertyValuePair(dp:DependencyProperty,value:obj) =
        member this.Property = dp
        member this.Value = value
        static member (++) 
            (target:#UIElement,pair:DependencyPropertyValuePair) =
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


    let makeGridLength h = new GridLength(h,GridUnitType.Star)

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
        
    let makeContextMenu (xs:list<Control>)=
        let menu = new ContextMenu()
        for x in xs do menu.Items.Add (x) |> ignore         
        menu

            
    let makeGridHorizontalEx (xs:list<UIElement*RowDefinition>)= 
        let grid = new Grid()
        xs |> List.iteri (fun i (e,rd)->        
            grid.RowDefinitions.Add <| rd
            grid.Children.Add       <| e ++ Grid.Row i
            |> ignore     
            )
        grid
    
    let makeGridVerticalEx (xs:list<UIElement*ColumnDefinition>)= 
        let grid = new Grid()
        xs |> List.iteri (fun i (e,cd)->        
            grid.ColumnDefinitions.Add <| cd
            grid.Children.Add          <| e ++ Grid.Column i
            |> ignore     
            )
        grid
    
    let makeGrid (xs:list<UIElement>)= 
        let grid = new Grid()
        xs |> List.iteri (fun i e->        
            grid.Children.Add       <| e ++ Grid.Row i
            |> ignore     
            )
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
