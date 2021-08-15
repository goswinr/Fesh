namespace Seff.Views


open System
open System.Windows
open System.Windows.Media
open System.Windows.Documents
open System.Windows.Controls
open System.Windows.Controls.Primitives // status bar

open FSharp.Compiler.Diagnostics

open AvalonEditB
open AvalonLog.Brush

open Seff
open Seff.Editor
open Seff.Model
open FsEx.Wpf.DependencyProps


module MenuUtil =
    let menuItem (cmd:CommandInfo) =  
        MenuItem(Header = cmd.name, InputGestureText = cmd.gesture, ToolTip = cmd.tip, Command = cmd.cmd):> Control

open MenuUtil

module StatusbarStyle = 
    let textPadding = Thickness(4. , 1. , 4., 1. ) //left ,top, right, bottom)
    let okColor =   Brushes.Green    |> brighter 140   |> freeze
    let errColor =  Brushes.Red      |> brighter 160   |> freeze
    let warnColor = Brushes.Yellow   |> brighter 40    |> freeze
    let activeCol = Brushes.Orange   |> brighter 20    |> freeze
    let failedCol = Brushes.Magenta                    |> freeze
    let greyText =  Brushes.Gray     |> darker 20      |> freeze
    let waitCol  =  Brushes.HotPink  |> brighter 80    |> freeze

open StatusbarStyle

type CheckerStatus (grid:TabsAndLog) as this = 
    inherit TextBlock()
    
    let tabs = grid.Tabs
    let checkingTxt = "Checking for Errors ..."
    let checker = Checker.GetOrCreate(grid.Config)
    //let originalBackGround = this.Background
    
    let mutable lastErrCount = -1
    let mutable lastFile = Guid.Empty

    let mutable firstErrorLine = None

    let updateCheckState(iEditor:IEditor)= 
        //log.PrintfnDebugMsg "Setting errors for %A %A " iEditor.FileInfo iEditor.CheckRes.Value.checkRes.Errors.Length 
        match iEditor.FileCheckState with
        | Done res ->                                            
                let es = res.checkRes.Diagnostics                
                if es.Length = 0 then 
                    if lastErrCount <> 0  || lastFile <> tabs.Current.Editor.Id then // no UI update needed in this case
                        this.Text <- "No compiler errors"
                        this.Background <- okColor
                        this.ToolTip <- "FSarp Compiler Service found no Errors in"+ Environment.NewLine + tabs.Current.FormatedFileName
                        lastFile <- tabs.Current.Editor.Id
                        lastErrCount <- 0
                        firstErrorLine <- None
                else 
                    lastFile <- tabs.Current.Editor.Id
                    lastErrCount <- es.Length 
                    es|> Array.sortInPlaceBy (fun e -> struct(e.StartLine, e.StartColumn)) // sort because we are not sure if they are allready sorted
                    firstErrorLine <- Some <| Document.TextLocation(es.[0].StartLine, es.[0].StartColumn + 1 )
                    let was = ResizeArray()
                    let ers = ResizeArray()
                    for e in es do
                        match e.Severity with 
                        | FSharpDiagnosticSeverity.Error   -> ers.Add e
                        | FSharpDiagnosticSeverity.Warning -> was.Add e
                        | FSharpDiagnosticSeverity.Hidden -> () // TODO or show something ?
                        | FSharpDiagnosticSeverity.Info   -> ()
                    let erk = ers.Count                    
                    let wak = was.Count
                    if wak > 0 && erk > 0 then
                        this.Text <- sprintf " %d compiler errors, %d warnings, first one on line: %d" erk wak firstErrorLine.Value.Line
                        this.Background <- errColor 
                    elif wak > 0 then
                        this.Text <- sprintf " %d compiler warnings, first one on line %d" wak firstErrorLine.Value.Line
                        this.Background <- warnColor 
                    elif erk > 0 then                        
                        this.Text <- sprintf " %d compiler errors, first one on line: %d" erk firstErrorLine.Value.Line
                        this.Background <- errColor                              
                    
                    this.ToolTip <- makePanelVert [                         
                        if erk>0 then       
                            TextBlock(Text="Errors:", FontSize = 14. , FontWeight = FontWeights.Bold )
                        for e in Seq.truncate 10 ers do    
                            TextBlock(Text = sprintf "• line %d: %s" e.StartLine e.Message)
                        if erk > 10 then 
                            TextBlock(Text = " ...")


                        if wak>0 then       
                            TextBlock(Text="Warnings:", FontSize = 14. , FontWeight = FontWeights.Bold )
                        for w in Seq.truncate 10 was do  
                            TextBlock(Text = sprintf "• line %d: %s" w.StartLine w.Message) 
                        if wak > 10 then 
                            TextBlock(Text = " ...")
                        TextBlock(Text = tabs.Current.FormatedFileName, FontSize = iEditor.AvaEdit.FontSize * 0.8)
                        ]        
        
        | GettingCode id0
        | Checking (id0,_) -> 
            async{
                do! Async.Sleep 300 // delay  to only show check in progress massage if it takes long, otherwis just show results via on checked event
                if iEditor.Id = tabs.Current.Editor.Id then // to cancel if tab changed  
                    match iEditor.FileCheckState with
                    | GettingCode id300
                    | Checking (id300,_) -> 
                        if id300 = id0 then // this is still the most recent checker
                            lastErrCount <- -1
                            this.Text <- checkingTxt
                            this.Background <- waitCol //originalBackGround 
                    | Done _ | NotStarted | Failed -> ()
            } |> Async.StartImmediate 
        
        | NotStarted -> // these below never happen because event is only triggerd on success
            lastErrCount <- -1
            this.Text <- "Initializing compiler . . ."
            this.Background <- waitCol //originalBackGround 
        
        | Failed -> // these below never happen because event is only triggerd on success
            lastErrCount <- -1
            this.Text <- "Fs Checker failed to complete."
            this.Background <- failedCol
    

    do     
        lastErrCount <- -1
        this.Padding <-textPadding
        this.Text <- checkingTxt
        this.Background <- waitCol //originalBackGround 

        tabs.OnTabChanged.Add (fun t -> updateCheckState(t.Editor))            
        checker.OnChecked.Add  updateCheckState
        checker.OnChecking.Add updateCheckState
        this.MouseDown.Add ( fun a -> 
            match firstErrorLine with 
            |Some loc -> Foldings.GoToLineAndUnfold(loc, grid.Tabs.Current.Editor, grid.Config)                
            |None     -> ()
            )
 
