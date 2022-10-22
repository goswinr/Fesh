namespace Seff.Editor


open System
open System.Windows
open System.Windows.Media
open System.Windows.Input

open FSharp.Compiler.Tokenization // for keywords

open AvalonEditB
open AvalonEditB.Utils
open AvalonEditB.Document
open AvalonLog

open Seff
open Seff.Editor.SelectionHighlighting
open Seff.Model
open Seff.Config
open Seff.Util.Str
open FSharp.Compiler.EditorServices
open System.Collections.Generic

module AutoFixErrors = 
    
    let saidNo = HashSet<string>()

    let refrences(ied:IEditor,ch:CheckResults) =
        for e in ch.checkRes.Diagnostics do 
            if e.ErrorNumber = 1108 then 
                match Util.Str.between "You must add a reference to assembly '" ","  e.Message with 
                |Some ass -> 
                    if saidNo.Contains ass |> not then 
                        match MessageBox.Show($"Do you want to add a refrence to\r\n{ass}.dll\r\non the first line? \r\n\r\nBecause of Error:\r\n{e.Message}" , 
                                              $"Add refernece to {ass} ?", 
                                              MessageBoxButton.YesNo, MessageBoxImage.Question) with
                        | MessageBoxResult.Yes -> ied.AvaEdit.Document.Insert(0, $"#r \"{ass}\"\r\n")
                        | MessageBoxResult.No -> 
                            saidNo.Add(ass)  |> ignore 
                        | _ -> 
                            saidNo.Add(ass)  |> ignore                    
                
                |None -> ()


