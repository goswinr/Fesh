namespace Seff.Editor

open Seff
open Seff.Config
open System
open System.Drawing
open Seff.Model
open System.Windows

module SyntaxHighlighting = 
    open ICSharpCode.AvalonEdit
    open ICSharpCode.AvalonEdit.Highlighting
    open System.IO
    
    let mutable private fsHighlighting: IHighlightingDefinition option = None //use same highlighter for al tabs. load just once 

    let mutable filePath = ""

    let setFSharp (ed:TextEditor, config:Config, forceReLoad) = //must be a function to be calld at later moment.
        if fsHighlighting.IsNone || forceReLoad then 
            async{
                try
                    //let stream = Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("FSharpSynatxHighlighter2.xshd") // Build action : Embeded Resource; Copy to ouput Dir: NO 
                    let assemblyLocation = IO.Path.GetDirectoryName(Reflection.Assembly.GetExecutingAssembly().Location)
                    let path = Path.Combine(assemblyLocation,"FSharpSynatxHighlighterExtended.xshd")
                    filePath <- path
                    let stream = new StreamReader(path)//will be copied there after compiling recompiling
                    use reader = new Xml.XmlTextReader(stream)
                    let fsh = Xshd.HighlightingLoader.Load(reader, HighlightingManager.Instance)
                    HighlightingManager.Instance.RegisterHighlighting("F#", [| ".fsx"; ".fs";".fsi" |], fsh)
                    fsHighlighting <- Some fsh                
                    do! Async.SwitchToContext Sync.syncContext
                    ed.SyntaxHighlighting <- fsh
                    if forceReLoad then config.Log.PrintfnInfoMsg "loaded syntax highlighting from: %s" path
                with e -> 
                    config.Log.PrintfnAppErrorMsg "Error loading Syntax Highlighting: %A" e
                } |> Async.Start
        else 
            ed.SyntaxHighlighting <- fsHighlighting.Value
    
    
    let openVSCode(log:ISeffLog) = 
        async{
            try
                if IO.File.Exists filePath then 
                    let p = new System.Diagnostics.Process()
                    p.StartInfo.FileName <- "code"
                    let inQuotes = "\"" + filePath + "\"" 
                    p.StartInfo.Arguments <- String.concat " " [inQuotes;  "--reuse-window"]
                    log.PrintfnCustomColor 0 0 200 "command:\r\n%s %s" p.StartInfo.FileName p.StartInfo.Arguments
                    //p.StartInfo.UseShellExecute <- false
                    //p.StartInfo.CreateNoWindow <- true //true if the process should be started without creating a new window to contain it
                    //p.StartInfo.RedirectStandardError <-true
                    //p.StartInfo.RedirectStandardOutput <-true
                    //p.OutputDataReceived.Add ( fun d -> log.PrintfnCustomBrush Media.Brushes.Red   "%s" d.Data)
                    //p.ErrorDataReceived.Add (  fun d -> log.PrintfnAppErrorMsg "%s" d.Data)               
                    //p.Exited.Add( fun _ -> log.PrintfnInfoMsg  "opend XSHD")
                    p.Start() |> ignore
                    //p.BeginOutputReadLine()
                    //p.BeginErrorReadLine()
                    //p.WaitForExit()
                else
                   log.PrintfnIOErrorMsg "File not found: %s" filePath    
            with e -> 
                log.PrintfnIOErrorMsg "Failed opening VS Code: %A" e                
            } |> Async.Start
    
    // TODO take colors from https://github.com/johnpapa/vscode-winteriscoming/blob/master/themes/WinterIsComing-light-color-theme.json


    /// taken from VS2017
    module ColorsUNUSED=
        open FSharp.Compiler.SourceCodeServices

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
