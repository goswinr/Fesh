namespace Seff

open System
open System.IO
open System.Windows
open System.Threading
open System.Text
open Seff.Util
open Seff.Model
open Seff.Logging
open System.Collections.Generic

module Config = 

    let writeToFileAsyncLocked(path, readerWriterLock:ReaderWriterLockSlim, text) = 
        async{
            readerWriterLock.EnterWriteLock()
            try
                try
                    IO.File.WriteAllText(path,text)
                with ex ->            
                    Log.printIOErrorMsg "%s" ex.Message
            finally
                readerWriterLock.ExitWriteLock()
            } |> Async.Start

      
    /// getString will only be called and file will only be written if after the delay the counter value is the same as before. (no more recent calls to this have been made)
    let writeToFileDelayed (file, delay, counter :int64 ref ,readerWriterLock:ReaderWriterLockSlim, getString: unit->string) =
        async{
            let k = Threading.Interlocked.Increment counter
            do! Async.Sleep(delay) // delay to see if this is the last of many events (otherwise there is a noticable lag in dragging window around)
            if !counter = k then //k > 2L &&   //do not save on startup && only save last event after a delay if there are many save events in a row ( eg from window size change)(ignore first two event from creating window)
                try writeToFileAsyncLocked(file, readerWriterLock, getString() ) // getString might fail
                with ex -> Log.printAppErrorMsg "%s" ex.Message
            } |> Async.Start
        
    
    /// to get a valid filename fom any host app name suplied
    let removeSpecialChars (str:string) = 
          let sb = new Text.StringBuilder()
          for c in str do
              if (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c = '.' || c = '_'|| c = ' ' || c = '-'|| c = '+' then  sb.Append(c) |> ignore
          sb.ToString()
    
    /// window size, layout and position, async state and more
    type Settings private () = 
        static let settingsDict = new Collections.Concurrent.ConcurrentDictionary<string,string>()   
        static let sep = '=' // key value separatur like in ini files
        static let counter = ref 0L // for atomic writing back to file
        static let readerWriterLock = new ReaderWriterLockSlim()
        static let settingsAsString () = 
            let sb = StringBuilder()
            for KeyValue(k,v) in settingsDict do
                sb.Append(k).Append(sep).AppendLine(v) |> ignore
            sb.ToString() 

        static member val FilePath = "xyz" with get,set

        static member loadFromFile() = 
            try            
                for ln in  IO.File.ReadAllLines Settings.FilePath do
                    match ln.Split(sep) with
                    | [|k;v|] -> settingsDict.[k] <- v // TODO allow for comments? use ini format ??
                    | _       -> Log.printAppErrorMsg "Bad line in settings file file: '%s'" ln
                    //Log.printDebugMsg "on File: %s" ln
            with 
                | :? FileNotFoundException ->   Log.printInfoMsg   "Settings file not found. (This is normal on first use of the App.)"
                | e ->                          Log.printAppErrorMsg  "Problem reading or initalizing settings file: %s"  e.Message
                        
        
        static member setDelayed k v delay= 
            // delayed because the onMaximise of window event triggers first Loaction changed and then state changed, 
            // state change event should still be able to get previous size and loaction that is not saved yet
            async{  do! Async.Sleep(delay) 
                    settingsDict.[k] <- v         
                 } |> Async.Start
        
        static member set (k:string) (v:string) = 
            if k.IndexOf(sep) > -1 then Log.printAppErrorMsg  "Settings key shall not contain '%c' : %s%c%s"  sep  k  sep  v            
            if v.IndexOf(sep) > -1 then Log.printAppErrorMsg  "Settings value shall not contain '%c' : %s%c%s"  sep  k  sep  v 
            settingsDict.[k] <- v             
        
        static member get k = 
            match settingsDict.TryGetValue k with 
            |true, v  -> 
                //Log.printDebugMsg "get %s as %s" k v
                Some v
            |false, _ -> 
                //Log.printDebugMsg "missing key %s " //TODO printing this will crash Rhino
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
        static member val FilePath = "xyz" with get,set        

        static member Get() =            
            try IO.File.ReadAllText DefaultCode.FilePath
            with _ -> 
                writeToFileAsyncLocked(DefaultCode.FilePath, readerWriterLock, defaultCodeOnFirstRun)// create file so it can be found and edited manually
                defaultCodeOnFirstRun
            
    
    /// files that are open when closing the editor window, for next restart
    type CurrentlyOpenFiles  private () = 
        static let readerWriterLock = new ReaderWriterLockSlim()
        static member val FilePath = "xyz" with get,set        
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
                Log.printAppErrorMsg "Error getFilesfileOnClosingOpen: %s"  e.Message
            files
    
    type RecentlyUsedFiles private () =
        static let counter = ref 0L // for atomic writing back to file
        static let readerWriterLock = new ReaderWriterLockSlim()

        static let recentFilesStack = new Collections.Concurrent.ConcurrentStack<FileInfo>()// might contain even files that dont exist(on a currently detached drive)
        static let recentFilesReOpened = new ResizeArray<FileInfo>() // to put them first in the menue
        
        static member val maxCount = 30 with get       
        static member val FilePath = "xyz" with get,set

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
                 Log.printAppErrorMsg "Error Loading recently used files: %s"   e.Message
    
    /// statistic of most used toplevel auto completions
    type AutoCompleteStatistic private () =
        
        static let completionStats = Dictionary<string,float>() // make concurrent ?
        static let sep = '=' // key value separatur like in ini files
        static let counter = ref 0L // for atomic writing back to file
        static let readerWriterLock = new ReaderWriterLockSlim()

        static let completionStatsAsString () = 
            let sb = StringBuilder() 
            for KeyValue(k,v) in completionStats do
                sb.Append(k).Append(sep).AppendLine(v.ToString()) |> ignore
            sb.ToString() 

        static member val FilePath = "xyz" with get,set

        static member loadFromFile() =
            async{
                try            
                    if IO.File.Exists AutoCompleteStatistic.FilePath then 
                        for ln in  IO.File.ReadAllLines AutoCompleteStatistic.FilePath do
                        match ln.Split(sep) with
                        | [|k;v|] -> completionStats.[k] <- float v // TODO allow for comments? use ini format ??
                        | _       -> Log.printAppErrorMsg "Bad line in CompletionStats file : '%s'" ln                   
                with e -> 
                    Log.printAppErrorMsg "Error load fileCompletionStats: %s"   e.Message
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
            
    /// opens up Explorer
    let openConfigFolder()=
        let path = IO.Path.GetDirectoryName Settings.FilePath
        IO.Directory.CreateDirectory path |> ignore        
        Diagnostics.Process.Start("explorer.exe", "\""+path+"\"")        |> ignore

    let mutable currentRunContext = Standalone

    let initialize(context:RunContext) =
        currentRunContext <- context
        let configFilesFolder = IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Seff")
        IO.Directory.CreateDirectory(configFilesFolder) |> ignore

        let host = match context with Standalone ->  "Standalone" | Hosted x -> "Hosted." + removeSpecialChars(x)

        Settings.FilePath               <- IO.Path.Combine(configFilesFolder, sprintf "%s.Settings.txt" host )
        RecentlyUsedFiles.FilePath      <- IO.Path.Combine(configFilesFolder, sprintf "%s.RecentlyUsedFiles.txt"     host )
        CurrentlyOpenFiles.FilePath     <- IO.Path.Combine(configFilesFolder, sprintf "%s.CurrentlyOpenFiles.txt"    host )
        DefaultCode.FilePath            <- IO.Path.Combine(configFilesFolder, sprintf "%s.DefaultCode.fsx"           host )
        AutoCompleteStatistic.FilePath  <- IO.Path.Combine(configFilesFolder, sprintf "%s.AutoCompleteStatistic.txt" host ) 
        
        Settings.loadFromFile()
        AutoCompleteStatistic.loadFromFile()

/// persitance of user settings such as recent files and window location and size  
module ConfigOLDUNUSED = 
    

    let mutable internal logger : string->unit = fun _ -> () // will be set once UI.Log is created

    // Yes, I rolled my own type of config file. The default App.config did not work for me when Editor is hosted in other CAD Apps like Rhinoceros3D

    type RunContext = Standalone | Hosted | Undefiend    
    let mutable currentRunContext = Undefiend
    let mutable hostName = "NotHosted" 

    // TODO refactor to use FileInfo instead of path strings
    let mutable         fileDefaultCode = ""     
    let mutable private fileSettings = ""
    let mutable private fileRecent =    ""       // files for list in open menu
    let mutable private fileOnClosingOpen = ""   // files that are open when closing the editor window, for next restart
    let mutable private fileCompletionStats = "" // statistic of most used toplevel auto completions
    let private sep = '=' // key value separatur like in ini files

    let configFilesPath = 
        IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Seff")
        

    let openConfigFolder()=
        IO.Directory.CreateDirectory configFilesPath |> ignore        
        Diagnostics.Process.Start("explorer.exe", configFilesPath)        |> ignore

    //-----------------
    //--default code--
    //-----------------
    let private defaultBaseCode() = 
        try IO.File.ReadAllText fileDefaultCode 
        with _ -> 
            File.WriteAllText(fileDefaultCode,"")// create file so it can be found to be edited manually
            ""
    let getDefaultCode() = 
        [
        "// Script created on " + Time.todayStr
        //"// https://panesofglass.github.io/scripting-workshop/#/" 
        //"// http://brandewinder.com/2016/02/06/10-fsharp-scripting-tips/"        
        //"#load @\"" + General.installFolder() + "\\SeffLib.fsx\""
        //"open System"
        //"Environment.CurrentDirectory <- __SOURCE_DIRECTORY__"
        defaultBaseCode()
        ""
        ] 
        |> String.concat Environment.NewLine

    //-----------------
    //--UI Layout Settings--
    //-----------------
    let private settingsDict = new Collections.Concurrent.ConcurrentDictionary<string,string>()

    /// to get a Valid foldername fom any host app name suplied
    let removeSpecialChars (str:string) = 
        let sb = new Text.StringBuilder()
        for c in str do
            if (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c = '.' || c = '_' then  sb.Append(c) |> ignore
        sb.ToString()

    let setCurrentRunContext ctx = 
        currentRunContext <- ctx
        IO.Directory.CreateDirectory configFilesPath |> ignore
        let host = removeSpecialChars hostName
        let ctxs = sprintf "%A" ctx
        
        fileSettings        <- IO.Path.Combine(configFilesPath, sprintf "%s.%s.Settings.WindowLayout.txt"ctxs host )
        fileRecent          <- IO.Path.Combine(configFilesPath, sprintf "%s.%s.RecentlyUsedFiles.txt"    ctxs host )
        fileOnClosingOpen   <- IO.Path.Combine(configFilesPath, sprintf "%s.%s.CurrentlyOpenFiles.txt"   ctxs host )
        fileDefaultCode     <- IO.Path.Combine(configFilesPath, sprintf "%s.%s.DefaultCode.fsx"          ctxs host )
        fileCompletionStats <- IO.Path.Combine(configFilesPath, sprintf "%s.%s.CompletionStats.txt"      ctxs host ) 
        try            
            for ln in  IO.File.ReadAllLines fileSettings do
                match ln.Split(sep) with
                | [|k;v|] -> settingsDict.[k] <- v // TODO allow for comments? use ini format ??
                | _       ->  logger ("Bad line in settings file file: '" + ln + "'")
        with 
            | :? FileNotFoundException ->   logger ("Settings file not found. (This is normal on first use of the App.)")
            | e ->                          logger ("Problem reading settings file: " + e.Message)
        
        //logger ("Settings loaded after " + Util.Timer3.toc)
       

    let setDelayed k v delay= 
        // delayed because the onMaximise of window event triggers first Loaction changed and then state changed, 
        // state change event should still be able to get previous size and loaction that is not saved yet
        async{  do! Async.Sleep(delay) 
                settingsDict.[k] <- v         
             } |> Async.Start
    
    let set k v = settingsDict.[k] <- v             
    
    let get k = 
        match settingsDict.TryGetValue k with 
        |true, v  -> Some v
        |false, _ -> None
    
    let private counter = ref 0L    
    let saveSettings () =
        async{
            let k = Threading.Interlocked.Increment counter
            do! Async.Sleep(500) // delay to see if this is the last of many events (otherwise there is a noticable lag in dragging window around)
            if k > 2L && !counter = k then //do not save on startup && only save last event after a delay if there are many save events in a row ( eg from window size change)(ignore first two event from creating window)
                let sb = StringBuilder()
                for KeyValue(k,v) in settingsDict do
                    sb.Append(k)
                      .Append(sep)
                      .AppendLine(v) |> ignore
                try 
                    IO.File.WriteAllText(fileSettings, sb.ToString())
                    //logger "Layout settings saved"
                with e -> 
                    logger (e.Message + Environment.NewLine + e.StackTrace)
            } |> Async.Start        
    
    let setFloat        key (v:float)       = set key (string v)
    let setFloatDelayed key (v:float) delay = setDelayed key (string v) delay
    let setInt          key (v:int)         = set key (string v)
    let setBool         key (v:bool)        = set key (string v)
    let getFloat        key def = match get key with Some v -> float v           | None -> def
    let getInt          key def = match get key with Some v -> int v             | None -> def
    let getBool         key def = match get key with Some v -> Boolean.Parse v   | None -> def



    //----------------------------
    //--recently used files menu--
    //----------------------------
    let maxRecentFiles = 30 // TODO expose this in UI
    let recentFilesStack = new Collections.Concurrent.ConcurrentStack<FileInfo>()// might contain even files that dont exist(on a currently detached drive)
    let recentFilesReOpened = new ResizeArray<FileInfo>() // to put them first in the menue
    

    let saveRecentFiles () =         
        let sb = StringBuilder()
        recentFilesStack 
        |> Seq.map (fun fi -> fi.FullName)
        |> Seq.distinctBy (fun f -> f.ToLowerInvariant())
        |> Seq.truncate maxRecentFiles 
        |> Seq.rev 
        |> Seq.iter (sb.AppendLine >> ignore) // most recent file is a bottom of list
        fileWriter.Post(fileRecent, sb.ToString())
    
    
    let loadRecentFilesMenu updateRecentMenu =
        try
            IO.File.ReadAllLines fileRecent
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
            logger ("Error Loading recently used files:" +  e.Message)

    
    //--------------------------------------------
    //--remember open files on closing the editor--
    //--------------------------------------------

    let saveOpenFilesAndCurrentTab (currentFile:FileInfo option , files: seq<FileInfo option>) =         
        let curr = if currentFile.IsSome then currentFile.Value.FullName else ""
        fileWriterLines.Post( fileOnClosingOpen , [| yield curr; for f in files do if f.IsSome then yield f.Value.FullName |] )

    
    let getFilesfileOnClosingOpen() = 
        let files=ResizeArray()
        try            
            if IO.File.Exists fileOnClosingOpen then 
                let lns = IO.File.ReadAllLines fileOnClosingOpen 
                if lns.Length > 1 then 
                    let currentFile = (lns |> Seq.head).ToLowerInvariant() // head is filepath and name for  current tab
                    for path in lns |> Seq.skip 1  do 
                        let fi = FileInfo(path)                    
                        if fi.Exists then 
                            let code = IO.File.ReadAllText path
                            let makeCurrent = path.ToLowerInvariant() = currentFile 
                            files.Add((fi,makeCurrent,code))            
        with e -> 
            logger ("Error getFilesfileOnClosingOpen:" +  e.Message)
        files


    //--------------------------------------------
    //--Auto completion statistic--
    //--------------------------------------------        

    let CompletionStats = Dictionary<string,float>() // make concurrent ?

    let loadCompletionStats() =
        async{
            try            
                if IO.File.Exists fileCompletionStats then 
                    for ln in  IO.File.ReadAllLines fileCompletionStats do
                    match ln.Split(sep) with
                    | [|k;v|] -> CompletionStats.[k] <- float v // TODO allow for comments? use ini format ??
                    | _       -> logger ("Bad line in CompletionStats file : '" + ln + "'")                       
            with e -> 
                logger ("Error load fileCompletionStats:" +  e.Message)
            } |> Async.Start 
     
    let getCompletionStats(key) =
        match CompletionStats.TryGetValue key with
        |true,i -> i
        |_      -> 0.0
    
    let incrCompletionStats(key) =
        match CompletionStats.TryGetValue key with
        |true,i -> CompletionStats.[key] <- i +  1.0
        |_      -> CompletionStats.[key] <- 1.0
    
    let saveCompletionStats() =
        async{
            let k = Threading.Interlocked.Increment counter
            do! Async.Sleep(1000) // delay to see if this is the last of many events (otherwise there is a noticable lag in dragging window around)
            if k > 2L && !counter = k then //do not save on startup && only save last event after a delay if there are many save events in a row ( eg from window size change)(ignore first two event from creating window)
                let sb = StringBuilder()
                for KeyValue(k,v) in CompletionStats do
                    sb.Append(k)
                        .Append(sep)
                        .AppendLine(v.ToString()) |> ignore
                try 
                    IO.File.WriteAllText(fileCompletionStats, sb.ToString())
                    //logger "fileCompletionStats settings saved"
                with e -> 
                    logger ("Error saving  fileCompletionStats:" + e.Message + Environment.NewLine + e.StackTrace)
            } |> Async.Start 