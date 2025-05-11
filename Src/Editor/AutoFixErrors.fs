namespace Fesh.Editor

open Avalonia
open System.Collections.Generic
open Fittings
open Fesh
open Fesh.Model
open FSharp.Compiler.CodeAnalysis

module AutoFixErrors =

    let asked  = HashSet<string>()
    let askedAgain  = HashSet<string>()

    let mutable isMessageBoxOpen = false // because errMsg box would appear behind completion window and type info

    let ask(errMsg:string,assemblyName:string) =
        // it is actually better to start the message box from another thread ?
        isMessageBoxOpen <- true
        async{
            do! Async.SwitchToContext SyncContext.context
            match MessageBox.Show(
                IEditor.mainWindow,
                $"Do you want to add a reference to\r\n\r\n{assemblyName}.dll\r\n\r\non the first line of the script? \r\n\r\nTo fix this Error:\r\n{errMsg}" ,
                $"Fesh | Add a reference to {assemblyName} ?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.Yes, // default result
                MessageBoxOptions.None) with
            | MessageBoxResult.Yes ->
                do! Async.SwitchToContext SyncContext.context
                match IEditor.current with
                |Some ied ->
                    ied.AvaEdit.Document.Insert(0, $"#r \"{assemblyName}\" // auto added\r\n")
                |None -> ()
            | _ -> ()

            isMessageBoxOpen <- false
        }|> Async.Start

    let check(errMsg) =
        match Util.Str.between "add a reference to assembly '" ","  errMsg with //assembly name with version number
        |Some assemblyName ->
            let n = assemblyName.Trim().Split(' ').[0] // remove version number
            if asked.Add n || askedAgain.Add n then ask(errMsg,n)
        |None ->
            match Util.Str.between "add a reference to assembly '" "'"  errMsg with //assembly name without version number
            |Some assemblyName ->
                let n = assemblyName.Trim()
                if asked.Add n || askedAgain.Add n then ask(errMsg,n)
            |None -> ()


    let references(checkRes:FSharpCheckFileResults) =
        for e in checkRes.Diagnostics do
            if e.Message.Contains "add a reference to assembly" then
                check(e.Message)