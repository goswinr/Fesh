namespace Seff

open System
open System.IO
open System.Windows
open System.Threading
open System.Text
open System.Collections.Generic
open Seff.Model

module Config = 


    let writeToFileAsyncLocked(path, readerWriterLock:ReaderWriterLockSlim, text) = 
        async{
            readerWriterLock.EnterWriteLock()
            try
                try
                    IO.File.WriteAllText(path,text)
                with ex ->            
                    Log.PrintIOErrorMsg "%s" ex.Message
            finally
                readerWriterLock.ExitWriteLock()
            } |> Async.Start

      
    /// getString will only be called and file will only be written if after the delay the counter value is the same as before. ( that means no more recent calls to this function have been made)
    let writeToFileDelayed (file, delay, counter :int64 ref ,readerWriterLock:ReaderWriterLockSlim, getString: unit->string) =
        async{
            let k = Threading.Interlocked.Increment counter
            do! Async.Sleep(delay) // delay to see if this is the last of many events (otherwise there is a noticable lag in dragging window around)
            if !counter = k then //k > 2L &&   //do not save on startup && only save last event after a delay if there are many save events in a row ( eg from window size change)(ignore first two event from creating window)
                try writeToFileAsyncLocked(file, readerWriterLock, getString() ) // getString might fail
                with ex -> Log.PrintAppErrorMsg "%s" ex.Message
            } |> Async.Start
        
    let private sep = '=' // key value separatur like in ini files

    
    /// window size, layout and position, async state and more
    type Settings private () = 
        static let settingsDict = new Collections.Concurrent.ConcurrentDictionary<string,string>()   
        static let counter = ref 0L // for atomic writing back to file
        static let readerWriterLock = new ReaderWriterLockSlim()
        static let settingsAsString () = 
            let sb = StringBuilder()
            for KeyValue(k,v) in settingsDict do
                sb.Append(k).Append(sep).AppendLine(v) |> ignore
            sb.ToString() 

        static member val FilePath = "Settings.Path not set " with get,set // set in Config.initialize()

        ///loads sync
        static member loadFromFile() = 
            try            
                for ln in  IO.File.ReadAllLines Settings.FilePath do
                    match ln.Split(sep) with
                    | [|k;v|] -> settingsDict.[k] <- v // TODO allow for comments? use ini format ??
                    | _       -> Log.PrintAppErrorMsg "Bad line in settings file file: '%s'" ln
                    //Log.PrintDebugMsg "on File: %s" ln
            with 
                | :? FileNotFoundException ->   Log.PrintInfoMsg   "Settings file not found. (This is normal on first use of the App.)"
                | e ->                          Log.PrintAppErrorMsg  "Problem reading or initalizing settings file: %s"  e.Message
                        
        
        static member setDelayed k v delay= 
            // delayed because the onMaximise of window event triggers first Loaction changed and then state changed, 
            // state change event should still be able to get previous size and loaction that is not saved yet
            async{  do! Async.Sleep(delay) 
                    settingsDict.[k] <- v         
                 } |> Async.Start
        
        static member set (k:string) (v:string) = 
            if k.IndexOf(sep) > -1 then Log.PrintAppErrorMsg  "Settings key shall not contain '%c' : %s%c%s"  sep  k  sep  v            
            if v.IndexOf(sep) > -1 then Log.PrintAppErrorMsg  "Settings value shall not contain '%c' : %s%c%s"  sep  k  sep  v 
            settingsDict.[k] <- v             
        
        static member get k = 
            match settingsDict.TryGetValue k with 
            |true, v  -> 
                //Log.PrintDebugMsg "get %s as %s" k v  //for DEBUG only
                Some v
            |false, _ -> 
                //Log.PrintDebugMsg "missing key %s " k  //for DEBUG only
                None

        static member Save () =                       
            writeToFileDelayed (Settings.FilePath, 500, counter,readerWriterLock, settingsAsString)
        
        static member setFloat        key (v:float)       = Settings.set key (string v)
        static member setFloatDelayed key (v:float) delay = Settings.setDelayed key (string v) delay
        static member setInt          key (v:int)         = Settings.set key (string v)
        static member setBool         key (v:bool)        = Settings.set key (string v)
        static member getFloat        key def = match Settings.get key with Some v -> float v           | None -> def
        static member getInt          key def = match Settings.get key with Some v -> int v             | None -> def
        static member getBool         key def = match Settings.get key with Some v -> Boolean.Parse v   | None -> def
    
    
    type DefaultCode private () =
        static let readerWriterLock = new ReaderWriterLockSlim()
        static member val FilePath = "DefaultCode.Path not set " with get,set        // set in Config.initialize()

        ///loads sync
        static member Get() =            
            try IO.File.ReadAllText DefaultCode.FilePath
            with _ -> 
                writeToFileAsyncLocked(DefaultCode.FilePath, readerWriterLock, defaultCodeOnFirstRun)// create file so it can be found and edited manually
                defaultCodeOnFirstRun
            
    
    /// files that are open when closing the editor window, for next restart
    type CurrentlyOpenFiles  private () = 
        static let readerWriterLock = new ReaderWriterLockSlim()
        static let counter = ref 0L // for atomic writing back to file
        static member val FilePath = "CurrentlyOpenFiles.Path not set " with get,set        // set in Config.initialize()

        static member Save (currentFile:FileInfo option , files: seq<FileInfo option>) =         
            let curr = if currentFile.IsSome then currentFile.Value.FullName else ""
            let sb = StringBuilder()
            sb.AppendLine(curr) |> ignore // first line is filepath and name for current tab (repeats below)
            for f in files do 
                if f.IsSome then sb.AppendLine(f.Value.FullName) |> ignore 
            writeToFileAsyncLocked(CurrentlyOpenFiles.FilePath, readerWriterLock, sb.ToString() )
            writeToFileDelayed    (CurrentlyOpenFiles.FilePath, 500, counter,readerWriterLock, sb.ToString )
        
        ///loads sync
        static member GetFromLastSession() = 
            let files = ResizeArray()
            let Done = HashSet()
            try            
                if IO.File.Exists CurrentlyOpenFiles.FilePath then 
                    let lns = IO.File.ReadAllLines CurrentlyOpenFiles.FilePath 
                    if lns.Length > 1 then 
                        let currentFile = (lns |> Seq.head).ToLowerInvariant() // first line is filepath and name for current tab (repeats below)
                        for path in lns |> Seq.skip 1  do 
                            let fi = FileInfo(path)                    
                            if fi.Exists then 
                                let lPath = path.ToLowerInvariant()
                                if not <| Done.Contains lPath then // savety check for duplicates
                                    Done.Add lPath  |> ignore
                                    let makeCurrent = lPath = currentFile 
                                    files.Add((fi,makeCurrent))            
            with e -> 
                Log.PrintAppErrorMsg "Error getFilesfileOnClosingOpen: %s"  e.Message
            files,Done
    
    type RecentlyUsedFiles private () =
        static let counter = ref 0L // for atomic writing back to file
        static let readerWriterLock = new ReaderWriterLockSlim()

        static let recentFilesStack = new Collections.Concurrent.ConcurrentStack<FileInfo>()// might contain even files that dont exist(on a currently detached drive)
                
        static let recentFilesChangedEv = new Event<unit>()
        
        /// the maximum number of recxent files to be saved
        /// the amount of files in the recently used manu can be controlled separetly
        static member val maxCount = 50 with get       

        static member val FilePath = "RecentlyUsedFiles.Path not set " with get,set // set in Config.initialize()

        static member Save(fi) =         
             recentFilesStack.Push fi             
             let getStringRaiseEvent() = 
                let sb = StringBuilder()
                for fi in recentFilesStack  do  sb.AppendLine(fi.FullName)  |> ignore // iteration starts at last element
                recentFilesChangedEv.Trigger()  //this event will be triggered 1000 ms after new tabs are created
                sb.ToString()                
             writeToFileDelayed(RecentlyUsedFiles.FilePath,1000,counter,readerWriterLock, getStringRaiseEvent)
        
        static member Items = Array.ofSeq recentFilesStack

        ///loads async, triggers Event
        static member loadFromFile(updateMenu:unit-> unit) =
            async{
                try            
                    if IO.File.Exists RecentlyUsedFiles.FilePath then 
                        for ln in  IO.File.ReadAllLines RecentlyUsedFiles.FilePath |> Seq.rev do
                            recentFilesStack.Push(FileInfo(ln)) |> ignore
                    updateMenu()
                with e -> 
                    Log.PrintAppErrorMsg "Error load RecentlyUsedFiles: %s"   e.Message
                } |> Async.Start 

        [<CLIEvent>]
        /// this even is not raised from UI thread 
        static member OnRecentFilesChanged = recentFilesChangedEv.Publish


    /// A static class to hold the statistic of most used toplevel auto completions
    type AutoCompleteStatistic private () =
    
        static let completionStats = Dictionary<string,float>() // TODO make concurrent ?
        static let counter = ref 0L // for atomic writing back to file
        static let readerWriterLock = new ReaderWriterLockSlim()

        static let completionStatsAsString () = 
            let sb = StringBuilder() 
            for KeyValue(k,v) in completionStats do
                sb.Append(k).Append(sep).AppendLine(v.ToString()) |> ignore
            sb.ToString() 

        static member val FilePath = "AutoCompleteStatistic.Path not set " with get,set // set in Config.initialize()

        ///loads async
        static member loadFromFile() =
            async{
                try            
                    if IO.File.Exists AutoCompleteStatistic.FilePath then 
                        for ln in  IO.File.ReadAllLines AutoCompleteStatistic.FilePath do
                        match ln.Split(sep) with
                        | [|k;v|] -> completionStats.[k] <- float v // TODO allow for comments? use ini format ??
                        | _       -> Log.PrintAppErrorMsg "Bad line in CompletionStats file : '%s'" ln                   
                with e -> 
                    Log.PrintAppErrorMsg "Error load fileCompletionStats: %s"   e.Message
                } |> Async.Start 
     
        static member Get(key) =
            match completionStats.TryGetValue key with
            |true,i -> i
            |_      -> 0.0
    
        /// increase by 1.0
        static member Incr(key) =
            match completionStats.TryGetValue key with
            |true,i -> completionStats.[key] <- i +  1.0
            |_      -> completionStats.[key] <- 1.0
    
        static member Save() =
            writeToFileDelayed (AutoCompleteStatistic.FilePath, 500, counter, readerWriterLock,completionStatsAsString)
        
    /// A static class to hold the previously loaded assemble refrences for auto completions
    type AssemblyReferenceStatistic private () =
            
        static let assRefStats = HashSet<string>() // TODO make concurrent ?        
        static let counter = ref 0L // for atomic writing back to file
        static let readerWriterLock = new ReaderWriterLockSlim()

        static let assRefStatsAsString () = 
            let sb = StringBuilder() 
            for v in assRefStats do sb.AppendLine(v.ToString()) |> ignore
            sb.ToString() 

        static member val FilePath = "AssemblyReferenceStatistic.Path not set " with get,set // set in Config.initialize()

        ///loads async
        static member loadFromFile() =
            async{
                try            
                    if IO.File.Exists AssemblyReferenceStatistic.FilePath then 
                        for ln in  IO.File.ReadAllLines AssemblyReferenceStatistic.FilePath do
                            //TODO verify path exists
                            assRefStats.Add(ln) |> ignore
                with e -> 
                    Log.PrintAppErrorMsg "Error load assRefStatsStats: %s"   e.Message
                } |> Async.Start 
             
        static member Items = assRefStats
            
        static member Save() =
            writeToFileDelayed (AssemblyReferenceStatistic.FilePath, 500, counter, readerWriterLock,assRefStatsAsString)
        
        static member RecordFromLog =
            fun (s:string) -> 
                if s.Contains "--> Referenced '" then // e.g.: --> Referenced 'C:\Program Files\Rhino 6\System\RhinoCommon.dll' (file may be locked by F# Interactive process)
                    let start = s.IndexOf(''') 
                    if start > -1 then 
                        let ende = s.IndexOf(''', start + 2)
                        if ende > start + 3 then
                            let r = s.Substring(start + 1, ende - 1)
                            assRefStats.Add (r)  |> ignore 
   
    /// A static class to hold the current App Run context (Standalone or Hosted)
    type Context private () =
        static let mutable currentRunContext = Standalone
        static member Mode     = currentRunContext 
        static member IsHosted     = currentRunContext <> Standalone        
        static member IsStandalone = currentRunContext = Standalone  
        static member internal Set (v:AppRunContext) = currentRunContext <- v
        static member asStringForFilename() = 
            match currentRunContext with 
            |Standalone ->  "Standalone" 
            |Hosted name ->  
                  let sb = new Text.StringBuilder()/// to get a valid filename fom any host app name suplied
                  for c in name do
                      if (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c = '.' || c = '_'|| c = ' ' || c = '-'|| c = '+' then  sb.Append(c) |> ignore
                  "Hosted." + sb.ToString()


    let Initialize(context:AppRunContext) =
        Context.Set (context)
        try
            let configFilesFolder = IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Seff")
            IO.Directory.CreateDirectory(configFilesFolder) |> ignore

            let host = Context.asStringForFilename()

            Settings.FilePath                    <- IO.Path.Combine(configFilesFolder, sprintf "%s.Settings.txt"                   host )
            RecentlyUsedFiles.FilePath           <- IO.Path.Combine(configFilesFolder, sprintf "%s.RecentlyUsedFiles.txt"          host )
            CurrentlyOpenFiles.FilePath          <- IO.Path.Combine(configFilesFolder, sprintf "%s.CurrentlyOpenFiles.txt"         host )
            DefaultCode.FilePath                 <- IO.Path.Combine(configFilesFolder, sprintf "%s.DefaultCode.fsx"                host )
            AutoCompleteStatistic.FilePath       <- IO.Path.Combine(configFilesFolder, sprintf "%s.AutoCompleteStatistic.txt"      host ) 
            AssemblyReferenceStatistic.FilePath  <- IO.Path.Combine(configFilesFolder, sprintf "%s.AssemblyReferenceStatistic.txt" host ) 

            Settings.loadFromFile()
            AutoCompleteStatistic.loadFromFile()
            AssemblyReferenceStatistic.loadFromFile()
            //RecentlyUsedFiles.loadFromFile() // do in Menu.initialize
            
            //Log:
            Log.OnPrint.Add (AssemblyReferenceStatistic.RecordFromLog) // TODO does this have print perfomance impact ? measure do async ?
            Log.ReadOnlyEditor.FontFamily       <- Appearance.font
            Log.ReadOnlyEditor.FontSize         <- Settings.getFloat "FontSize" Appearance.fontSize                
            Log.setWordWrap( Settings.getBool "logHasLineWrap" true )
               
        with ex ->
            Log.PrintAppErrorMsg "Error in Congig.Initialize(%A): %A" context ex


    /// opens up Explorer
    let openConfigFolder()=
        let path = IO.Path.GetDirectoryName Settings.FilePath
        IO.Directory.CreateDirectory path |> ignore        
        Diagnostics.Process.Start("explorer.exe", "\""+path+"\"")        |> ignore

