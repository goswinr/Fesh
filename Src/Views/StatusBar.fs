﻿namespace Seff.Views


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
open FsEx.Wpf // for TextBlockSelectable
open FsEx.Wpf.DependencyProps


module MenuUtil = 
    let menuItem (cmd:CommandInfo) = 
        MenuItem(Header = cmd.name, InputGestureText = cmd.gesture, ToolTip = cmd.tip, Command = cmd.cmd):> Control

open MenuUtil

module StatusbarStyle =     

    let errColor =  Brushes.Red      |> brighter 160   |> freeze // not ErrorStyle.errBackGr  
    let warnColor = Brushes.Yellow   |> brighter 40    |> freeze // not ErrorStyle.warnBackGr

    let textPadding = Thickness(4. , 1. , 4., 1. ) //left ,top, right, bottom)
    let okColor =   Brushes.Green    |> brighter 140   |> freeze
    let activeCol = Brushes.Orange   |> brighter 20    |> freeze
    let failedCol = Brushes.Magenta                    |> freeze
    let grayText =  Brushes.Gray     |> darker 20      |> freeze
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

    let mutable firstErrorSegm = None
    let was = ResizeArray<FSharpDiagnostic>()  // Warnings
    let ers = ResizeArray<FSharpDiagnostic>()  // Errors
    let his = ResizeArray<FSharpDiagnostic>()  // Hidden
    let ins = ResizeArray<FSharpDiagnostic>()  // Infos

    let getErrPanel(addPersistInfo:bool) = 
        let erk = ers.Count
        let wak = was.Count
        let ink = ins.Count
        let hik = his.Count
        makePanelVert [
            if erk>0 then
                if addPersistInfo then TextBlock(Text = "Click on text in statusbar or press Ctrl + E keys to scroll to first error.", FontSize = Style.fontSize * 0.75, FontStyle = FontStyles.Italic, Margin=Thickness 3.0)
                if addPersistInfo then TextBlock(Text = "Press Ctrl + P to persist this tooltip window.", FontSize = Style.fontSize * 0.75, FontStyle = FontStyles.Italic, Margin=Thickness 3.0)                
                TextBlockSelectable(Text = "File: " + tabs.Current.FormattedFileName, FontSize = Style.fontSize * 0.8, Margin=Thickness 3.0 )
                TextBlock(Text = "Errors:", FontSize = Style.fontSize , FontWeight = FontWeights.Bold )
            for e in Seq.truncate 10 ers do
                TextBlockSelectable(Text = sprintf "• line %d: %s: %s" e.StartLine e.ErrorNumberText e.Message, FontSize = Style.fontSize * 0.9, Margin=Thickness 3.0 )
            if erk > 10 then
                TextBlock(Text = sprintf "• and %d more ..." (erk-10), FontSize = Style.fontSize * 0.9)

            if wak>0 then
                TextBlock(Text="Warnings:", FontSize = Style.fontSize , FontWeight = FontWeights.Bold )
            for w in Seq.truncate 10 was do
                TextBlockSelectable(Text = sprintf "• line %d: %s: %s" w.StartLine w.ErrorNumberText w.Message, FontSize = Style.fontSize * 0.9, Margin=Thickness 3.0 )
            if wak > 10 then
                TextBlock(Text = sprintf "• and %d more ..." (wak-10), FontSize = Style.fontSize * 0.9)  
                
            if ink>0 then
                TextBlock(Text="Infos:", FontSize = Style.fontSize , FontWeight = FontWeights.Bold )
            for i in Seq.truncate 10 ins do
                TextBlockSelectable(Text = sprintf "• line %d: %s: %s" i.StartLine i.ErrorNumberText i.Message, FontSize = Style.fontSize * 0.9, Margin=Thickness 3.0 )
            if ink > 10 then
                TextBlock(Text = sprintf "• and %d more ..." (ink-10), FontSize = Style.fontSize * 0.9)  
            
            if hik>0 then
                TextBlock(Text="Hidden Infos:", FontSize = Style.fontSize , FontWeight = FontWeights.Bold )
            for h in Seq.truncate 10 his do
                TextBlockSelectable(Text = sprintf "• line %d: %s: %s" h.StartLine h.ErrorNumberText h.Message, FontSize = Style.fontSize * 0.9, Margin=Thickness 3.0 )
            if hik > 10 then
                TextBlock(Text = sprintf "• and %d more ..." (ink-10), FontSize = Style.fontSize * 0.9)  
            ]


    let updateCheckState(iEditor:IEditor)= 
        //log.PrintfnDebugMsg "Setting errors for %A %A " iEditor.FileInfo iEditor.CheckRes.Value.checkRes.Errors.Length
        match iEditor.FileCheckState with
        | Done res ->
                //ISeffLog.log.PrintfnDebugMsg $"checking  Done. Arrived in status bar with {res.checkRes.Diagnostics.Length} msgs"
                let es = res.checkRes.Diagnostics
                if es.Length = 0 then
                    if lastErrCount <> 0  || lastFile <> tabs.Current.Editor.Id then // no UI update needed in this case
                        this.Text <- "No compiler errors"
                        this.Background <- okColor
                        this.ToolTip <- "FSharp Compiler Service found no Errors in"+ Environment.NewLine + tabs.Current.FormattedFileName
                        lastFile <- tabs.Current.Editor.Id
                        lastErrCount <- 0
                        firstErrorSegm <- None
                else
                    lastFile <- tabs.Current.Editor.Id
                    lastErrCount <- es.Length
                    es|> Array.sortInPlaceBy (fun e -> struct(e.StartLine, e.StartColumn)) // sort because we are not sure if they are already sorted
                    firstErrorSegm <- Some <| ErrorUtil.getSegment iEditor.AvaEdit.Document es.[0]
                    was.Clear()
                    ers.Clear()
                    his.Clear()
                    ins.Clear()
                    for e in es do
                        match e.Severity with
                        | FSharpDiagnosticSeverity.Error   -> ers.Add e
                        | FSharpDiagnosticSeverity.Warning -> was.Add e
                        | FSharpDiagnosticSeverity.Hidden  -> his.Add e
                        | FSharpDiagnosticSeverity.Info    -> ins.Add e
                    let erk = ers.Count
                    let wak = was.Count
                    if wak > 0 && erk > 0 then
                        this.Text <- sprintf " %d compiler errors, %d warnings, first one on line: %d" erk wak es.[0].StartLine
                        this.Background <- errColor
                    elif wak > 0 then
                        this.Text <- sprintf " %d compiler warnings, first one on line %d" wak es.[0].StartLine
                        this.Background <- warnColor
                    elif erk > 0 then
                        this.Text <- sprintf " %d compiler errors, first one on line: %d" erk es.[0].StartLine
                        this.Background <- errColor
                    else
                       this.Text <- $"No compiler errors, {ins.Count+his.Count} Infos" 
                       this.Background <- okColor                       
                    
                    let tip = new ToolTip(Content = getErrPanel(true) )
                    tip.Placement <- Primitives.PlacementMode.Top //https://docs.microsoft.com/en-us/dotnet/framework/wpf/controls/popup-placement-behavior
                    tip.VerticalOffset <- -6.0
                    this.ToolTip <- tip
                    

        | GettingCode id0
        | Checking (id0,_) ->
            async{
                do! Async.Sleep 300 // delay  to only show check in progress massage if it takes long, otherwise just show results via on checked event
                if iEditor.Id = tabs.Current.Editor.Id then // to cancel if tab changed
                    match iEditor.FileCheckState with
                    | GettingCode id300
                    | Checking (id300,_) ->
                        if id300 = id0 then // this is still the most recent checker
                            lastErrCount <- -1
                            this.Text <- checkingTxt
                            this.Background <- waitCol //originalBackGround
                            this.ToolTip <- sprintf "Checking %s for Errors ..." tabs.Current.FormattedFileName
                    | Done _ | NotStarted | Failed -> ()
            } |> Async.StartImmediate

        | NotStarted -> // these below never happen because event is only triggered on success
            lastErrCount <- -1
            this.Text <- "Initializing compiler . . ."
            this.ToolTip <- "Initializing compiler . . ."
            this.Background <- waitCol //originalBackGround

        | Failed -> // these below never happen because event is only triggered on success
            lastErrCount <- -1
            this.Text <- "Fs Checker failed to complete."
            this.ToolTip <- "Fs Checker failed to complete."
            this.Background <- failedCol


    do
        lastErrCount <- -1
        this.Padding <-textPadding
        this.Text <- checkingTxt
        this.Background <- waitCol //originalBackGround

        tabs.OnTabChanged.Add (fun t -> updateCheckState(t.Editor))
        checker.OnCheckedForErrors.Add  updateCheckState
        checker.OnChecking.Add updateCheckState
        this.MouseLeftButtonDown.Add ( fun a ->
            match firstErrorSegm with
            |Some seg -> Foldings.GoToLineAndUnfold(seg, grid.Tabs.Current.Editor, grid.Config)
            |None     -> ()
            )

        this.MouseRightButtonDown.Add ( fun a ->
            match tabs.Current.Editor.FileCheckState with
            | Done res ->
                for e in res.checkRes.Diagnostics |> Seq.sortBy ( fun e -> e.StartLine) do
                    ISeffLog.log.PrintfnInfoMsg "Line: %d %A %s: %s" e.StartLine e.Severity e.ErrorNumberText e.Message
            | _ -> ()
            )

    member _.GetErrorPanelCached() = getErrPanel(false)

