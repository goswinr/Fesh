namespace Seff.Views


open System
open System.Windows
open System.Windows.Media
open System.Windows.Documents
open System.Windows.Controls
open System.Windows.Controls.Primitives // status bar

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
    let compileCol = Brushes.Magenta                    |> freeze
    let grayText =  Brushes.Gray     |> darker 60      |> freeze
    let waitCol  =  Brushes.HotPink  |> brighter 80    |> freeze

open StatusbarStyle
open System.Threading

type CheckerStatus (grid:TabsAndLog) as this = 
    inherit TextBlock()

    let tabs = grid.Tabs
    let checkingTxt = "Checking for Errors ..."

    let mutable lastErrCount = -1
    let mutable lastFile : TextEditor = null
    let mutable scrollToSegm = None
    
    let k = ref 0L

    let getErrPanel(es:ErrorsBySeverity, addPersistInfo:bool) = 
        let erk = es.errors.Count
        let wak = es.warnings.Count
        let ink = es.infos.Count
        let hik = es.hiddens.Count
        let maxShowCount = 5 // for each typ of error, the maximum number of errors to show
        makePanelVert [
            if erk>0 then
                if addPersistInfo then TextBlock(Text = "Click on text in statusbar or press Ctrl + E keys to scroll to first error.", FontSize = StyleState.fontSize * 0.75, FontStyle = FontStyles.Italic, Margin=Thickness 3.0)
                if addPersistInfo then TextBlock(Text = "Press Ctrl + P to persist this tooltip window.", FontSize = StyleState.fontSize * 0.75, FontStyle = FontStyles.Italic, Margin=Thickness 3.0)                
                TextBlockSelectable(Text = "File: " + tabs.Current.FormattedFileName, FontSize = StyleState.fontSize * 0.8, Margin=Thickness 3.0 , TextWrapping = TextWrapping.Wrap)
                TextBlock(Text = "Errors:", FontSize = StyleState.fontSize , FontWeight = FontWeights.Bold )
            for e in Seq.truncate maxShowCount es.errors do
                TextBlockSelectable(Text = sprintf "• line %d: %s: %s" e.StartLine e.ErrorNumberText e.Message, FontSize = StyleState.fontSize * 0.9, Margin=Thickness 3.0 , TextWrapping = TextWrapping.Wrap)
            if erk > maxShowCount then
                TextBlock(Text = sprintf "• and %d more ..." (erk-maxShowCount), FontSize = StyleState.fontSize * 0.9)

            if wak>0 then
                TextBlock(Text="Warnings:", FontSize = StyleState.fontSize , FontWeight = FontWeights.Bold )
            for w in Seq.truncate maxShowCount es.warnings do
                TextBlockSelectable(Text = sprintf "• line %d: %s: %s" w.StartLine w.ErrorNumberText w.Message, FontSize = StyleState.fontSize * 0.9, Margin=Thickness 3.0 , TextWrapping = TextWrapping.Wrap)
            if wak > maxShowCount then
                TextBlock(Text = sprintf "• and %d more ..." (wak-maxShowCount), FontSize = StyleState.fontSize * 0.9)  
                
            if ink>0 then
                TextBlock(Text="Infos:", FontSize = StyleState.fontSize , FontWeight = FontWeights.Bold )
            for i in Seq.truncate maxShowCount es.infos do
                TextBlockSelectable(Text = sprintf "• line %d: %s: %s" i.StartLine i.ErrorNumberText i.Message, FontSize = StyleState.fontSize * 0.9, Margin=Thickness 3.0, TextWrapping = TextWrapping.Wrap )
            if ink > maxShowCount then
                TextBlock(Text = sprintf "• and %d more ..." (ink-maxShowCount), FontSize = StyleState.fontSize * 0.9)  
            
            if hik>0 then
                TextBlock(Text="Hidden Infos:", FontSize = StyleState.fontSize , FontWeight = FontWeights.Bold )
            for h in Seq.truncate maxShowCount es.hiddens do
                TextBlockSelectable(Text = sprintf "• line %d: %s: %s" h.StartLine h.ErrorNumberText h.Message, FontSize = StyleState.fontSize * 0.9, Margin=Thickness 3.0, TextWrapping = TextWrapping.Wrap )
            if hik > maxShowCount then
                TextBlock(Text = sprintf "• and %d more ..." (ink-maxShowCount), FontSize = StyleState.fontSize * 0.9)  
            ]


    let updateCheckState(checkState:FileCheckState)= 
        let k0 = Interlocked.Increment k
        //ISeffLog.log.PrintfnDebugMsg $"updateCheckState: {checkState}"
        match checkState with
        | Done res ->
            if k.Value = k0 then 
                //ISeffLog.log.PrintfnDebugMsg $"checking  Done. Arrived in status bar with {res.checkRes.Diagnostics.Length} msgs"
                let es = res.errors
                let erWas = es.errorsAndWarnings  

                if erWas.Count = 0 then
                    if lastErrCount <> 0  || lastFile <> tabs.Current.Editor.AvaEdit then // no UI update needed in this case
                        this.Text <- "No compiler errors"
                        this.Background <- okColor
                        this.ToolTip <- "FSharp Compiler Service found no Errors in"+ Environment.NewLine + tabs.Current.FormattedFileName
                        lastFile <- tabs.Current.Editor.AvaEdit
                        lastErrCount <- 0
                        scrollToSegm <- None
                else
                    lastFile <- tabs.Current.Editor.AvaEdit
                    lastErrCount <- erWas.Count
                    erWas.Sort(fun x y -> Operators.compare x.StartLine y.StartLine)// sort because we are not sure if they are already sorted                
                    
                    let erk = es.errors.Count
                    let wak = es.warnings.Count
                    if wak > 0 && erk > 0 then
                        this.Text <- sprintf " %d compiler errors, %d warnings, first one on line: %d" erk wak erWas.[0].StartLine
                        this.Background <- errColor
                    elif wak > 0 then
                        this.Text <- sprintf " %d compiler warnings, first one on line %d" wak erWas.[0].StartLine
                        this.Background <- warnColor
                    elif erk > 0 then
                        this.Text <- sprintf " %d compiler errors, first one on line: %d" erk erWas.[0].StartLine
                        this.Background <- errColor
                    else
                        this.Text <- $"No compiler errors, {es.hiddens.Count + es.infos.Count} Infos" 
                        this.Background <- okColor                       
                    
                    let tip = new ToolTip(Content = getErrPanel(es, true) )
                    tip.Placement <- Primitives.PlacementMode.Top //https://docs.microsoft.com/en-us/dotnet/framework/wpf/controls/popup-placement-behavior
                    tip.VerticalOffset <- -6.0
                    this.ToolTip <- tip
              
        | Checking ->            
            async{
                do! Async.Sleep 250 // delay  to only show check in progress massage if it takes long, otherwise just show results via on checked event                
                match IEditor.current with 
                |None -> ()
                |Some e -> 
                    match e.FileCheckState with
                    | Done _ -> () //now need to update
                    | Checking -> 
                        if k.Value = k0 then 
                            lastErrCount <- -1
                            this.Text <- checkingTxt
                            this.Background <- waitCol //originalBackGround
                            this.ToolTip <- sprintf "Checking %s for Errors ..." tabs.Current.FormattedFileName
                    
            } |> Async.StartImmediate
    
    do
        lastErrCount <- -1
        this.Padding <-textPadding
        this.Text <- checkingTxt
        this.Background <- waitCol //originalBackGround

        tabs.OnTabChanged.Add (fun t -> updateCheckState(t.Editor.FileCheckState))
        Checker.CheckingStateChanged.Add updateCheckState        
        this.MouseLeftButtonDown.Add ( fun a -> CheckerStatus.goToNextSegment(grid.Tabs.Current.Editor))
        
         
    member this.GetErrorPanelCached(ed:IEditor) = 
        match ed.FileCheckState with
        | Done res -> Some (getErrPanel(res.errors,false))               
        | _ -> None
        
    static member goToNextSegment(ed:Editor) =
        match ErrorUtil.getNextSegment(ed) with 
        |None -> ()
        |Some seg ->  ed.Folds.GoToOffsetAndUnfold (seg.Offset, seg.Length, false)

