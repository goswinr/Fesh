namespace Seff.Editor

open System
open System.Drawing

open FSharp.Compiler.EditorServices

open Seff

open Seff.Config
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

       

    
    (*
    /// taken from VS2017
    module ColorsUNUSED = 
        // TODO take colors from https://github.com/johnpapa/vscode-winteriscoming/blob/master/themes/WinterIsComing-light-color-theme.json

        open FSharp.Compiler.CodeAnalysis

        let shadowed        = Color.FromArgb(188,0,0 )
        let comment         = Color.FromArgb( 0,128,0)
        let disposable      = Color.FromArgb(43,145,175)
        let functionVal     = Color.FromArgb(0,0,0 )
        let methodVal       = Color.FromArgb(0,0,0 )
        let properties      = Color.FromArgb(0,0,0 )
        let mutableVar      = Color.FromArgb(160,128,0 )
        let refVar          = Color.FromArgb(160,128,0 )
        let printfFormat    = Color.FromArgb(43,145,175) // #2B91AF

        let stringVal       = Color.FromArgb(163,21,21 )// #A31515
        let stringVerbatim  = Color.FromArgb(128,0,0 )
        let keyword         = Color.FromArgb(0,0,255 )

        let number          = Color.FromArgb(0,0,0 )
        let operator        = Color.FromArgb(0,0,0 )
        let unusedDecl      = Color.FromArgb(157,157,157 )//VS2015

        let types           = Color.FromArgb(43,145,175)//VS2015
        let escapeChars     = Color.FromArgb(255,0,128) // #FF0080 VS2015

        let colorUNUSED glyph = 
            match glyph with  // from completion window / does not change coler when selected anymore
            | FSharpGlyph.Class
            | FSharpGlyph.Typedef
            | FSharpGlyph.Type
            | FSharpGlyph.Exception         -> Brushes.DarkBlue

            | FSharpGlyph.Union
            | FSharpGlyph.Enum              -> Brushes.DarkGray

            | FSharpGlyph.EnumMember
            | FSharpGlyph.Variable
            | FSharpGlyph.Field             -> Brushes.Black

            | FSharpGlyph.Constant          -> Brushes.DarkCyan
            | FSharpGlyph.Event             -> Brushes.DarkRed
            | FSharpGlyph.Delegate          -> Brushes.DarkMagenta
            | FSharpGlyph.Interface         -> Brushes.DarkCyan
            | FSharpGlyph.Method            -> Brushes.Black
            | FSharpGlyph.OverridenMethod   -> Brushes.DarkKhaki
            | FSharpGlyph.Module            -> Brushes.Black
            | FSharpGlyph.NameSpace         -> Brushes.Black
            | FSharpGlyph.Property          -> Brushes.DarkGreen
            | FSharpGlyph.Struct            -> Brushes.Blue
            | FSharpGlyph.ExtensionMethod   -> Brushes.DarkKhaki
            | FSharpGlyph.Error             -> Brushes.Red
    *)
