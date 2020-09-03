﻿namespace Seff.Views

open Seff
open Seff.Util.General
open System
open System.Windows
open System.Windows.Controls
open System.Windows.Controls.Primitives // status bar
open System.Windows.Media
open ICSharpCode
open Seff.Views.Util
open FSharp.Compiler.SourceCodeServices
open System.Windows.Automation.Peers

open Seff.Config
open Seff.Editor


module private StatusbarStyle = 
    let padding = Thickness(6. , 2. , 6., 2. ) //left ,top, right, bottom)

type CheckerStatus (grid:TabsAndLog) as this = 
    inherit TextBlock()
    
    let tabs = grid.Tabs
    let checkingTxt = "checking for Errors ..."
    let checker = Checker.GetOrCreate(grid.Config)
    let originalBackGround = this.Background
                
    let updateCheckState(iEditor:IEditor)= 
        //log.PrintDebugMsg "Setting errors for %A %A " iEditor.FileInfo iEditor.CheckRes.Value.checkRes.Errors.Length 
        match iEditor.CheckState with
        | Done res ->                                            
                let es = res.checkRes.Errors
                if es.Length = 0 then 
                    this.Text <- "No compiler errors"
                    this.Background <- Brushes.Green |> brighter 120
                    this.ToolTip <- "FSarp Compiler Service found no Errors in " + tabs.Current.FormatedFileName
                else 
                    let ers = es|> Seq.filter (fun e -> e.Severity = FSharpErrorSeverity.Error) |> Seq.length
                    let was = es.Length - ers
                    if ers = 0 then 
                        this.Text <- sprintf "Compiler warnings: %d" was
                        this.Background <- Brushes.Yellow   |> brighter 40  
                    elif was = 0 then
                        this.Text <- sprintf "Compiler errors: %d" ers
                        this.Background <- Brushes.Red   |> brighter 150  
                    else
                        this.Text <- sprintf "Compiler errors: %d, warnings: %d" ers was
                        this.Background <- Brushes.Red   |> brighter 150             
                    this.ToolTip <- makePanelVert [                         
                        if ers>0 then TextBlock(Text="Errors:", FontSize = 14. , FontWeight = FontWeights.Bold )
                        for e in es|> Seq.filter (fun e -> e.Severity = FSharpErrorSeverity.Error)  do new TextBlock(Text = sprintf "• Line %d: %s" e.StartLineAlternate e.Message)
                        if was>0 then TextBlock(Text="Warnings:", FontSize = 14. , FontWeight = FontWeights.Bold )
                        for e in es|> Seq.filter (fun e -> e.Severity = FSharpErrorSeverity.Warning) do new TextBlock(Text = sprintf "• Line %d: %s" e.StartLineAlternate e.Message) 
                        TextBlock(Text = tabs.Current.FormatedFileName, FontSize = 9.)
                        ]        
        
        | Running id0->
            async{
                do! Async.Sleep 300 // delay  to only show check in progress massage if it takes long, otherwis just show results via on checked event
                if iEditor.Id = tabs.Current.Editor.Id then // to cancel if tab changed
                    match iEditor.CheckState with
                    | Running id300 ->
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
            this.Background <- Brushes.Magenta 
    

    do     
        this.Padding <-StatusbarStyle.padding

        this.Text <- checkingTxt
        
        tabs.OnTabChanged.Add (fun t -> updateCheckState(t.Editor))
            
        checker.OnChecked.Add updateCheckState

        checker.OnChecking.Add updateCheckState
 
type FsiRunStatus (grid:TabsAndLog, cmds:Commands) as this = 
    inherit TextBlock()
    do     
        this.Padding <- StatusbarStyle.padding
        this.Text <- "FSI is initializing ..."
        this.ContextMenu <- makeContextMenu [ menuItem cmds.CancelFSI ]
        //this.ToolTip <- "Click here to enabel or disable the default output from fsi in the log window"
              
        grid.Tabs.Fsi.OnStarted.Add(fun code -> 
            this.Background <- Brushes.Orange   |> brighter 20 
            match code.file with 
            |SetTo fi -> 
                if code.allOfFile then this.Text <- sprintf "FSI is running %s  ..." fi.Name
                else                   this.Text <- sprintf "FSI is running selected code from file %s  ..." fi.Name
            |NotSet ->                 this.Text <- "FSI is running ..." )

        grid.Tabs.Fsi.OnIsReady.Add(fun _ -> 
            this.Text <- "FSI is ready" 
            this.Background <- Brushes.Green |> brighter 120)

type FsiOutputStatus (grid:TabsAndLog) as this = 
    inherit TextBlock()
    do     
        this.Padding <- StatusbarStyle.padding
        this.Text <- "FSI output is on"
        this.ToolTip <- "Click here to enabel or disable the default output from fsi in the log window"
        this.MouseLeftButtonDown.Add ( fun a -> 
            if grid.Config.Settings.GetBool Settings.keyFsiQuiet false then 
                this.Text <- "FSI prints to log window"
                grid.Config.Settings.SetBool Settings.keyFsiQuiet false
                grid.Tabs.Fsi.Initalize()
            else
                this.Text <- "FSI is quiet"
                grid.Config.Settings.SetBool Settings.keyFsiQuiet true
                grid.Tabs.Fsi.Initalize()
            )  
            
type AsyncStatus (grid:TabsAndLog) as this = 
    inherit TextBlock()
    let fsi = grid.Tabs.Fsi
    let set = grid.Config.Settings
    let sync = "FSI evaluation mode: Synchronos" 
    let asyn = "FSI evaluation mode: Asynchronos"
        
    do     
        this.Padding <- StatusbarStyle.padding
        this.Text <- if set.GetBool "asyncFsi" true  then asyn else sync
        this.ToolTip <- "Click to switch between synchronous and asynchronous evaluation in FSI,\r\nsynchronous is needed for UI interaction,\r\nasynchronous allows easy cancellation and keeps the editor window alive"
        this.MouseDown.Add(fun _ -> fsi.ToggleSync()) //done in fsi module      
        fsi.OnModeChanged.Add(function 
            | Sync  -> this.Text <- sync 
            | Async -> this.Text <- asyn  )
 
type StatusBar (grid:TabsAndLog, cmds:Commands)  = // TODO better make it dependent on commands , not fsi

    let bar = new Primitives.StatusBar()   

    do 
        bar.Items.Add (new CheckerStatus(grid))                                         |> ignore 
        bar.Items.Add (new Separator())                                                 |> ignore 
        bar.Items.Add (new FsiRunStatus (grid, cmds))                                   |> ignore 
        bar.Items.Add (new Separator())                                                 |> ignore 
        bar.Items.Add (new FsiOutputStatus(grid))                                       |> ignore 
        bar.Items.Add (new Separator())                                                 |> ignore 
        if grid.Config.HostingInfo.IsHosted then bar.Items.Add( new AsyncStatus(grid))  |> ignore
        bar.Items.Add( new StatusBarItem())                                             |> ignore // to fill remaining space

    member this.Bar =  bar
  



