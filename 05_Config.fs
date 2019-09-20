namespace Seff

open System
open System.IO
open System.Windows
open System.Text
open Seff.Util
  
module Config = 
    

    // Yes, I rolled my own type of config file. The default App.config did not work for me when Editor is hosted in other CAD Apps like Rhinoceros3D

    type RunContext = Standalone | Hosted | Undefiend    
    let mutable currentRunContext = Undefiend
    let mutable hostName = "NotHosted" 
    // TODO refactor to use FileInfo instead of path strings
    let mutable fileDefaultCode = ""
    let mutable codeToAppendEvaluations = "" // \r\nRhino.RhinoDoc.ActiveDoc.Views.Redraw()  
    let mutable private fileSettings = ""
    let mutable private fileRecent =    ""   // files for list in open menu
    let mutable private fileOnClosingOpen = "" // files that are open when closing the editor window, for next restart
    let private sep = '=' // key value separatur
    let private path = IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Seff")
    
    //-----------------
    //--default code--
    //-----------------
    let private defaultBaseCode() = try IO.File.ReadAllText fileDefaultCode with _ -> ""
    let getDefaultCode() = // TODO expose this in UI as file
        [
        "// Script created on " + Time.todayStr
        //"// https://panesofglass.github.io/scripting-workshop/#/" 
        //"// http://brandewinder.com/2016/02/06/10-fsharp-scripting-tips/"        
        //"#load @\"" + General.installFolder() + "\\SeffLib.fsx\""
        //"open SeffLib"
        "open System"
        //"Environment.CurrentDirectory <- __SOURCE_DIRECTORY__"
        defaultBaseCode()
        ""
        ] 
        |> String.concat Environment.NewLine

    //-----------------
    //--UI Layout Settings--
    //-----------------
    let private Dict = new Collections.Concurrent.ConcurrentDictionary<string,string>()

    let removeSpecialChars (str:string) = // to get a Valid foldername fom any host app name suplied
        let sb = new Text.StringBuilder()
        for c in str do
            if (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c = '.' || c = '_' then  sb.Append(c) |> ignore
        sb.ToString()

    let setCurrentRunContext ctx logger= 
        currentRunContext <- ctx
        IO.Directory.CreateDirectory path |> ignore
        let host = removeSpecialChars hostName
        let ctxs = sprintf "%A" ctx
        
        fileSettings        <- IO.Path.Combine(path, sprintf "%s.%s.Settings.WindowLayout.txt"ctxs host )
        fileRecent          <- IO.Path.Combine(path, sprintf "%s.%s.RecentlyUsedFiles.txt"    ctxs host )
        fileOnClosingOpen   <- IO.Path.Combine(path, sprintf "%s.%s.CurrentlyOpenFiles.txt"   ctxs host )
        fileDefaultCode     <- IO.Path.Combine(path, sprintf "%s.%s.DefaultCode.fsx"          ctxs host )
        
        try            
            for ln in  IO.File.ReadAllLines fileSettings do
                match ln.Split(sep) with
                | [|k;v|] -> Dict.[k] <- v // TODO allow for comments? use ini format ??
                | _       -> ()
        with 
            | :? FileNotFoundException -> logger ("Settings file not found. (This is normal on first use of the App.)")
            | e -> logger ("Problem reading settings file: " + e.Message)
        
        //logger ("Settings loaded after " + Util.Timer3.toc)
       

    let setDelayed k v delay= 
        // delayed because the onMaximise of window event triggers first Loaction changed and then state changed, 
        // state change event should still be able to get previous size and loaction that is not savade yet
        async{  do! Async.Sleep(delay) 
                Dict.[k] <- v         
             } |> Async.Start
    
    let set k v = Dict.[k] <- v             
    
    let get k = 
        match Dict.TryGetValue k with 
        |true, v  -> Some v
        |false, _ -> None
    
    let private counter = ref 0L    
    let save logger =
        async{
            let k = Threading.Interlocked.Increment counter
            do! Async.Sleep(500) // delay to see if this is the last of many events (otherwise there is a noticable lag in dragging window around)
            if k > 2L && !counter = k then //do not save on startup && only save last event after a delay if there are many save events in a row ( eg from window size change)(ignore first two event from creating window)
                let sb = StringBuilder()
                for KeyValue(k,v) in Dict do
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
    

    let saveRecentFiles dlogger =         
        let sb = StringBuilder()
        recentFilesStack 
        |> Seq.map (fun fi -> fi.FullName)
        |> Seq.distinctBy (fun f -> f.ToLowerInvariant())
        |> Seq.truncate maxRecentFiles 
        |> Seq.rev 
        |> Seq.iter (sb.AppendLine >> ignore) // most recent file is a bottom of list
        FileWriter.Post(fileRecent, sb.ToString())
    
    
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

        with _ -> ()
    
    //--------------------------------------------
    //--remember open files on closing the editor--
    //--------------------------------------------

    let saveOpenFilesAndCurrentTab (currentFile:FileInfo option , files: seq<FileInfo option>) =         
        let curr = if currentFile.IsSome then currentFile.Value.FullName else ""
        FileWriterLines.Post( fileOnClosingOpen , [| yield curr; for f in files do if f.IsSome then yield f.Value.FullName |] )

    
    let loadOpenFilesOnLastAppClosing (createFun, dlogger) = 
        let mutable any = false
        async{
            if IO.File.Exists fileOnClosingOpen then 
                let lns = IO.File.ReadAllLines fileOnClosingOpen 
                if lns.Length > 1 then 
                    let currentFile = (lns |> Seq.head).ToLowerInvariant() // head is filepath and name for  current tab
                    for path in lns |> Seq.skip 1  do 
                        try 
                            let code = IO.File.ReadAllText path                    
                            do! Async.SwitchToContext Sync.syncContext
                            createFun (code, Some (FileInfo(path)), path.ToLowerInvariant() = currentFile )
                            any <- true
                            do! Async.SwitchToThreadPool()
                        with 
                            | :? FileNotFoundException -> () ///dlogger "Recent Files record not found. (This is normal on first use of the App.)"
                            | e -> dlogger e.Message
                    
            if not any then //create empty file if none was opened
                let def = getDefaultCode()
                do! Async.SwitchToContext Sync.syncContext
                createFun(def, None, true)                    
            } |> Async.Start // TODO make blocking so that user waits till all files are open ?


        

    