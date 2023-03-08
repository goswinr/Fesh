namespace Seff.Editor

open System.Windows
open System.Collections.Generic
open FsEx.Wpf
open Seff
open Seff.Model

module AutoFixErrors = 
    
    let asked  = HashSet<string>()
    
    let mutable isMessageBoxOpen = false // because msg box would apear behind completion window and type info
     
    let ask(msg:string,ass:string,ied:IEditor) =
        // it is actually better to stat the message box from another thread ?
        isMessageBoxOpen <- true        
        async{  
            do! Async.SwitchToContext SyncWpf.context 
            match MessageBox.Show(
                IEditor.mainWindow,
                $"Do you want to add a refrence to\r\n\r\n{ass}.dll\r\n\r\non the first line of the script? \r\n\r\nTo fix this Error:\r\n{msg}" , 
                $"Add a reference to {ass} ?", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Question, 
                MessageBoxResult.Yes,// default result 
                MessageBoxOptions.None) with
            | MessageBoxResult.Yes -> 
                do! Async.SwitchToContext SyncWpf.context                        
                ied.AvaEdit.Document.Insert(0, $"#r \"{ass}\"\r\n") 
            | _ -> ()
            
            isMessageBoxOpen <- false
        }|> Async.Start

    let check(msg,ied:IEditor) =
        match Util.Str.between "must add a reference to assembly '" ","  msg with 
        |Some ass -> 
            if asked.Add msg then ask(msg,ass,ied)            
        |None -> ()


    let refrences(ied:IEditor,ch:CheckResults) =
        for e in ch.checkRes.Diagnostics do 
            if e.ErrorNumber = 1108 then 
                check(e.Message, ied) 