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

type StatusBar (config:Config,cmds:Commands)  = // TODO better make it dependent on commands , not fsi
    let bar = new Primitives.StatusBar() 

    let padding = Thickness(6. , 2. , 6., 2. ) //left ,top, right, bottom)

    let fsi = Fsi.GetOrCreate(config)
    let checker = Checker.GetOrCreate(config)
     
    let fsiState = TextBlock(Text="FSI is initializing ...", Padding = padding, ContextMenu = makeContextMenu [ menuItem cmds.CancelFSI ])
    
    let compilerErrors = new TextBlock(Text="checking for Errors ..." , Padding = padding) 
    
    let originalBackGround = compilerErrors.Background
            
    let setErrors(es:FSharpErrorInfo[])= 
        if es.Length = 0 then 
            compilerErrors.Text <- "No compiler errors"
            compilerErrors.Background <- Brushes.Green |> brighter 120
            compilerErrors.ToolTip <- "FSarp Compiler Service found no Errors in this tab"
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
                if ers>0 then TextBlock(Text="Errors", FontSize = 14.)
                for e in es|> Seq.filter (fun e -> e.Severity = FSharpErrorSeverity.Error)  do new TextBlock(Text = sprintf "• Line %d: %s" e.StartLineAlternate e.Message)
                if was>0 then TextBlock(Text="Warnings", FontSize = 14.)
                for e in es|> Seq.filter (fun e -> e.Severity = FSharpErrorSeverity.Warning) do new TextBlock(Text = sprintf "• Line %d: %s" e.StartLineAlternate e.Message) 
                ]


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
        
        checker.OnChecked.Add setErrors

        checker.OnChecking.Add(fun idr -> 
                                    let id = !idr 
                                    async{
                                        do! Async.Sleep 200
                                        if id = !idr then 
                                            compilerErrors.Text <- "checking for Errors ..."
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


