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

        static member val FilePath = "xyz" with get,set // set in Config.initialize()

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
        static member val FilePath = "xyz" with get,set        // set in Config.initialize()

        static member Get() =            
            try IO.File.ReadAllText DefaultCode.FilePath
            with _ -> 
                writeToFileAsyncLocked(DefaultCode.FilePath, readerWriterLock, defaultCodeOnFirstRun)// create file so it can be found and edited manually
                defaultCodeOnFirstRun
            
    
    /// files that are open when closing the editor window, for next restart
    type CurrentlyOpenFiles  private () = 
        static let readerWriterLock = new ReaderWriterLockSlim()

        static member val FilePath = "xyz" with get,set        // set in Config.initialize()

        static member Save (currentFile:FileInfo option , files: seq<FileInfo option>) =         
            let curr = if currentFile.IsSome then currentFile.Value.FullName else ""
            let sb = StringBuilder()
            sb.AppendLine(curr) |> ignore // first line is filepath and name for current tab (repeats below)
            for f in files do 
                if f.IsSome then sb.AppendLine(f.Value.FullName) |> ignore 
            writeToFileAsyncLocked(CurrentlyOpenFiles.FilePath, readerWriterLock, sb.ToString() )

        
        static member GetFromLastSession() = 
            let files = ResizeArray()
            try            
                if IO.File.Exists CurrentlyOpenFiles.FilePath then 
                    let lns = IO.File.ReadAllLines CurrentlyOpenFiles.FilePath 
                    if lns.Length > 1 then 
                        let currentFile = (lns |> Seq.head).ToLowerInvariant() // first line is filepath and name for current tab (repeats below)
                        for path in lns |> Seq.skip 1  do 
                            let fi = FileInfo(path)                    
                            if fi.Exists then 
                                let code = IO.File.ReadAllText path
                                let makeCurrent = path.ToLowerInvariant() = currentFile 
                                files.Add((fi,makeCurrent,code))            
            with e -> 
                Log.PrintAppErrorMsg "Error getFilesfileOnClosingOpen: %s"  e.Message
            files
    
    type RecentlyUsedFiles private () =
        static let counter = ref 0L // for atomic writing back to file
        static let readerWriterLock = new ReaderWriterLockSlim()

        static let recentFilesStack = new Collections.Concurrent.ConcurrentStack<FileInfo>()// might contain even files that dont exist(on a currently detached drive)

        static let recentFilesReOpened = new ResizeArray<FileInfo>() // to put them first in the menue
        
        /// the maximum number of recxent files to be saved
        static member val maxCount = 30 with get       

        static member val FilePath = "xyz" with get,set // set in Config.initialize()

        static member Save() =         
             let sb = StringBuilder()
             recentFilesStack 
             |> Seq.map (fun fi -> fi.FullName)
             |> Seq.distinctBy (fun f -> f.ToLowerInvariant())
             |> Seq.truncate RecentlyUsedFiles.maxCount 
             |> Seq.rev 
             |> Seq.iter (sb.AppendLine >> ignore) // most recent file is a bottom of list
             writeToFileDelayed(RecentlyUsedFiles.FilePath,1000,counter,readerWriterLock, sb.ToString)
                      
        static member Add (fi) = recentFilesStack.Push fi

        static member loadRecentFilesMenu updateRecentMenu =
             try
                 IO.File.ReadAllLines RecentlyUsedFiles.FilePath
                 |> Seq.iter (
                     fun f -> 
                         let fl = f.ToLowerInvariant()
                         match recentFilesReOpened |> Seq.tryFind (fun fi -> fi.FullName.ToLowerInvariant() = fl ) with 
                         |Some _ -> ()
                         |None ->
                             let fi = new FileInfo(f)
                             recentFilesStack.Push fi
                             updateRecentMenu fi
                         )
                 for fi in recentFilesReOpened |> Seq.rev do // they are already distinct
                     recentFilesStack.Push fi
                     updateRecentMenu fi
             with e -> 
                 Log.PrintAppErrorMsg "Error Loading recently used files: %s"   e.Message
    
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

        static member val FilePath = "xyz" with get,set // set in Config.initialize()

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
            
        static let assRefStats = Dictionary<string,float>() // TODO make concurrent ?        
        static let counter = ref 0L // for atomic writing back to file
        static let readerWriterLock = new ReaderWriterLockSlim()

        static let assRefStatsAsString () = 
            let sb = StringBuilder() 
            for KeyValue(k,v) in assRefStats do
                sb.Append(k).Append(sep).AppendLine(v.ToString()) |> ignore
            sb.ToString() 

        static member val FilePath = "xyz" with get,set // set in Config.initialize()

        static member loadFromFile() =
            async{
                try            
                    if IO.File.Exists AssemblyReferenceStatistic.FilePath then 
                        for ln in  IO.File.ReadAllLines AssemblyReferenceStatistic.FilePath do
                        match ln.Split(sep) with
                        | [|k;v|] -> assRefStats.[k] <- float v // TODO allow for comments? use ini format ??
                        | _       -> Log.PrintAppErrorMsg "Bad line in AssemblyReferenceStatistic file : '%s'" ln                   
                with e -> 
                    Log.PrintAppErrorMsg "Error load assRefStatsStats: %s"   e.Message
                } |> Async.Start 
             
        static member Get(key) =
            match assRefStats.TryGetValue key with
            |true,i -> i
            |_      -> 0.0
            
        /// increase by 1.0
        static member Incr(key) =
            match assRefStats.TryGetValue key with
            |true,i -> assRefStats.[key] <- i +  1.0
            |_      -> assRefStats.[key] <- 1.0
            
        static member Save() =
            writeToFileDelayed (AssemblyReferenceStatistic.FilePath, 500, counter, readerWriterLock,assRefStatsAsString)
    
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


    let initialize(context:AppRunContext) =
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
        with ex ->
            Log.PrintAppErrorMsg "Error in Congig.initialize(%A): %A" context ex


    /// opens up Explorer
    let openConfigFolder()=
        let path = IO.Path.GetDirectoryName Settings.FilePath
        IO.Directory.CreateDirectory path |> ignore        
        Diagnostics.Process.Start("explorer.exe", "\""+path+"\"")        |> ignore

