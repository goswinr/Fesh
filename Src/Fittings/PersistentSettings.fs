namespace Fittings

open System
open System.Globalization
open System.Text

/// A class to save window size, layout and position, or any arbitrary string-string key-value pairs.
/// This class is useful when app.config does not work in a hosted context.
/// Keys may not contain the separator character, Values and keys may not contain a new line character.
/// Comments are not allowed.
/// Whitespace around keys and values will be trimmed off.
/// Any errors are reported to the provided logging functions.
type PersistentSettings (settingsFile:IO.FileInfo, separator:char, errorLogger:string->unit) =

    let  sep  = separator // key value separator
    let settingsDict = new Collections.Concurrent.ConcurrentDictionary<string,string>()

    let writer =
        if not settingsFile.Directory.Exists then
            IO.Directory.CreateDirectory settingsFile.DirectoryName |> ignore
        SafeReadWriter(settingsFile.FullName, settingsDict, errorLogger)

    do
        match writer.CreateFileIfMissing "" with
        | Failed -> () //errors get logged to errorLogger function
        | Created -> () //for case when Settings file is not found. (This is expected on first use of the App.)"
        | ExitedAlready ->
            match writer.ReadAllLines() with
            |None -> () //errors get logged
            |Some lns ->
                for ln in lns do
                    if not <| String.IsNullOrWhiteSpace ln then // ignore empty lines
                        match ln.IndexOf(sep) with
                        | -1 -> errorLogger (sprintf "Bad line in settings file: '%s'" ln)
                        | i ->
                            let k = ln.Substring(0,i).Trim()
                            if ln.Length > i then
                                let v = ln.Substring(i+1).Trim() // + 1 to skip sep
                                settingsDict.[k] <- v // TODO allow for comments? tricky because comments need to be saved back too
                            else
                                settingsDict.[k] <- "" // empty value

    let settingsAsString () =
        let sb = StringBuilder()
        for KeyValue(k,v) in settingsDict |> Seq.sortBy (fun (KeyValue(k,_)) -> k) do // sorted for better debugging
            sb.Append(k).Append(sep).AppendLine v |> ignore
        sb.ToString()

    let tryGet k =
         match settingsDict.TryGetValue k with
         |true, v  ->  Some v
         |false, _ ->  None

    let pFloat(s:string) = match Double.TryParse s  with |true, v -> Some v   |false,_ -> None
    let pInt  (s:string) = match Int32.TryParse s   with |true, v -> Some v   |false,_ -> None
    let pBool (s:string) = match Boolean.TryParse s with |true, v -> Some v   |false,_ -> None

    let getFloat key def  = tryGet key |> Option.bind pFloat |> Option.defaultValue def
    let getInt key def    = tryGet key |> Option.bind pInt   |> Option.defaultValue def
    let getBool key def   = tryGet key |> Option.bind pBool  |> Option.defaultValue def


    /// Create a class to save window size, layout and position, or any arbitrary string-string key value pairs.
    /// This class is useful when app.config does not work in a hosted context.
    /// Keys may not contain the separator character '=' , Values and keys may not contain a new line character.
    /// Comments are not allowed.
    /// Whitespace around keys and values will be trimmed off.
    /// Any errors are reported to the provided logging functions.
    new (settingsFile:IO.FileInfo, errorLogger:string->unit) =
        PersistentSettings (settingsFile, '=', errorLogger)


    /// Save setting with a delay.
    /// Delayed because the OnMaximize of window event triggers first location changed and then state changed,
    /// State change event should still be able to Get previous size and location that is not saved yet.
    /// Call Save() afterwards.
    member this.SetDelayed (k, v, delay:int)=
        async{
            do! Async.Sleep delay
            settingsDict.[k] <- v
            this.SaveWithDelay()
        } |> Async.Start

    /// Add string value to the Concurrent settings Dictionary.
    /// Keys may not contain the separator character, Values and keys may not contain a new line character.
    /// If they do it will be replaced by empty string. And a message will be written to errorLogger.
    /// Comments care not allowed.
    /// Call this.Save() afterwards to write to file async with a bit of delay.
    member this.Set(key:string, value:string) :unit =
        let mutable k = key
        let mutable v = value
        if k.IndexOf(sep)  > -1 then k <- k.Replace(string sep," ") ; errorLogger(sprintf "separator in key:%s" key)
        if k.IndexOf('\r') > -1 then k <- k.Replace("\r"      ," ") ; errorLogger(sprintf "newline in key:%s" key)
        if k.IndexOf('\n') > -1 then k <- k.Replace("\n"      ," ") ; errorLogger(sprintf "newline in key:%s" key)
        if v.IndexOf('\r') > -1 then v <- v.Replace("\r"      ," ") ; errorLogger(sprintf "newline in value:%s" value)
        if v.IndexOf('\n') > -1 then v <- v.Replace("\n"      ," ") ; errorLogger(sprintf "newline in value:%s" value)
        settingsDict.[k] <- v
        this.SaveWithDelay()


    /// Write to Settings to  file in specified in constructor.
    /// Writes after a delay of 400 ms and only if there was no more recent call to Save.
    member this.SaveWithDelay() =
        writer.WriteIfLast (settingsAsString,  400)


    /// give delay in milliseconds
    member this.SaveWithDelay(delay) =
        writer.WriteIfLast (settingsAsString,  delay)


    /// Using maximum digits of precision
    member this.SetFloatHighPrec (key, v:float) =
        this.Set (key, v.ToString("R", CultureInfo.InvariantCulture)) // R is slower but nicer than G17 formatter. InvariantCulture to not mess up , and .


    /// Using just one digit after zero for precision
    member this.SetFloat (key,v:float) =
        this.Set (key, v.ToString("0.#", CultureInfo.InvariantCulture)) // InvariantCulture to not mess up , and .

    /// Save float to dict after a delay.
    /// Using just one digit after zero for precision.
    /// A delay is useful e.g. because the OnMaximize of window event triggers first Location changed and then state changed,
    /// State change event should still be able to Get previous size and location that is not saved yet.
    member this.SetFloatDelayed (key, v:float, delay) =
        this.SetDelayed (key, v.ToString("0.#",CultureInfo.InvariantCulture), delay) // InvariantCulture to not mess up , and .


    /// Save int to dict after a delay.
    /// A delay is useful e.g. because the OnMaximize of window event triggers first Location changed and then state changed,
    /// State change event should still be able to Get previous size and location that is not saved yet.
    member this.SetIntDelayed (key, v:int, delay) =
        this.SetDelayed (key, v.ToString(), delay)

    member this.SetInt(key, v:int)  =
        this.Set(key, string v)

    member this.SetBool(key, v:bool) =
        this.Set(key, string v)


    /// Also saves the default value to the settings if not found.
    member this.GetFloat (key:string, def:float)  = tryGet key |> Option.bind pFloat |> Option.defaultWith ( fun () -> this.SetFloat(key, def); this.SaveWithDelay(); def)

    /// Also saves the default value to the settings if not found.
    member this.GetInt  (key:string, def:int)    = tryGet key |> Option.bind pInt    |> Option.defaultWith ( fun () -> this.SetInt(key, def); this.SaveWithDelay(); def)

    /// Also saves the default value to the settings if not found.
    member this.GetBool (key:string, def:bool)   = tryGet key |> Option.bind pBool   |> Option.defaultWith ( fun () -> this.SetBool(key, def); this.SaveWithDelay(); def)

    /// Also saves the default value to the settings if not found.
    member this.GetString (key:string, def:string) = tryGet key                      |> Option.defaultWith ( fun () -> this.Set(key, def); this.SaveWithDelay(); def)


    member this.TryGetString key = tryGet key
    member this.TryGetInt   key = tryGet key |> Option.bind pInt
    member this.TryGetFloat key = tryGet key |> Option.bind pFloat
    member this.TryGetBool  key = tryGet key |> Option.bind pBool




    [<Obsolete("Use tryGetString instead, it saves too")>]
    member this.Get (key) = tryGet key

    [<Obsolete("Use GetFloat instead, it saves too")>]
    member this.GetFloatSaveDefault (key,def) = this.GetFloat(key,def)

    [<Obsolete("Use GetInt instead, it saves too")>]
    member this.GetIntSaveDefault (key,def) = this.GetInt(key,def)

    [<Obsolete("Use GetBool instead, it saves too")>]
    member this.GetBoolSaveDefault (key,def) = this.GetBool(key,def)

    [<Obsolete("Use Get instead, it saves too")>]
    member this.GetSaveDefault (key,def) = this.GetString(key,def)


