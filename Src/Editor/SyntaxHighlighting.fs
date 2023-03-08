namespace Seff.Editor

open System
open Seff.Model

module SyntaxHighlighting = 
    open AvalonEditB
    open AvalonEditB.Highlighting
    open System.IO

    let mutable private fsHighlighting: IHighlightingDefinition option = None //use same highlighter for al tabs. load just once

    let mutable filePath = ""

    let setFSharp (ed:TextEditor, forceReLoad) = //must be a function to be called at later moment.
        if fsHighlighting.IsNone || forceReLoad then
            async{
                try
                    //let stream = Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("FSharpSynatxHighlighter2.xshd") // Build action : Embeded Resource; Copy to ouput Dir: NO
                    let assemblyLocation = IO.Path.GetDirectoryName(Reflection.Assembly.GetExecutingAssembly().Location)
                    let path = Path.Combine(assemblyLocation,"SyntaxHighlightingFSharp.xshd")
                    filePath <- path
                    let stream = new StreamReader(path)//will be copied there after compiling recompiling
                    use reader = new Xml.XmlTextReader(stream)
                    let fsh = Xshd.HighlightingLoader.Load(reader, HighlightingManager.Instance)
                    //HighlightingManager.Instance.RegisterHighlighting("F#", [| ".fsx"; ".fs";".fsi" |], fsh)
                    fsHighlighting <- Some fsh
                    do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context
                    ed.SyntaxHighlighting <- fsh
                    if forceReLoad then ISeffLog.log.PrintfnInfoMsg "loaded syntax highlighting from: %s" path
                with e ->
                    ISeffLog.log.PrintfnAppErrorMsg "Error loading Syntax Highlighting: %A" e
                } |> Async.Start
        else
            ed.SyntaxHighlighting <- fsHighlighting.Value
    

    
    let private xlsWatcher = new FileSystemWatcher()
    let mutable private isWatching = false // to create only once the event

    /// includes file system watcher for FileChanged
    let watch(path:FileInfo,ed:TextEditor) =
        path.Refresh()
        if not isWatching && path.Exists  then 
            let file = path.Name
            let folder = path.DirectoryName
            xlsWatcher.Path <- folder
            xlsWatcher.Filter <- file
            xlsWatcher.NotifyFilter  <-  NotifyFilters.LastWrite  
            xlsWatcher.EnableRaisingEvents <- true // must be after setting path
            xlsWatcher.Changed.Add (fun a ->
                xlsWatcher.EnableRaisingEvents <- false // to not raise events twice
                try
                    async{
                        do! Async.Sleep 200 // wait till file is really closed
                        setFSharp(ed,true)                        
                        } |> Async.StartImmediate                    
                finally
                    async{
                        do! Async.Sleep 500
                        xlsWatcher.EnableRaisingEvents <- true // to not raise events twice
                        } |> Async.StartImmediate
                )
            isWatching <-true

    let openVSCode(ed:TextEditor) = 
        try
            if IO.File.Exists filePath then
                //Diagnostics.Process.Start("code", "\"" + filePath+ "\" --reuse-window") |> ignore
                let p = new System.Diagnostics.Process()
                p.StartInfo.FileName <- "code"
                let inQuotes = "\"" + filePath + "\""
                p.StartInfo.Arguments <- String.concat " " [inQuotes;  "--reuse-window"]
                p.StartInfo.WindowStyle <- Diagnostics.ProcessWindowStyle.Hidden
                p.Start() |> ignore
                watch(FileInfo filePath, ed)
            else
                ISeffLog.log.PrintfnIOErrorMsg "File not found: %s" filePath
        with e ->
            ISeffLog.log.PrintfnIOErrorMsg "Open SyntaxHighlighting with VScode failed: %A" e

       
