namespace Seff

open System
open System.Drawing

module XshdHighlighting = 
    open ICSharpCode.AvalonEdit
    open ICSharpCode.AvalonEdit.Highlighting
    open System.IO
    
    let mutable private fsHighlighting: IHighlightingDefinition option = None //use same highlighter for al tabs. load just once 

    let setFSharp (ed:TextEditor,forceReLoad) = //must be a function to be calld at later moment.
        if fsHighlighting.IsNone || forceReLoad then 
            async{
                try
                    //let stream = Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("FSharpSynatxHighlighter2.xshd") // Build action : Embeded Resource; Copy to ouput Dir: NO 
                    let assemblyLocation = IO.Path.GetDirectoryName(Reflection.Assembly.GetExecutingAssembly().Location)                    
                    let stream = new StreamReader(Path.Combine(assemblyLocation,"FSharpSynatxHighlighterExtended.xshd"))//will be copied there after compiling recompiling
                    use reader = new Xml.XmlTextReader(stream)
                    let fsh = Xshd.HighlightingLoader.Load(reader, HighlightingManager.Instance)
                    HighlightingManager.Instance.RegisterHighlighting("F#", [| ".fsx"; ".fs";".fsi" |], fsh)
                    fsHighlighting <- Some fsh                
                    do! Async.SwitchToContext Sync.syncContext
                    ed.SyntaxHighlighting <- fsh
                with e -> 
                    Log.Print "Error loading Syntax Highlighting: %A" e
                } |> Async.Start
        else 
            ed.SyntaxHighlighting <- fsHighlighting.Value


/// taken from VS2017
module ColorsUNUSED=
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

