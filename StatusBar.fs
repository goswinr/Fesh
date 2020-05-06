namespace Seff

open System
open System.Windows
open System.Windows.Controls
open System.Windows.Controls.Primitives // status bar
open System.Windows.Media
open ICSharpCode
open Seff.Util.WPF
open FSharp.Compiler.SourceCodeServices
open System.Windows.Automation.Peers
open Seff.Model

type StatusBar private ()  =
    static let bar = new Primitives.StatusBar() 

    static let compilerErrors = new TextBlock(Text="checking for Errors ...") //FontWeight = FontWeights.Bold
    
    static let fsiState = new TextBlock(Text="FSI is initializing ...") //FontWeight = FontWeights.Bold
        

    static member Initialize() =        
        bar.Items.Add compilerErrors          |> ignore 
        bar.Items.Add (new Separator())       |> ignore 
        bar.Items.Add fsiState                |> ignore 
        bar.Items.Add (new Separator())       |> ignore 
        bar.Items.Add (new StatusBarItem())   |> ignore // to fill remaining space
        
        Fsi.OnStarted.Add(fun _ -> fsiState.Text <- "FSI is evaluating ..." )
        Fsi.OnIsReady.Add(fun _ -> fsiState.Text <- "FSI is ready!" )    

    static member SetErrors(es:FSharpErrorInfo[])= 
        if es.Length = 0 then 
            compilerErrors.Text <- "No Errors"
            compilerErrors.Background <- Brushes.Green |> brighter 90
            compilerErrors.ToolTip <- "FSarp Compiler Service found no Errors in this tab"
        else 
            compilerErrors.Text <- sprintf "%d Errors" es.Length
            compilerErrors.Background <- Brushes.Red   |> brighter 90  
            compilerErrors.ToolTip <- makePanelVert [ for e in es do new TextBlock(Text=sprintf "• Line %d: %A: %s" e.StartLineAlternate e.Severity e.Message)]

    static member Bar =  bar

    static member AddFsiSynchModeStatus()=
        let atPosition = 0
        bar.Items.Insert(atPosition, new StatusBarItem(Content="FSI evaluation mode: "))
        let asyncDesc = new TextBlock(Text="*unknown*")
        asyncDesc.ToolTip <- "Click to switch between synchronous and asynchronous evaluation in FSI,\r\nsynchronous is needed for UI interaction,\r\nasynchronous allows easy cancellation and keeps the editor window alive"
        asyncDesc.MouseDown.Add(fun _ -> Fsi.ToggleSync()) //done in fsi module
        
        bar.Items.Insert(atPosition+1,asyncDesc)
        bar.Items.Insert(atPosition+2, new Separator())          
        Fsi.OnModeChanged.Add(function 
            | Sync  -> asyncDesc.Text <- "Synchronos" 
            | Async -> asyncDesc.Text <- "Asynchronos"  )