type FsiRunStatus (grid:TabsAndLog, cmds:Commands) as this = 
    inherit TextBlock()
    do     
        this.Padding <- textPadding
        this.Inlines.Add ("FSI is initializing . . .")
        this.Background <- waitCol //originalBackGround 
        this.ContextMenu <- makeContextMenu [ menuItem cmds.CancelFSI ]
        //this.ToolTip <- "Click here to enabel or disable the default output from fsi in the log window"
              
        grid.Tabs.Fsi.OnStarted.Add(fun code -> 
            this.Background <- activeCol
            this.Inlines.Clear()
            match code.file with 
            |SetTo fi ->                 
                if code.allOfFile then this.Inlines.Add(new Run ("FSI is running ",             Foreground = greyText))
                else                   this.Inlines.Add(new Run ("FSI is running a part of ", Foreground = greyText))
                this.Inlines.Add( new Run (fi.Name, FontFamily = Style.fontEditor) )     
                this.Inlines.Add( new Run (" . . ."                                           , Foreground = greyText))
            |NotSet ->                 
                this.Inlines.Add( "FSI is running . . ." )
            )

        grid.Tabs.Fsi.OnIsReady.Add(fun _ -> 
            this.Inlines.Clear()
            this.Inlines.Add("FSI is ready") 
            this.Background <- okColor)

type FsiOutputStatus (grid:TabsAndLog) as this = 
    inherit TextBlock()
    let onTxt = "FSI prints to log window"
    let offTxt = "FSI is quiet"
    let isOff () = grid.Config.Settings.GetBool "fsiOutputQuiet" false
    do     
        this.Padding <- textPadding
        this.Text <- if isOff() then offTxt else onTxt
        this.ToolTip <- "Click here to enabel or disable the default output from fsi in the log window"
        this.MouseLeftButtonDown.Add ( fun a -> 
            if isOff() then 
                this.Text <- onTxt
                grid.Config.Settings.SetBool "fsiOutputQuiet" false
                grid.Config.Settings.Save ()
                grid.Tabs.Fsi.Initalize()
            else
                this.Text <- offTxt
                grid.Config.Settings.SetBool "fsiOutputQuiet" true
                grid.Config.Settings.Save ()
                grid.Tabs.Fsi.Initalize()
            )  
            
