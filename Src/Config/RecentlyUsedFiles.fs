namespace Fesh.Config

open System
open System.Text
open System.IO
open System.Globalization

open Fittings

open Fesh
open Fesh.Util
open Fesh.Model


type UsedFile = {
    fileInfo:FileInfo
    lastOpenedUTC:DateTime
    }

type RecentlyUsedFiles  ( runContext:RunContext) =

    let filePath0 = runContext.GetPathToSaveAppData("Recently-Used-Files.txt")

    let writer = SaveReadWriter(filePath0,IFeshLog.printError)

    let recentFilesChangedEv = new Event<unit>()

    /// This stack is filled async on startup, changed from the UI thread and read from other threads for the menu.
    /// So always lock on it when accessing it.
    let recentFilesStack =
        let stack = Collections.Generic.Stack<UsedFile>()
        let push (uf:UsedFile) = lock stack (fun () -> stack.Push uf)
        async{
            writer.CreateFileIfMissing("")  |> ignore  //IFeshLog.log.PrintfnInfoMsg "No recently used files found. (This is expected on first use of the App)"
            match writer.ReadAllLines() with
            |None -> ()
            |Some files ->
                for ln in files |> Seq.rev do
                    let path , d = Str.splitOnce "|" ln
                    match DateTime.TryParseExact(d, "yyyy-MM-dd HH:mm", null,  DateTimeStyles.None) with // TODO is this UTC ?
                    | true, date ->
                        if IO.File.Exists(path) then
                            push {fileInfo = FileInfo(path) ; lastOpenedUTC = date}
                        elif DateTime.UtcNow - date < TimeSpan.FromDays(2.) then // if a file is missing only add it to the recent file stack if it was used in the last 2 days( might be on a network drive that is temporarily disconnected)
                            push {fileInfo = FileInfo(path) ; lastOpenedUTC = date}
                    | _ ->
                        IFeshLog.log.PrintfnAppErrorMsg "Failed to parse date from recent file text: %s" ln
                        push {fileInfo = FileInfo(path) ; lastOpenedUTC = DateTime.MinValue}
            recentFilesChangedEv.Trigger()// to update menu if delegate is already set up in menu.fs
            } |> Async.Start
        stack  // the returned stack is empty initially , it will be filled async

    /// A copy of the stack, the first element is the top of the stack.
    let snapshot() : UsedFile[] =
        lock recentFilesStack (fun () -> recentFilesStack.ToArray())


    /// the maximum number of recent files to be saved
    /// the amount of files in the recently used menu can be controlled separately in menu.fs
    let maxCount = 100

    let getStringRaiseEvent() =
        let sb = StringBuilder()
        let Dup = Collections.Generic.HashSet()
        let k = ref 0
        for uf in snapshot()  do   // iteration starts at top element of stack
            if !k < maxCount then
                if not <| Dup.Contains uf.fileInfo.FullName then
                    let date = uf.lastOpenedUTC.ToString("yyyy-MM-dd HH:mm")
                    let file = uf.fileInfo.FullName
                    sb.AppendLine(file + "|" + date)  |> ignore
                    Dup.Add uf.fileInfo.FullName  |> ignore
                    incr k

        recentFilesChangedEv.Trigger()  //this event will be triggered 2000 ms after new tabs are created, because of writer.WriteIfLast
        sb.ToString()

    /// does not save
    member this.Add(fi:FileInfo) =
        lock recentFilesStack (fun () ->
            if recentFilesStack.Count = 0  then
                recentFilesStack.Push {fileInfo=fi ; lastOpenedUTC=DateTime.UtcNow }
            else
                if recentFilesStack.Peek().fileInfo.FullName = fi.FullName then
                    recentFilesStack.Pop()  |> ignore// pop old date add new date
                recentFilesStack.Push {fileInfo=fi ; lastOpenedUTC=DateTime.UtcNow }
            )
    /// saves async with 2 sec delay
    member this.Save() =
        writer.WriteIfLast( getStringRaiseEvent, 2000)

    /// saves async with 2 sec delay
    member this.AddAndSave(fi:FileInfo) =
        this.Add(fi)
        this.Save()

    /// saves immediately in sync without delay
    member this.AddAndSaveSync(fi:FileInfo) =
        this.Add(fi)
        IO.File.WriteAllText(filePath0,getStringRaiseEvent(),Text.Encoding.UTF8)

    /// the first element in this array the top of stack
    member this.GetUniqueExistingSorted() =
        let xs = ResizeArray()
        let Dup = Collections.Generic.HashSet()
        for uf in snapshot() do
            let lc = uf.fileInfo.FullName.ToLowerInvariant()
            if not (Dup.Contains lc) then
                Dup.Add lc |> ignore
                if File.Exists lc then // async is done in Menu.setRecentFiles()
                    xs.Add uf
        xs |> Util.General.sortInPlaceBy ( fun uf -> uf.lastOpenedUTC)
        xs.Reverse()
        xs

    member this.MostRecentPath : option<DirectoryInfo> =
        lock recentFilesStack (fun () ->
            if recentFilesStack.Count = 0 then None
            else Some <| recentFilesStack.Peek().fileInfo.Directory
            )



    member this.Contains(s:string) =
        snapshot()
        |> Seq.exists ( fun p ->
            let a = p.fileInfo.FullName.ToLowerInvariant()
            let b = s.ToLowerInvariant()
            a=b )

    /// this even is raised from UI thread
    /// used to update Menu
    [<CLIEvent>]
    member this.OnRecentFilesChanged = recentFilesChangedEv.Publish