type FsiRunStatus (grid:TabsAndLog) as this = 
    inherit TextBlock()
    do
        this.Padding <- textPadding
        this.Inlines.Add ("FSI is initializing . . .")
        this.Background <- waitCol //originalBackGround
        //this.ContextMenu <- makeContextMenu [ menuItem cmds.CancelFSI ]
        this.ToolTip <- "Shows the status of the fsi evaluation core. This is the same for all tabs. Only one script can run at the time."

        
        grid.Tabs.Fsi.OnCompiling.Add(fun codeToEval ->
            this.Background <- activeCol
            this.Inlines.Clear()
            match codeToEval.editor.FilePath with
            |Deleted fi|SetTo fi ->
                match codeToEval.amount with
                | All                 ->  this.Inlines.Add(new Run ("FSI is compiling "          , Foreground = grayText))
                | ContinueFromChanges ->  this.Inlines.Add(new Run ("FSI continues to compiling ", Foreground = grayText))
                | FsiSegment _        ->  this.Inlines.Add(new Run ("FSI is compiling a part of ", Foreground = grayText))
                this.Inlines.Add( new Run (fi.Name, FontFamily = StyleState.fontEditor) )
                this.Inlines.Add( new Run (" . . ."                                              , Foreground = grayText))
            |NotSet dummyName ->
                this.Inlines.Add( "FSI is compiling "+dummyName + " . . ." )
            )        
        
        grid.Tabs.Fsi.OnEmitting.Add(fun codeToEval ->
            this.Background <- compileCol
            this.Inlines.Clear()
            match codeToEval.editor.FilePath with
            |Deleted fi|SetTo fi ->
                match codeToEval.amount with
                | All                 ->  this.Inlines.Add(new Run ("FSI is running ",           Foreground = grayText))
                | ContinueFromChanges ->  this.Inlines.Add(new Run ("FSI continues to run "   ,  Foreground = grayText))
                | FsiSegment _        ->  this.Inlines.Add(new Run ("FSI is running a part of ", Foreground = grayText))
                this.Inlines.Add( new Run (fi.Name, FontFamily = StyleState.fontEditor) )
                this.Inlines.Add( new Run (" . . ."                                           , Foreground = grayText))
            |NotSet dummyName ->
                this.Inlines.Add( "FSI is running "+dummyName + " . . ." )
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
        this.ToolTip <- "Click here to enable or disable the default output from fsi in the log window\r\n(This will also reset FSI.)"
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
    let sync = "FSI evaluation mode: Synchronous"
    let asyn = "FSI evaluation mode: Asynchronous"

    do
        this.Padding <- textPadding
        this.Text <- if isAsync then asyn else sync
        this.ToolTip <- "Click to switch between synchronous and asynchronous evaluation in FSI,\r\nsynchronous is needed for UI interaction,\r\nasynchronous allows easy cancellation and keeps the editor window alive"
        this.MouseDown.Add(fun _ -> fsi.ToggleSync()) //done in fsi module      // TODO better make it dependent on commands , not fsi
        fsi.OnModeChanged.Add(function
            | InSync             -> this.Text <- sync
            | Async472 | Async70 -> this.Text <- asyn  )

#nowarn "44" //for obsolete grid.Log.AvalonLog.AvalonEdit

type SelectedEditorTextStatus (grid:TabsAndLog) as this = 
    inherit TextBlock() 
    let noSelTxt = new Run ("no selection in Editor", Foreground = SelectionHighlighting.colorInactive) //Editor Selection Highlighting" 
    let tipText = "Highlights and counts the occurrences of the currently selected Text in the current Editor.\r\nMinimum two characters and no line breaks.\r\nClick here to scroll through all occurrences."
    let mutable scrollToIdx = 0 

    let fillStatusMarkLog triggerNext  = 
        let sel = grid.Tabs.Current.Editor.DrawingServices.selection
        if triggerNext then
            match grid.Log.SelectionHighlighter with 
            |None -> ISeffLog.log.PrintfnAppErrorMsg "Log.SelectionHighlighter not set up"
            |Some hili -> hili.MarkInLog(sel.Word)            
        
        if sel.Offsets.Count = 0 then 
            this.Inlines.Clear()
            this.Inlines.Add noSelTxt
        else
            this.Inlines.Clear()
            this.Inlines.Add( $"%d{sel.Offsets.Count} of "  )
            this.Inlines.Add( new Run (sel.Word, FontFamily = StyleState.fontEditor, Background = SelectionHighlighting.colorEditor))
            this.Inlines.Add( $" (%d{sel.Word.Length} Chars) " )

    do
        this.Padding <- textPadding
        this.ToolTip <-  tipText
        this.Inlines.Add noSelTxt
        SelectionHighlighting.GlobalFoundSelectionsEditor.Add(fillStatusMarkLog)
       
        // on each click loop through all locations where text appears
        this.MouseDown.Add ( fun _ -> // press mouse to scroll to them
            let ed = grid.Tabs.Current.Editor
            let sel = ed.DrawingServices.selection
            if sel.Offsets.Count > 0 then
                //ed.AvaEdit.Focus() |> ignore 
                if scrollToIdx >= sel.Offsets.Count then scrollToIdx <- 0
                let ed = grid.Tabs.Current.Editor
                let off = sel.Offsets.[scrollToIdx]
                if off < ed.AvaEdit.Document.TextLength then                    
                    ed.Folds.GoToOffsetAndUnfold(off, sel.Word.Length, true )                    
                    scrollToIdx <- scrollToIdx + 1
                else
                    scrollToIdx <- 0
            ) 

type SelectedLogTextStatus (grid:TabsAndLog) as this = 
    inherit TextBlock()
    let log = grid.Log    
    let noSelTxt = new Run ("no selection in Log", Foreground = SelectionHighlighting.colorInactive) //Log Selection Highlighting " 
    let tipText = "Highlights and counts the occurrences of the currently selected Text in the Log output.\r\nMinimum two characters and no line breaks.\r\nClick here to scroll through all occurrences."
    let mutable scrollToIdx = 0 

    let setStatusMarkEd triggerNext =
        match log.SelectionHighlighter with 
        |None -> ISeffLog.log.PrintfnAppErrorMsg "Log.SelectionHighlighter not set up"
        |Some hiLi ->
            if triggerNext then 
                grid.Tabs.Current.Editor.DrawingServices.selection.RedrawMarksInEditor(hiLi.Word)

            if hiLi.Offsets.Count = 0 then
                this.Inlines.Clear()
                this.Inlines.Add noSelTxt
            else
                this.Inlines.Clear()
                this.Inlines.Add( sprintf $"%d{hiLi.Offsets.Count} of ")
                this.Inlines.Add( new Run (hiLi.Word, FontFamily = StyleState.fontEditor, Background = SelectionHighlighting.colorLog))
                this.Inlines.Add( sprintf " (%d Chars) " hiLi.Word.Length)

    do
        this.Padding <- textPadding
        this.ToolTip <- tipText
        this.Inlines.Add noSelTxt 
        SelectionHighlighting.FoundSelectionsLog.Add(setStatusMarkEd)
       
        // on each click loop through all locations where text appears
        this.MouseDown.Add ( fun _ -> // press mouse to scroll to them
            match log.SelectionHighlighter with 
            |None -> ()
            |Some hiLi -> 
                if hiLi.Offsets.Count > 0 then
                    if scrollToIdx >= hiLi.Offsets.Count then scrollToIdx <- 0                
                    let off = hiLi.Offsets.[scrollToIdx]
                    let doc = log.AvalonEditLog.Document
                    if off < doc.TextLength then
                        //log.AvalonEditLog.Focus() |> ignore                         
                        //match log.Folds with Some f -> f.GoToOffsetAndUnfold(off, sel.Word.Length, true )  |None ->()                  
                        let ln = doc.GetLineByOffset(off)
                        log.AvalonEditLog.ScrollTo(ln.LineNumber,1)
                        log.AvalonEditLog.Select(off, hiLi.Word.Length)                        
                        scrollToIdx <- scrollToIdx + 1
                    else
                        scrollToIdx <- 0
            )    

        grid.Tabs.OnTabChanged.Add ( fun _ -> match log.SelectionHighlighter with|None -> () |Some hiLi -> hiLi.Update()) 

type SeffStatusBar (grid:TabsAndLog)  = 
    let bar = new Primitives.StatusBar()

    let addSep (side:Dock) =         
        let s = new Separator()
        DockPanel.SetDock(s,side)
        bar.Items.Add s |> ignore


    let add (side:Dock) (e:UIElement) = 
        let bi = new StatusBarItem(Content=e)
        DockPanel.SetDock(bi,side)
        bar.Items.Add bi |> ignore


    let fsi = FsiRunStatus (grid)
    let errs = CheckerStatus(grid)
    do
        add    Dock.Left  <| errs // on very left
        addSep Dock.Left         
        add    Dock.Left  <| SelectedEditorTextStatus(grid)
        addSep Dock.Left         
        add    Dock.Left  <| SelectedLogTextStatus(grid)
        addSep Dock.Left         
        
        add    Dock.Right <| fsi // on very right
        addSep Dock.Right
        add    Dock.Right <| FsiOutputStatus(grid)
        addSep Dock.Right
        
        if grid.Config.RunContext.IsHosted then
            add    Dock.Right  <|  AsyncStatus(grid)
            addSep Dock.Right

        bar.Items.Add (new StatusBarItem()) |> ignore // to fill remaining gap


    member this.Bar =  bar

    member this.FsiStatus = fsi

    member this.CheckerStatus = errs




