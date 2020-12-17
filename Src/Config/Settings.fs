namespace Seff.Config

open Seff
open System
open System.Text
open Seff.Model
 
/// window size, layout and position, async state and more
type Settings (log:ISeffLog, hostInfo:Hosting) = 
    
    let mutable selectAllOccurences = true // explict value for often accessd settings , skips parsing and dict
    
        
    let  sep = '=' // key value separatur like in ini files
    
    let filePath = hostInfo.GetPathToSaveAppData("Settings.txt")
    
    let settingsDict = 
        let dict = new Collections.Concurrent.ConcurrentDictionary<string,string>()   
        try            
            for ln in  IO.File.ReadAllLines filePath do
                match ln.Split(sep) with
                | [|k;v|] -> dict.[k] <- v // TODO allow for comments? use ini format ??
                | _       -> log.PrintfnAppErrorMsg "Bad line in settings file file: '%s'" ln
                //log.PrintfnDebugMsg "on File: %s" ln
        with 
            | :? IO.FileNotFoundException ->  log.PrintfnInfoMsg   "Settings file not found. (This is expected on first use of the App.)"
            | e ->                            log.PrintfnAppErrorMsg  "Problem reading or initalizing settings file: %A"  e
        dict

    let writer = SaveWriter(log)
    
    let settingsAsString () = 
        let sb = StringBuilder()
        for KeyValue(k,v) in settingsDict do
            sb.Append(k).Append(sep).AppendLine(v) |> ignore
        sb.ToString() 
    
    let get k = 
         match settingsDict.TryGetValue k with 
         |true, v  -> 
            //log.PrintfnDebugMsg "Get %s as %s" k v  //for DEBUG only
            Some v
         |false, _ -> 
            //log.PrintfnDebugMsg "missing key %s " k  //for DEBUG only
            None

    let getFloat  key def = match get key with Some v -> float v           | None -> def
    let getInt    key def = match get key with Some v -> int v             | None -> def
    let getBool   key def = match get key with Some v -> Boolean.Parse v   | None -> def
    
    
    do // do for often accessd settings , skips parsing and dict
        selectAllOccurences <- getBool "selectAllOccurences"  true // true as default value


    member this.SetDelayed k v (delay:int)= 
        // delayed because the onMaximise of window event triggers first Loaction changed and then state changed, 
        // state change event should still be able to Get previous size and loaction that is not saved yet
        async{  do! Async.Sleep(delay) 
                settingsDict.[k] <- v         
                } |> Async.Start
        
    member this.Set (k:string) (v:string) = 
        if k.IndexOf(sep) > -1 then log.PrintfnAppErrorMsg  "Settings key shall not contain '%c' : %s%c%s"  sep  k  sep  v            
        if v.IndexOf(sep) > -1 then log.PrintfnAppErrorMsg  "Settings value shall not contain '%c' : %s%c%s"  sep  k  sep  v 
        settingsDict.[k] <- v             
        
    //member this.Get k = get k        

    member this.Save () =                       
        writer.WriteDelayed (filePath, settingsAsString,  500)
        
    member this.SetFloat        key (v:float)       = this.Set key (string v)
    member this.SetFloatDelayed key (v:float) delay = this.SetDelayed key (string v) delay
    member this.SetInt          key (v:int)         = this.Set key (string v)
    member this.SetBool         key (v:bool)        = this.Set key (string v)
    member this.GetFloat        key def = getFloat key def
    member this.GetInt          key def = getInt   key def
    member this.GetBool         key def = getBool  key def


    // Explicit values:

    member this.SelectAllOccurences // do for often accessd settings , skips parsing and dict
        with get () = selectAllOccurences
        and set v = 
            this.SetBool "selectAllOccurences" v
            selectAllOccurences <- v
    
    static member val keyFsiQuiet = "fsiOutputQuiet" 
    


    