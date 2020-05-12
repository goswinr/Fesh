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
open Seff.Model
open Seff.Config
open Seff.Editor

type StatusBar (config:Config)  = // TODO better make it dependent on commands , not fsi
    let bar = new Primitives.StatusBar() 

    let padding = Thickness(6. , 2. , 6., 2. ) //left ,top, right, bottom)

    let fsi = Fsi.Create(config)
    let checker = Checker.Create(config)

    let compilerErrors = new TextBlock(Text="checking for Errors ..." , Padding = padding) //FontWeight = FontWeights.Bold
    
    let originalBackGround = compilerErrors.Background

    let fsiState = new TextBlock(Text="FSI is initializing ...", Padding = padding) //FontWeight = FontWeights.Bold
        
    let setErrors(es:FSharpErrorInfo[])= 
        if es.Length = 0 then 
            compilerErrors.Text <- "No compiler errors"
            compilerErrors.Background <- Brushes.Green |> brighter 90
            compilerErrors.ToolTip <- "FSarp Compiler Service found no Errors in this tab"
        else 
            let ers = es|> Seq.filter (fun e -> e.Severity = FSharpErrorSeverity.Error) |> Seq.length
            let was = es.Length - ers

            qeerfgt

            
            if es.Length = 1 then compilerErrors.Text <- "There is 1 compiler error" 
            else                  compilerErrors.Text <- sprintf "There are %d compiler errors" es.Length
            compilerErrors.Background <- Brushes.Red   |> brighter 90  
            compilerErrors.ToolTip <- makePanelVert [ for e in es do new TextBlock(Text=sprintf "• Line %d: %A: %s" e.StartLineAlternate e.Severity e.Message)]

    
    do        
        bar.Items.Add compilerErrors          |> ignore 
        bar.Items.Add (new Separator())       |> ignore 
        bar.Items.Add fsiState                |> ignore 
        bar.Items.Add (new Separator())       |> ignore 
        bar.Items.Add (new StatusBarItem())   |> ignore // to fill remaining space
        
        fsi.OnStarted.Add(fun _ -> fsiState.Text <- "FSI is evaluating ..." )
        fsi.OnIsReady.Add(fun _ -> fsiState.Text <- "FSI is ready!" )
        
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


