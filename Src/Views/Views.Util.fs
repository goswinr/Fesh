namespace Seff.Views
open Seff


module Util = 
    open System
    open System.Windows
    open System.Windows.Controls
    open System.Windows.Media
    open System.Windows.Input

    /// used for evaluating results of win32 dialogs
    let inline isTrue (nb:Nullable<bool>) = nb.HasValue && nb.Value

    let inline freeze(br:SolidColorBrush)= 
        if br.CanFreeze then 
            br.Freeze()
        br

    //---------- creating UIElemnts --------------

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
    
    let menuItem (cmd:CommandInfo) =  MenuItem(Header = cmd.name, InputGestureText = cmd.gesture, ToolTip = cmd.tip, Command = cmd.cmd):> Control
    
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
    
    

    
    /// A command that optionally hooks into CommandManager.RequerySuggested to
    /// automatically trigger CanExecuteChanged whenever the CommandManager detects
    /// conditions that might change the output of canExecute. It's necessary to use
    /// this feature for command bindings where the CommandParameter is bound to
    /// another UI control (e.g. a ListView.SelectedItem).
    type Command(execute, canExecute, autoRequery) as this =
        let canExecuteChanged = Event<EventHandler,EventArgs>()
        let handler = EventHandler(fun _ _ -> this.RaiseCanExecuteChanged())
    
        do if autoRequery then CommandManager.RequerySuggested.AddHandler(handler)
        
        member private this._Handler = handler // CommandManager only keeps a weak reference to the event handler, so a strong handler must be maintained
           
        member this.RaiseCanExecuteChanged () = canExecuteChanged.Trigger(this , EventArgs.Empty)        
           
        //interface is implemented as members and as interface members( to be sure it works):
        [<CLIEvent>]
        member this.CanExecuteChanged = canExecuteChanged.Publish
        member this.CanExecute p = canExecute p
        member this.Execute p = execute p
        interface ICommand with
            [<CLIEvent>]
            member this.CanExecuteChanged = this.CanExecuteChanged
            member this.CanExecute p =      this.CanExecute p 
            member this.Execute p =         this.Execute p 
        
    /// creates a ICommand
    let mkCmd canEx ex = new Command(ex,canEx,true) :> ICommand
    
    /// creates a ICommand, CanExecute is always true
    let mkCmdSimple action =
        let ev = Event<_ , _>()
        { new Windows.Input.ICommand with
                [<CLIEvent>]
                member this.CanExecuteChanged = ev.Publish
                member this.CanExecute(obj) = true
                member this.Execute(obj) = action(obj)                
                }

(*
    open System.ComponentModel
    
    type ViewModelBase() = //http://www.fssnip.net/4Q/title/F-Quotations-with-INotifyPropertyChanged
        let propertyChanged = new Event<_, _>()
        let toPropName(query : Expr) = 
            match query with
            | PropertyGet(a, b, list) ->
                b.Name
            | _ -> ""

        interface INotifyPropertyChanged with
            [<CLIEvent>]
            member x.PropertyChanged = propertyChanged.Publish

        abstract member OnPropertyChanged: string -> unit
        default x.OnPropertyChanged(propertyName : string) =
            propertyChanged.Trigger(x, new PropertyChangedEventArgs(propertyName))

        member x.OnPropertyChanged(expr : Expr) =
            let propName = toPropName(expr)
            x.OnPropertyChanged(propName)

    type TestModel() =
        inherit ViewModelBase()

        let mutable selectedItem : obj = null

        member x.SelectedItem
            with get() = selectedItem
            and set(v : obj) = 
                selectedItem <- v
                x.OnPropertyChanged(<@ x.SelectedItem @>)


*)