namespace Seff.Editor

open System
open System.Collections.Generic
open FSharp.Compiler.SourceCodeServices
open Seff.Util.String
    
module Packages=
  
    //https://gist.github.com/toburger/9786275
    //https://blog.nuget.org/20130520/Play-with-packages.html  

    //TODO use Nuget 3 !!!!!!!!!!!!!!!!!!!!!!!
    
    let Searched = Dictionary<string,string>()
    let mutable isRunning = false

    let checkForMissingPackage (tab:Editor)(e:FSharpErrorInfo) startOffset length=
        if e.ErrorNumber = 84 then 
            let doc = tab.AvaEdit.Document
            let hook = doc.CreateAnchor(startOffset)
            let errTxt =  doc.GetText(startOffset, length)
            let errLine = doc.GetText(doc.GetLineByOffset(startOffset))
            if errLine.StartsWith "#r " then 
                let _,name,_ = between "\"" "\"" errTxt
                if  not (name.Contains ".dll")  &&
                    not (name.Contains "\\")    &&
                    not (name.Contains "/")     &&
                    not (Searched.ContainsKey name) &&
                    not isRunning then 
                        let setNewRef (s:string) = // function to be called at end of loading to insert #r file ref path
                            if not hook.IsDeleted then 
                                let errTxtNow =  doc.GetText(hook.Offset, length)
                                if errTxtNow = errTxt then 
                                    doc.Replace(hook.Offset,length,s)  

                        isRunning <- true
                        printfn "-looking for package '%s' (not implemeted yet)" name 
                        //getInstallOk tab name setNewRef
                        isRunning <- false

