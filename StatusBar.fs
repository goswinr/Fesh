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

module StatusBar =
    let asyncDesc = 
        let bi = StatusBarItem(Content="*unknown*")
        bi.ToolTip <- "Click to switch between synchronous and asynchronous evaluation in FSI,\r\nsynchronous is needed for UI interaction,\r\nasynchronous allows easy cancellation and keeps the editor window alive"
        //bi.MouseDown.Add(fun _ -> toggleSync()) //done in fsi module
        bi

    let compilerErrors=
        let tb = TextBox(Text="checking for Errors...")
        tb.FontWeight <- FontWeights.Bold
        tb

    let setErrors(es:FSharpErrorInfo[])= 
        if es.Length = 0 then 
            compilerErrors.Text <- "No Errors"
            compilerErrors.Background <- Brushes.Green |> brighter 90            
        else 
            compilerErrors.Text <- sprintf "%d Errors" es.Length
            compilerErrors.Background <- Brushes.Red   |> brighter 90  
            compilerErrors.ToolTip <- makePanelVert [ for e in es do TextBlock(Text=sprintf "• Line %d: %A: %s" e.StartLineAlternate e.Severity e.Message)]

    let Bar = 
        let b = new StatusBar()
        b.Items.Add compilerErrors      |> ignore 
        b.Items.Add (Separator())       |> ignore 
        b.Items.Add (StatusBarItem())   |> ignore // to fill remaining space
        b

    let addSwitchFforSyncchonisationMode()=
        Bar.Items.Insert(0,StatusBarItem(Content="FSI evaluation mode: "))
        Bar.Items.Insert(1,asyncDesc)
        Bar.Items.Insert(2,Separator())          
    
