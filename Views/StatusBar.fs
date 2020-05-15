namespace Seff.Views

open Seff
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

    let bar = new Primitives.StatusBar() 
   

    let padding = Thickness(6. , 2. , 6., 2. ) //left ,top, right, bottom)

    let fsi = Fsi.GetOrCreate(config)
    let checker = Checker.GetOrCreate(config)
     
    let fsiState = TextBlock(Text="FSI is initializing ...", Padding = padding, ContextMenu = makeContextMenu [ menuItem cmds.CancelFSI ])
    
    let checkingTxt = "checking for Errors ..."

    let compilerErrors = new TextBlock(Text=checkingTxt , Padding = padding) 
    
    let originalBackGround = compilerErrors.Background
            
    let setErrors(iEditor:IEditor, es:FSharpErrorInfo[])= 
        if tabs.Current.Editor.Id = iEditor.Id then
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
                    TextBlock(Text = tabs.Current.FormatedFileName)
                    if ers>0 then TextBlock(Text="Errors", FontSize = 14.)
                    for e in es|> Seq.filter (fun e -> e.Severity = FSharpErrorSeverity.Error)  do new TextBlock(Text = sprintf "• Line %d: %s" e.StartLineAlternate e.Message)
                    if was>0 then TextBlock(Text="Warnings", FontSize = 14.)
                    for e in es|> Seq.filter (fun e -> e.Severity = FSharpErrorSeverity.Warning) do new TextBlock(Text = sprintf "• Line %d: %s" e.StartLineAlternate e.Message) 
                    ]
        else
            compilerErrors.Text <- checkingTxt
            compilerErrors.Background <- originalBackGround

    do        
        bar.Items.Add compilerErrors          |> ignore 
        bar.Items.Add (new Separator())       |> ignore 
        bar.Items.Add fsiState                |> ignore 
        bar.Items.Add (new Separator())       |> ignore 
        bar.Items.Add (new StatusBarItem())   |> ignore // to fill remaining space
        
        fsi.OnStarted.Add(fun code -> 
            fsiState.Background <- Brushes.Orange   |> brighter 20 
            match code.file with 
            |Some fi -> 
                if code.allOfFile then fsiState.Text <- sprintf "FSI is running %s  ..." fi.Name
                else                   fsiState.Text <- sprintf "FSI is running segments from file %s  ..." fi.Name
            | None ->                  fsiState.Text <- "FSI is running ..." )

        fsi.OnIsReady.Add(fun _ -> 
            fsiState.Text <- "FSI is ready!" 
            fsiState.Background <- Brushes.Green |> brighter 120)
        
        tabs.OnTabChanged.Add (fun tab -> 
            if tab.Editor.CheckRes.IsSome then 
                setErrors(tab.Editor, tab.Editor.CheckRes.Value.checkRes.Errors))
            
        checker.OnChecked.Add setErrors

        checker.OnChecking.Add(fun (iEditor, checkId) -> 
                                    let id = !checkId 
                                    async{
                                        do! Async.Sleep 300
                                        if id = !checkId // to cancel if new checker has already started ( from tab change or text enter)
                                        && iEditor.Id = tabs.Current.Editor.Id  // to cancel if tab changed
                                        && iEditor.NeedsChecking then // to cancel if check completed already
                                            compilerErrors.Text <- checkingTxt
                                            compilerErrors.Background <- originalBackGround 
                                        } |> Async.StartImmediate )

        

    member this.Bar =  bar
  

    member this.AddFsiSynchModeStatus()=
        let atPosition = 0
        bar.Items.Insert(atPosition, new StatusBarItem(Content="FSI evaluation mode: "))
        let asyncDesc = new TextBlock(Text="*unknown*", Padding = padding)
        asyncDesc.ToolTip <- "Click to switch between synchronous and asynchronous evaluation in FSI,\r\nsynchronous is needed for UI interaction,\r\nasynchronous allows easy cancellation and keeps the editor window alive"
        asyncDesc.MouseDown.Add(fun _ -> fsi.ToggleSync()) //done in fsi module
        
        bar.Items.Insert(atPosition+1,asyncDesc)
        bar.Items.Insert(atPosition+2, new Separator())          
        fsi.OnModeChanged.Add(function 
            | Sync  -> asyncDesc.Text <- "Synchronos" 
            | Async -> asyncDesc.Text <- "Asynchronos"  )


