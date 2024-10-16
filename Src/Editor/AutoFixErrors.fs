﻿namespace Fesh.Editor

open System.Windows
open System.Collections.Generic
open Fittings
open Fesh
open Fesh.Model
open FSharp.Compiler.CodeAnalysis

module AutoFixErrors =

    let asked  = HashSet<string>()
    let askedAgain  = HashSet<string>()

    let mutable isMessageBoxOpen = false // because msg box would appear behind completion window and type info

    let ask(msg:string,ass:string) =
        // it is actually better to stat the message box from another thread ?
        isMessageBoxOpen <- true
        async{
            do! Async.SwitchToContext SyncWpf.context
            match MessageBox.Show(
                IEditor.mainWindow,
                $"Do you want to add a reference to\r\n\r\n{ass}.dll\r\n\r\non the first line of the script? \r\n\r\nTo fix this Error:\r\n{msg}" ,
                $"Fesh | Add a reference to {ass} ?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.Yes,// default result
                MessageBoxOptions.None) with
            | MessageBoxResult.Yes ->
                do! Async.SwitchToContext SyncWpf.context
                match IEditor.current with
                |Some ied ->
                    ied.AvaEdit.Document.Insert(0, $"#r \"{ass}\" // auto added\r\n")
                |None -> ()
            | _ -> ()

            isMessageBoxOpen <- false
        }|> Async.Start

    let check(msg) =
        match Util.Str.between "add a reference to assembly '" ","  msg with //assembly name with version number
        |Some ass ->
            if asked.Add msg || askedAgain.Add msg then ask(msg,ass)
        |None ->
            match Util.Str.between "add a reference to assembly '" "'"  msg with //assembly name without version number
            |Some ass ->
                if asked.Add msg || askedAgain.Add msg then ask(msg,ass)
            |None -> ()


    let references(checkRes:FSharpCheckFileResults) =
        for e in checkRes.Diagnostics do
            if e.Message.Contains "add a reference to assembly" then
                check(e.Message)