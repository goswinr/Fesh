namespace Seff.Config

open Seff
open System
open System.Text

 
/// window size, layout and position, async state and more
type Settings (log:ISeffLog, hostInfo:HostingInfo) = 
    let  sep = '=' // key value separatur like in ini files
    
    let filePath = hostInfo.GetPathToSaveAppData("Settings.txt")
    
    let settingsDict = 
        let dict = new Collections.Concurrent.ConcurrentDictionary<string,string>()   
        try            
            for ln in  IO.File.ReadAllLines filePath do
                match ln.Split(sep) with
                | [|k;v|] -> dict.[k] <- v // TODO allow for comments? use ini format ??
                | _       -> log.PrintAppErrorMsg "Bad line in settings file file: '%s'" ln
                //log.PrintDebugMsg "on File: %s" ln
        with 
            | :? IO.FileNotFoundException ->  log.PrintInfoMsg   "Settings file not found. (This is expected on first use of the App.)"
            | e ->                            log.PrintAppErrorMsg  "Problem reading or initalizing settings file: %A"  e
        dict

    let writer = SaveWriter(log)
    
    let settingsAsString () = 
        let sb = StringBuilder()
        for KeyValue(k,v) in settingsDict do
            sb.Append(k).Append(sep).AppendLine(v) |> ignore
        sb.ToString() 
    

    member this.SetDelayed k v delay= 
        // delayed because the onMaximise of window event triggers first Loaction changed and then state changed, 
        // state change event should still be able to Get previous size and loaction that is not saved yet
        async{  do! Async.Sleep(delay) 
                settingsDict.[k] <- v         
                } |> Async.Start
        
    member this.Set (k:string) (v:string) = 
        if k.IndexOf(sep) > -1 then log.PrintAppErrorMsg  "Settings key shall not contain '%c' : %s%c%s"  sep  k  sep  v            
        if v.IndexOf(sep) > -1 then log.PrintAppErrorMsg  "Settings value shall not contain '%c' : %s%c%s"  sep  k  sep  v 
        settingsDict.[k] <- v             
        
    member this.Get k = 
        match settingsDict.TryGetValue k with 
        |true, v  -> 
            //log.PrintDebugMsg "Get %s as %s" k v  //for DEBUG only
            Some v
        |false, _ -> 
            //log.PrintDebugMsg "missing key %s " k  //for DEBUG only
            None

    member this.Save () =                       
        writer.WriteDelayed (filePath, settingsAsString,  500)
        
    member this.SetFloat        key (v:float)       = this.Set key (string v)
    member this.SetFloatDelayed key (v:float) delay = this.SetDelayed key (string v) delay
    member this.SetInt          key (v:int)         = this.Set key (string v)
    member this.SetBool         key (v:bool)        = this.Set key (string v)
    member this.GetFloat        key def = match this.Get key with Some v -> float v           | None -> def
    member this.GetInt          key def = match this.Get key with Some v -> int v             | None -> def
    member this.GetBool         key def = match this.Get key with Some v -> Boolean.Parse v   | None -> def


    static member val keyFsiQuiet = "fsiOutputQuiet"