type AsyncStatus (grid:TabsAndLog) as this = 
    inherit TextBlock()
    let fsi = grid.Tabs.Fsi
    let isAsync = grid.Config.Settings.GetBool "asyncFsi" true  
    let sync = "FSI evaluation mode: Synchronos" 
    let asyn = "FSI evaluation mode: Asynchronos"
        
    do     
        this.Padding <- textPadding
        this.Text <- if isAsync then asyn else sync
        this.ToolTip <- "Click to switch between synchronous and asynchronous evaluation in FSI,\r\nsynchronous is needed for UI interaction,\r\nasynchronous allows easy cancellation and keeps the editor window alive"
        this.MouseDown.Add(fun _ -> fsi.ToggleSync()) //done in fsi module      // TODO better make it dependent on commands , not fsi
        fsi.OnModeChanged.Add(function 
            | Sync  -> this.Text <- sync 
            | Async -> this.Text <- asyn  )

type SelectedTextStatus (grid:TabsAndLog) as this = 
    inherit TextBlock()  
    let codeblock = Brushes.White   |> darker 70

    let isSelOcc() = grid.Config.Settings.Get("SelOcc") = Some "1" 

    let onTxt ="ON"
    let offTxt = "OFF"
    let desc = "Highlighting is " // with trailing space
    let baseTxt = "Highlights and counts the occurences of the currently selected Text.\r\nMinimum two characters. No line breaks\r\nClick here to turn " 
    do             
        let sett = grid.Config.Settings
        
        this.Padding <- textPadding
        this.ToolTip <-  baseTxt + if isSelOcc() then offTxt else onTxt        
        this.Inlines.Add ( desc  + if isSelOcc() then onTxt else offTxt)
        
        //Editor events
        SelectedTextTracer.Instance.OnHighlightChanged.Add ( fun (highTxt,k ) ->             
            this.Inlines.Clear()
            this.Inlines.Add( sprintf "%d of " k)
            this.Inlines.Add( new Run (highTxt, FontFamily = Style.fontEditor, Background = SelectedTextHighlighter.ColorHighlight))      
            this.Inlines.Add( sprintf " (%d Chars) " highTxt.Length)
            )
        SelectedTextTracer.Instance.OnHighlightCleared.Add ( fun () ->  
            this.Inlines.Clear()
            this.Inlines.Add ( desc + if isSelOcc() then onTxt else offTxt)
            )
        
        //Log events 
        grid.Log.AvalonLog.SelectedTextHighLighter.OnHighlightChanged.Add( fun (highTxt,k ) ->             
            this.Inlines.Clear()
            this.Inlines.Add( sprintf "%d of " k)
            this.Inlines.Add( new Run (highTxt, FontFamily = Style.fontEditor, Background = grid.Log.AvalonLog.SelectedTextHighLighter.ColorHighlight))      
            this.Inlines.Add( sprintf " (%d Chars) in Log" highTxt.Length)
            ) 
        
        grid.Log.AvalonLog.SelectedTextHighLighter.OnHighlightCleared.Add ( fun () ->  
            this.Inlines.Clear()
            this.Inlines.Add ( desc + if isSelOcc() then onTxt else offTxt)
            )
        
        this.MouseDown.Add ( fun _ -> 
            if isSelOcc() then sett.Set "SelOcc" "0" else sett.Set "SelOcc" "1"// toggle 
            this.Inlines.Clear()            
            this.Inlines.Add( desc +    if isSelOcc() then onTxt else offTxt)
            this.ToolTip <-   baseTxt + if isSelOcc() then offTxt else onTxt            
            grid.Config.Settings.Save ()            
            )

        grid.Tabs.OnTabChanged.Add ( fun _ ->             
            this.Inlines.Clear()
            this.Inlines.Add(desc +    if isSelOcc() then onTxt else offTxt)
            this.ToolTip <-  baseTxt + if isSelOcc() then offTxt else onTxt
            )

type StatusBar (grid:TabsAndLog, cmds:Commands)  = 
    let bar = new Primitives.StatusBar() 

    let add (side:Dock) (e:UIElement) = 
        let bi = new StatusBarItem(Content=e)
        DockPanel.SetDock(bi,side)        
        bar.Items.Add bi |> ignore         
        
        let s = new Separator() 
        DockPanel.SetDock(s,side)
        bar.Items.Add s |> ignore 
    

    do 
        add Dock.Left  <| CheckerStatus(grid)    
        add Dock.Left  <| FsiRunStatus (grid, cmds)
        add Dock.Left  <| SelectedTextStatus(grid)

        add Dock.Right  <| FsiOutputStatus(grid)
        if grid.Config.Hosting.IsHosted then     add Dock.Right  <|  AsyncStatus(grid)    

        bar.Items.Add (new StatusBarItem()) |> ignore // to fill remaining gap
       
       

    member this.Bar =  bar
  



