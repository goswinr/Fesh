namespace Seff.Config

open Seff
open Seff.Model
open System
open System.Text
open System.IO
open System.Collections.ObjectModel
open System.Globalization
open FSharp.Compiler.AbstractIL.Internal.Library

type UsedFile = {fileInfo:FileInfo ; date:DateTime}
   
type RecentlyUsedFiles  (log:ISeffLog, hostInfo:Hosting) =
    let writer = SaveWriter(log)
        
    let filePath = hostInfo.GetPathToSaveAppData("RecentlyUsedFiles.txt")
        
    let recentFilesChangedEv = new Event<unit>()
        
    let recentFilesStack : Collections.Generic.Stack<UsedFile> = 
        // TODO this could be done async too?        
        
        let stack = Collections.Generic.Stack<UsedFile>()
        try            
            //if IO.File.Exists filePath then // do this check only when creating menu items
            for ln in  IO.File.ReadAllLines filePath |> Seq.rev do
                let path , d = Util.String.splitOnce "|" ln                
                match DateTime.TryParseExact(d, "yyyy-MM-dd HH:mm", null,  DateTimeStyles.None) with
                | true, date -> stack.Push( {fileInfo=FileInfo(path) ; date=date}) |> ignore  
                | _ ->          stack.Push( {fileInfo=FileInfo(path) ; date=DateTime.MinValue}) |> ignore 
                                  
        with e -> 
            log.PrintfnInfoMsg "No recently used files found. (This is expected on first use of the App)"  
                  
        stack   

        
    /// the maximum number of recxent files to be saved
    /// the amount of files in the recently used menu can be controlled separetly in menu.fs
    let maxCount = 100       
                

    let getStringRaiseEvent() = 
        let sb = StringBuilder()
        let Dup = Collections.Generic.HashSet()
        let k = ref 0
        for uf in recentFilesStack  do   // iteration starts at top element of stack
            incr k
            if !k < maxCount then 
                if not <| Dup.Contains uf.fileInfo.FullName then 
                    let date = uf.date.ToString("yyyy-MM-dd HH:mm")
                    let file = uf.fileInfo.FullName
                    sb.AppendLine(file + "|" + date)  |> ignore
                    Dup.Add uf.fileInfo.FullName  |> ignore 

        recentFilesChangedEv.Trigger()  //this event will be triggered 1000 ms after new tabs are created
        sb.ToString()    

    
      /// does not save 
    member this.Add(fi:FileInfo) =         
        if recentFilesStack.Count = 0  then 
            recentFilesStack.Push {fileInfo=fi ; date=DateTime.Now }
        else
            if recentFilesStack.Peek().fileInfo.FullName = fi.FullName then 
                recentFilesStack.Pop()  |> ignore // pop old date add new date
            recentFilesStack.Push {fileInfo=fi ; date=DateTime.Now }   
    
    member this.Save() =         
        writer.WriteDelayed(filePath, getStringRaiseEvent, 500)

    
    member this.AddAndSave(fi:FileInfo) =         
        this.Add(fi)
        this.Save()            

        
    /// the first elemnt in this array the top of stack
    member this.GetUniqueExistingSorted() = 

        let xs = ResizeArray()
        let Dup = Collections.Generic.HashSet()
        for uf in recentFilesStack do
            let lc = uf.fileInfo.FullName.ToLowerInvariant()
            if not (Dup.Contains lc) then 
                Dup.Add lc |> ignore 
                if File.Exists lc then // async is done in Menu.setRecentFiles()
                    xs.Add uf
        
        xs |> Util.General.sortInPlaceBy ( fun uf -> uf.date)
        xs.Reverse()
        xs                    
        

    member this.Contains(s:string) = 
        recentFilesStack 
        |> Seq.exists ( fun p -> 
            let a = p.fileInfo.FullName.ToLowerInvariant()
            let b = s.ToLowerInvariant()
            a=b )
   
    [<CLIEvent>]
    /// this even is raised from UI thread 
    /// used to update Menu
    member this.OnRecentFilesChanged = recentFilesChangedEv.Publish 