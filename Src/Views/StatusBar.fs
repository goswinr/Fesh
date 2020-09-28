namespace Seff.Views

open Seff
open Seff.Util.General
open System
open System.Windows
open System.Windows.Documents
open System.Windows.Controls
open System.Windows.Controls.Primitives // status bar
open System.Windows.Media
open ICSharpCode
open Seff.Views.Util
open FSharp.Compiler.SourceCodeServices
open System.Windows.Automation.Peers

open Seff.Config
open Seff.Editor
open Seff
open System


module private StatusbarStyle = 
    let textPadding = Thickness(4. , 1. , 4., 1. ) //left ,top, right, bottom)
    let okColor =   Brushes.Green    |> brighter 140
    let errColor =  Brushes.Red      |> brighter 160
    let warnColor = Brushes.Yellow   |> brighter 40  
    let activeCol = Brushes.Orange   |> brighter 20
    let failedCol = Brushes.Magenta 
    let greyText =  Brushes.Gray

open StatusbarStyle

type CheckerStatus (grid:TabsAndLog) as this = 
    inherit TextBlock()
    
    let tabs = grid.Tabs
    let checkingTxt = "checking for Errors ..."
    let checker = Checker.GetOrCreate(grid.Config)
    let originalBackGround = this.Background
                
    let updateCheckState(iEditor:IEditor)= 
        //log.PrintDebugMsg "Setting errors for %A %A " iEditor.FileInfo iEditor.CheckRes.Value.checkRes.Errors.Length 
        match iEditor.FileCheckState with
        | Done res ->                                            
                let es = res.checkRes.Errors
                if es.Length = 0 then 
                    this.Text <- "No compiler errors"
                    this.Background <- okColor
                    this.ToolTip <- "FSarp Compiler Service found no Errors in"+ Environment.NewLine + tabs.Current.FormatedFileName
                else 
                    let ers = es|> Seq.filter (fun e -> e.Severity = FSharpErrorSeverity.Error) |> Seq.length
                    let was = es.Length - ers
                    if ers = 0 then 
                        this.Text <- sprintf "Compiler warnings: %d" was
                        this.Background <- warnColor 
                    elif was = 0 then
                        this.Text <- sprintf "Compiler errors: %d" ers
                        this.Background <- errColor
                    else
                        this.Text <- sprintf "Compiler errors: %d, warnings: %d" ers was
                        this.Background <- errColor            
                    this.ToolTip <- makePanelVert [                         
                        if ers>0 then TextBlock(Text="Errors:", FontSize = 14. , FontWeight = FontWeights.Bold )
                        for e in es|> Seq.filter (fun e -> e.Severity = FSharpErrorSeverity.Error)  do new TextBlock(Text = sprintf "• Line %d: %s" e.StartLineAlternate e.Message)
                        if was>0 then TextBlock(Text="Warnings:", FontSize = 14. , FontWeight = FontWeights.Bold )
                        for e in es|> Seq.filter (fun e -> e.Severity = FSharpErrorSeverity.Warning) do new TextBlock(Text = sprintf "• Line %d: %s" e.StartLineAlternate e.Message) 
                        TextBlock(Text = tabs.Current.FormatedFileName, FontSize = 9.)
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
                            this.Text <- checkingTxt
                            this.Background <- originalBackGround 
                    | Done _ | NotStarted | Failed -> ()
            } |> Async.StartImmediate 
        
        | NotStarted -> // these below never happen because event is only triggerd on success
            this.Text <- "Initializing compiler.."
            this.Background <- originalBackGround 
        
        | Failed -> // these below never happen because event is only triggerd on success
            this.Text <- "Fs Checker failed to complete."
            this.Background <- failedCol
    

    do     
        this.Padding <-textPadding

        this.Text <- checkingTxt
        
        tabs.OnTabChanged.Add (fun t -> updateCheckState(t.Editor))
            
        checker.OnChecked.Add updateCheckState

        checker.OnChecking.Add updateCheckState
 
