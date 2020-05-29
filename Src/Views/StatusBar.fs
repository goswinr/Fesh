namespace Seff.Views

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

type StatusBar (grid:TabsAndLog, cmds:Commands)  = // TODO better make it dependent on commands , not fsi

    let log = grid.Log
    let tabs= grid.Tabs
    let config = grid.Config 
    let fsi = tabs.Fsi

    let bar = new Primitives.StatusBar() 
   

    let padding = Thickness(6. , 2. , 6., 2. ) //left ,top, right, bottom)

    
    let checker = Checker.GetOrCreate(config)
     
    let fsiState = TextBlock(Text="FSI is initializing ...", Padding = padding, ContextMenu = makeContextMenu [ menuItem cmds.CancelFSI ])
    
    let checkingTxt = "checking for Errors ..."

    let compilerErrors = new TextBlock(Text=checkingTxt , Padding = padding) 
    
    let originalBackGround = compilerErrors.Background
            
    let updateCheckState(iEditor:IEditor)= 
        //log.PrintDebugMsg "Setting errors for %A %A " iEditor.FileInfo iEditor.CheckRes.Value.checkRes.Errors.Length 
        match iEditor.CheckState with
        | Done res ->                                            
                let es = res.checkRes.Errors
                if es.Length = 0 then 
                    compilerErrors.Text <- "No compiler errors"
                    compilerErrors.Background <- Brushes.Green |> brighter 120
                    compilerErrors.ToolTip <- "FSarp Compiler Service found no Errors in " + tabs.Current.FormatedFileName
                else 
                    let ers = es|> Seq.filter (fun e -> e.Severity = FSharpErrorSeverity.Error) |> Seq.length
                    let was = es.Length - ers
                    if ers = 0 then 
                        compilerErrors.Text <- sprintf "Compiler warnings: %d" was
                        compilerErrors.Background <- Brushes.Yellow   |> brighter 40  
                    elif was = 0 then
                        compilerErrors.Text <- sprintf "Compiler errors: %d" ers
                        compilerErrors.Background <- Brushes.Red   |> brighter 150  
                    else
                        compilerErrors.Text <- sprintf "Compiler errors: %d, warnings: %d" ers was
                        compilerErrors.Background <- Brushes.Red   |> brighter 150             
                    compilerErrors.ToolTip <- makePanelVert [                         
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
                            compilerErrors.Text <- checkingTxt
                            compilerErrors.Background <- originalBackGround 
                    | Done _ | NotStarted | Failed -> ()
            } |> Async.StartImmediate 
        
        | NotStarted -> // these below never happen because event is only triggerd on success
            compilerErrors.Text <- "Initializing compiler.."
            compilerErrors.Background <- originalBackGround 
        
        | Failed -> // these below never happen because event is only triggerd on success
            compilerErrors.Text <- "Fs Checker failed to complete."
            compilerErrors.Background <- Brushes.Magenta 

    


    do        
        
        fsi.OnStarted.Add(fun code -> 
            fsiState.Background <- Brushes.Orange   |> brighter 20 
            match code.file with 
            |SetTo fi -> 
                if code.allOfFile then fsiState.Text <- sprintf "FSI is running %s  ..." fi.Name
                else                   fsiState.Text <- sprintf "FSI is running selected code from file %s  ..." fi.Name
            |NotSet ->                 fsiState.Text <- "FSI is running ..." )

        fsi.OnIsReady.Add(fun _ -> 
            fsiState.Text <- "FSI is ready" 
            fsiState.Background <- Brushes.Green |> brighter 120)
        
        tabs.OnTabChanged.Add (fun t -> updateCheckState(t.Editor))
            
        checker.OnChecked.Add updateCheckState

        checker.OnChecking.Add updateCheckState

        bar.Items.Add compilerErrors          |> ignore 
        bar.Items.Add (new Separator())       |> ignore 
        bar.Items.Add fsiState                |> ignore 
        bar.Items.Add (new Separator())       |> ignore 
        
        //------- add Async or Sync state-------------
        match config.HostingInfo.Mode with
        |Standalone -> 
            bar.Items.Add (new StatusBarItem())   |> ignore // to fill remaining space
        |Hosted _ ->
            let ini =   
                match config.Settings.GetBool "asyncFsi" true  with
                | false  ->  "FSI evaluation mode: Synchronos" 
                | true ->  "FSI evaluation mode: Asynchronos"
        
            let asyncDesc = new TextBlock( Text =  ini  , Padding = padding) 
            bar.Items.Add( new StatusBarItem(Content=asyncDesc)) |> ignore
            bar.Items.Add( new Separator())  |> ignore 
            bar.Items.Add( new StatusBarItem())   |> ignore // to fill remaining space
            asyncDesc.ToolTip <- "Click to switch between synchronous and asynchronous evaluation in FSI,\r\nsynchronous is needed for UI interaction,\r\nasynchronous allows easy cancellation and keeps the editor window alive"
            asyncDesc.MouseDown.Add(fun _ -> fsi.ToggleSync()) //done in fsi module
      
            fsi.OnModeChanged.Add(function 
                | Sync  -> asyncDesc.Text <- "FSI evaluation mode: Synchronos" 
                | Async -> asyncDesc.Text <- "FSI evaluation mode: Asynchronos"  )





    member this.Bar =  bar
  