type FsiRunStatus (grid:TabsAndLog) as this = 
    inherit TextBlock()
    do
        this.Padding <- textPadding
        this.Inlines.Add ("FSI is initializing . . .")
        this.Background <- waitCol //originalBackGround
        //this.ContextMenu <- makeContextMenu [ menuItem cmds.CancelFSI ]
        this.ToolTip <- "Shows the status of the fsi evaluation core. This is the same for all tabs. Only one script can run at the time."

        grid.Tabs.Fsi.OnStarted.Add(fun codeToEval ->
            this.Background <- activeCol
            this.Inlines.Clear()
            match codeToEval.editor.FilePath with
            |SetTo fi ->
                match codeToEval.amount with
                | All                 ->  this.Inlines.Add(new Run ("FSI is running ",           Foreground = grayText))
                | ContinueFromChanges ->  this.Inlines.Add(new Run ("FSI continues to run ",  Foreground = grayText))
                | FsiSegment _        ->  this.Inlines.Add(new Run ("FSI is running a part of ", Foreground = grayText))
                this.Inlines.Add( new Run (fi.Name, FontFamily = Style.fontEditor) )
                this.Inlines.Add( new Run (" . . ."                                           , Foreground = grayText))
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
    let isOff () = grid.Config.Settings.GetBool ("fsiOutputQuiet", false)
    do
        this.Padding <- textPadding
        this.Text <- if isOff() then offTxt else onTxt
        this.ToolTip <- "Click here to enable or disable the default output from fsi in the log window"
        this.MouseLeftButtonDown.Add ( fun a ->
            if isOff() then
                this.Text <- onTxt
                grid.Config.Settings.SetBool ("fsiOutputQuiet", false)
            else
                this.Text <- offTxt
                grid.Config.Settings.SetBool ("fsiOutputQuiet", true)
            grid.Config.Settings.Save ()
            grid.Tabs.Fsi.Initialize()
            )

type AsyncStatus (grid:TabsAndLog) as this = 
    inherit TextBlock()
    let fsi = grid.Tabs.Fsi
    let isAsync = grid.Config.Settings.GetBool ("asyncFsi", true)
    let sync = "FSI evaluation mode: Synchronos"
    let asyn = "FSI evaluation mode: Asynchronos"

    do
        this.Padding <- textPadding
        this.Text <- if isAsync then asyn else sync
        this.ToolTip <- "Click to switch between synchronous and asynchronous evaluation in FSI,\r\nsynchronous is needed for UI interaction,\r\nasynchronous allows easy cancellation and keeps the editor window alive"
        this.MouseDown.Add(fun _ -> fsi.ToggleSync()) //done in fsi module      // TODO better make it dependent on commands , not fsi
        fsi.OnModeChanged.Add(function
            | Sync               -> this.Text <- sync
            | Async472 | Async60 -> this.Text <- asyn  )

// TODO keep always on, combine log and editor

type SelectedTextStatus (grid:TabsAndLog) as this = 
    inherit TextBlock()
    let codeblock = Brushes.White   |> darker 70

    let isSelOcc() = grid.Config.Settings.GetBool("HighlightAllOccurrences",true) 

    let onTxt ="ON"
    let offTxt = "OFF"
    let desc = "Highlighting is " // with trailing space
    let baseTxt = "Highlights and counts the occurrences of the currently selected Text.\r\nMinimum two characters. No line breaks\r\nClick here to turn "
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
        grid.Log.AvalonLog.SelectedTextHighLighter.OnHighlightChanged.Add( fun (highTxt,ints ) ->
            this.Inlines.Clear()
            this.Inlines.Add( sprintf "%d of " ints.Count)
            this.Inlines.Add( new Run (highTxt, FontFamily = Style.fontEditor, Background = grid.Log.AvalonLog.SelectedTextHighLighter.ColorHighlighting))
            this.Inlines.Add( sprintf " (%d Chars) in Log" highTxt.Length)
            )

        grid.Log.AvalonLog.SelectedTextHighLighter.OnHighlightCleared.Add ( fun () ->
            this.Inlines.Clear()
            this.Inlines.Add ( desc + if isSelOcc() then onTxt else offTxt)
            )

        this.MouseDown.Add ( fun _ -> // press mouse to toggle
            if isSelOcc() then
                sett.SetBool ("HighlightAllOccurrences", false)   // TODO turn off selection highlight in log too ?
            else
                sett.SetBool ("HighlightAllOccurrences", true)   // toggle
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

type SeffStatusBar (grid:TabsAndLog)  = 
    let bar = new Primitives.StatusBar()

    let add (side:Dock) (e:UIElement) = 
        let bi = new StatusBarItem(Content=e)
        DockPanel.SetDock(bi,side)
        bar.Items.Add bi |> ignore

        let s = new Separator()
        DockPanel.SetDock(s,side)
        bar.Items.Add s |> ignore

    let fsi = FsiRunStatus (grid)
    let errs = CheckerStatus(grid)
    do
        add Dock.Left  <| errs
        add Dock.Left  <| fsi
        add Dock.Left  <| SelectedTextStatus(grid)

        add Dock.Right  <| FsiOutputStatus(grid)
        if grid.Config.Hosting.IsHosted then     add Dock.Right  <|  AsyncStatus(grid)

        bar.Items.Add (new StatusBarItem()) |> ignore // to fill remaining gap


    member this.Bar =  bar

    member this.FsiStatus = fsi

    member this.CheckerStatus = errs




