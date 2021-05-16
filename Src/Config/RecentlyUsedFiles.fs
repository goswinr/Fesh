namespace Seff.Config

open Seff
open Seff.Model
open System
open System.Text
open System.IO
open System.Collections.ObjectModel
open System.Globalization
open FSharp.Compiler.AbstractIL.Internal.Library

type UsedFile = {fileInfo:FileInfo ; lastOpendUtc:DateTime}
   
type RecentlyUsedFiles  (log:ISeffLog, hostInfo:Hosting) =
    
    let filePath0 = hostInfo.GetPathToSaveAppData("RecentlyUsedFiles.txt")
    
    let writer = SaveReadWriter(filePath0)        
        
    let recentFilesChangedEv = new Event<unit>()
        
    let recentFilesStack : Collections.Generic.Stack<UsedFile> = 
        // TODO this could be done async too?        
        
        let stack = Collections.Generic.Stack<UsedFile>()
        try            
            //if IO.File.Exists filePath then // do this check only when creating menu items
            for ln in writer.ReadAllLines() |> Seq.rev do
                let path , d = Util.String.splitOnce "|" ln                
                match DateTime.TryParseExact(d, "yyyy-MM-dd HH:mm", null,  DateTimeStyles.None) with // TODO is this UTC ?
                | true, date -> stack.Push( {fileInfo = FileInfo(path) ; lastOpendUtc = date}) |> ignore  
                | _ ->          stack.Push( {fileInfo = FileInfo(path) ; lastOpendUtc = DateTime.MinValue}) |> ignore 
                                  
        with 
            | :? IO.FileNotFoundException  -> log.PrintfnInfoMsg "No recently used files found. (This is expected on first use of the App)"  
            | e ->                            log.PrintfnAppErrorMsg  "Problem reading RecentlyUsedFiles settings file: %A"  e     
        stack   

        
    /// the maximum number of recxent files to be saved
    /// the amount of files in the recently used menu can be controlled separetly in menu.fs
    let maxCount = 100       
                

    let getStringRaiseEvent() = 
        let sb = StringBuilder()
        let Dup = Collections.Generic.HashSet()
        let k = ref 0
        for uf in recentFilesStack  do   // iteration starts at top element of stack            
            if !k < maxCount then 
                if not <| Dup.Contains uf.fileInfo.FullName then 
                    let date = uf.lastOpendUtc.ToString("yyyy-MM-dd HH:mm")
                    let file = uf.fileInfo.FullName
                    sb.AppendLine(file + "|" + date)  |> ignore
                    Dup.Add uf.fileInfo.FullName  |> ignore
                    incr k

        recentFilesChangedEv.Trigger()  //this event will be triggered 1000 ms after new tabs are created
        sb.ToString()    

    
    /// does not save 
    member this.Add(fi:FileInfo) =         
        if recentFilesStack.Count = 0  then 
            recentFilesStack.Push {fileInfo=fi ; lastOpendUtc=DateTime.UtcNow }
        else
            if recentFilesStack.Peek().fileInfo.FullName = fi.FullName then 
                recentFilesStack.Pop()  |> ignore // pop old date add new date
            recentFilesStack.Push {fileInfo=fi ; lastOpendUtc=DateTime.UtcNow }   
    
    member this.Save() =         
        writer.WriteIfLast( getStringRaiseEvent, 2000)

    
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
        
        xs |> Util.General.sortInPlaceBy ( fun uf -> uf.lastOpendUtc)
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