type FsiRunStatus (grid:TabsAndLog, cmds:Commands) as this = 
    inherit TextBlock()
    do     
        this.Padding <- textPadding
        this.Inlines.Add ("FSI is initializing ...")
        this.ContextMenu <- makeContextMenu [ menuItem cmds.CancelFSI ]
        //this.ToolTip <- "Click here to enabel or disable the default output from fsi in the log window"
              
        grid.Tabs.Fsi.OnStarted.Add(fun code -> 
            this.Background <- activeCol
            this.Inlines.Clear()
            match code.file with 
            |SetTo fi ->                 
                if code.allOfFile then this.Inlines.Add(new Run ("FSI is running a part of ", Foreground = greyText))
                else                   this.Inlines.Add(new Run ("FSI is running "          , Foreground = greyText))
                this.Inlines.Add( new Run (fi.Name, FontFamily = Style.fontEditor) )     
                this.Inlines.Add( new Run (" ..."                                           , Foreground = greyText))
            |NotSet ->                 
                this.Inlines.Add( "FSI is running ..." )
            )

        grid.Tabs.Fsi.OnIsReady.Add(fun _ -> 
            this.Inlines.Clear()
            this.Inlines.Add("FSI is ready") 
            this.Background <- okColor)

type FsiOutputStatus (grid:TabsAndLog) as this = 
    inherit TextBlock()
    let on = "FSI prints to log window"
    let off = "FSI is quiet"
    let isOff () = grid.Config.Settings.GetBool Settings.keyFsiQuiet false
    do     
        this.Padding <- textPadding
        this.Text <- if isOff() then off else on
        this.ToolTip <- "Click here to enabel or disable the default output from fsi in the log window"
        this.MouseLeftButtonDown.Add ( fun a -> 
            if isOff() then 
                this.Text <- on
                grid.Config.Settings.SetBool Settings.keyFsiQuiet false
                grid.Tabs.Fsi.Initalize()
            else
                this.Text <- off
                grid.Config.Settings.SetBool Settings.keyFsiQuiet true
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

    let desc = "Highlighting is "
    let baseTxt = "Highlights and counts the occurences of the currently selected Text.\r\nMinimum two characters. No line breaks\r\nClick here to turn " 
    do             
        let isOn =  grid.Config.Settings.SelectAllOccurences 
        this.Padding <- textPadding
        this.ToolTip <-  baseTxt + if isOn then "Off" else "On"         
        this.Inlines.Add ( desc + if isOn then "On" else "Off")

        SelectedTextTracer.Instance.HighlightChanged.Add ( fun (highTxt,k ) ->             
            this.Inlines.Clear()
            this.Inlines.Add( sprintf "%d of " k)
            this.Inlines.Add( new Run (highTxt, FontFamily = Style.fontEditor, Background = SelectedTextHighlighter.ColorHighlight))      
            this.Inlines.Add( sprintf " (%d Chars) " highTxt.Length)
            )
        
        grid.Log.SelectedTextHighLighter.HighlightChanged.Add( fun (highTxt,k ) ->             
            this.Inlines.Clear()
            this.Inlines.Add( sprintf "%d of " k)
            this.Inlines.Add( new Run (highTxt, FontFamily = Style.fontEditor, Background = grid.Log.SelectedTextHighLighter.ColorHighlight))      
            this.Inlines.Add( sprintf " (%d Chars) in Log" highTxt.Length)
            ) 
        
        this.MouseDown.Add ( fun _ -> 
            let mutable isOnn =  grid.Config.Settings.SelectAllOccurences
            isOnn <- not isOnn // toggle            
            this.Inlines.Clear()
            this.Inlines.Add(desc +    if isOnn then "On" else "Off")
            this.ToolTip <-  baseTxt + if isOnn then "Off" else "On" 
            grid.Config.Settings.SelectAllOccurences <- isOnn 
            //SelectedTextTracer.IsActive <- isOnn
            )

        grid.Tabs.OnTabChanged.Add ( fun _ -> 
            let isO =  grid.Config.Settings.SelectAllOccurences 
            this.Inlines.Clear()
            this.Inlines.Add(desc +    if isO then "On" else "Off")
            this.ToolTip <-  baseTxt + if isO then "Off" else "On" 
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
        add Dock.Left <| CheckerStatus(grid)    
        add Dock.Left  <| FsiRunStatus (grid, cmds)
        add Dock.Left  <| SelectedTextStatus(grid)

        add Dock.Right  <| FsiOutputStatus(grid)
        if grid.Config.HostingInfo.IsHosted then     add Dock.Right  <|  AsyncStatus(grid)    

        bar.Items.Add (new StatusBarItem()) |> ignore // to fill remaining gap
       
       

    member this.Bar =  bar
  



