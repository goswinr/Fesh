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
    
    let mutable prevLine =""
    let mutable prevprevLine =""
    let private dontPrint  = function // do not print certain strings to Log window
        |""                         -> true
        |"For help type #help;;"    -> true
        |"Copyright (c) Microsoft Corporation. All Rights Reserved."    -> true // FCS it  is actulally MIT licence
        | s when s.StartsWith "--> Referenced" -> true // too noisy
        | s when s="\r\n" && prevLine=s && prevprevLine=s -> true // to not have more than one empty line ever
        | s                         -> 
            prevprevLine<-prevLine
            prevLine<-s
            false

    let private backlog = Collections.Concurrent.ConcurrentQueue()  // to cache messages that cant be logged yet, (TODO never needed? debug only)
    
    
    let private printLineToLog(s:string) =
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
  

    
    /// like printfn, use with format strings, adds new line
    let print s = 
        Printf.fprintfn textwriter s 
    
   
    /// debug logging, might be disabled later
    let debugLogOLD txt = 
        textwriter.WriteLine ("debugLog: " + txt )        
        

    do
        Config.logger <- print "debugLog: %s" 

    
