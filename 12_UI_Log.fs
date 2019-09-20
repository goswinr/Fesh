namespace Seff

open System
open System.IO


module Log = 
    
     //-----------------
    //--private values--
    //-----------------

    /// A TextWriter that writes using a function (to an Avalonedit Control). (injected into the FSI session constructor)
    type FsxTextWriter(writeStr) =
        inherit TextWriter()
        override this.Encoding =  Text.Encoding.Default
        override this.Write     (s:string)  = writeStr (s)
        override this.WriteLine (s:string)  = writeStr (s + Environment.NewLine)        
        override this.WriteLine ()          = writeStr (    Environment.NewLine) 
        //TODO cache this call and pre fix it to next call of writeStr (s). less UI updates. 
        //see https://github.com/Microsoft/visualfsharp/issues/3712         
        //override this.Flush() = () //needed ?
    
    
    let private dontPrint  = function // do not print certain strings to Log window
        |""                         -> true
        |"For help type #help;;"    -> true
        |"Copyright (c) Microsoft Corporation. All Rights Reserved."    -> true // FCS it  is actulally MIT licence
        | _                         -> false

    let private backlog = Collections.Concurrent.ConcurrentQueue()  // to cache messages that cant be logged yet, (TODO never needed? debug only)
    
    
    let private printLineToLog(s:string) =
        //FileLoggingAgent.Post(s) // debugg logger directly to a file
        if dontPrint s then 
            ()
        elif isNull Sync.syncContext then //|| not isLogReady then // this might never happen ? // install sync context instead?
            backlog.Enqueue s
        else
            async {
                do! Async.SwitchToContext Sync.syncContext
                let backlogMsg =  ref ""
                while backlog.TryDequeue(backlogMsg) do UI.log.AppendText ( "BACKLOG: " + !backlogMsg ) //TODO never needed? debug only                
                UI.log.AppendText s
                UI.log.ScrollToEnd()
            } |> Async.StartImmediate            
    
    //-----------------
    //--public values--
    //-----------------
    
    /// the instance of Textwriter used in FSI
    let textwriter = 
        new FsxTextWriter(printLineToLog) 
  
    /// debug logging, might be disabled later
    let dlog a = 
        textwriter.WriteLine ("dlog: " + a )        
        //fileLoggingAgent.Post(sprintf "%A" a)
    
    /// like printfn, use with format strings, adds new line
    let printf s = 
        Printf.fprintfn textwriter s 
    
    /// prints any type
    let print a = 
        match box a with 
        | :? string as s -> Printf.fprintfn textwriter "%s" s // to avoid printing of quotes around string
        | _              -> Printf.fprintfn textwriter "%A" a

    /// printf, does not add new line at end, use with format strings
    let printInline s = Printf.fprintf textwriter s

    
