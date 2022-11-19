namespace Seff.Editor


open System
open System.IO

open System.Collections.Generic
open System.Windows
open System.Windows.Controls
open System.Windows.Media
open System.Windows.Input
open System.Windows.Documents

open AvalonEditB.Document


open AvalonLog.Brush

open FsEx.Wpf // for TextBlockSelectable

open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.EditorServices    // Misc functionality for editors, e.g. interface stub generation
open FSharp.Compiler.Symbols           // FSharpEntity etc
open FSharp.Compiler.Text              // ISourceFile, Range, TaggedText and other things
open FSharp.Compiler.Tokenization      // FSharpLineTokenizer etc.

open Seff
open Seff.Util
open Seff.Model
open Seff.XmlParser


type OptDefArg   = { name:string } //; defVal:string} //  TODO actual default value from attribute  seems to be not available via FCS see below in: namesOfOptnlArgs(fsu:FSharpSymbolUse)

type DllPath = string
type ErrMsg = string
type PathWithNameSpace =string

type ToolTipData = {
    name          : string; 
    signature     : TaggedText[]
    fullName      : PathWithNameSpace
    optDefs       : ResizeArray<OptDefArg> 
    xmlDoc        : Result<XmlParser.Child*DllPath,ErrMsg>
    }

type ToolTipExtraData ={
    declListItem  : DeclarationListItem option        // only for autocomplete tooltips
    semanticClass : SemanticClassificationItem option // only for mose over type info tooltips
    declLocation  : Range option                      // only for mose over type info tooltips
    dllLocation   : string option                     // only for mose over type info tooltips
    }

///a static class for creating tooltips
type TypeInfo private () = 

    static let loadingTxt =  "Loading type info ..."

    static let darkgray     = Brushes.Gray       |> darker    40 |> freeze
    static let gray         = Brushes.Gray                       |> freeze
    static let lightgray    = Brushes.Gray       |> brighter 100 |> freeze
    static let blue         = Brushes.Blue       |> darker    90 |> freeze
    static let darkblue     = Brushes.DarkSlateBlue |> darker  20|> freeze
    static let darkgreen    = Brushes.DarkGreen  |> darker    20 |> freeze 
    static let darkpurple   = Brushes.Purple     |> darker    90 |> freeze
    static let purple       = Brushes.Purple     |> brighter  40 |> freeze
    static let black        = Brushes.Black                      |> freeze
    static let red          = Brushes.DarkSalmon |> darker   120 |> freeze
    static let fullred      = Brushes.Red        |> darker    60 |> freeze
    static let cyan         = Brushes.DarkCyan   |> darker    60 |> freeze
    static let white        = Brushes.White      |> darker    5  |> freeze

    static let maxCharInSignLine = 100

    static let coloredSignature(td:ToolTipData): TextBlockSelectable = 
        let tb = TextBlockSelectable()
        tb.Foreground <- black
        tb.FontSize   <- Style.fontSize * 1.2
        tb.FontFamily <- Style.fontEditor
        let ts = td.signature
        let mutable len = 0
        let lastArrI = // used for giving the return value a different background            
            ts 
            |> Array.tryFindIndexBack ( fun t -> t.Tag=TextTag.Punctuation && t.Text = "->") 
            |> Option.defaultWith (fun () -> 
                ts 
                |> Array.tryFindIndexBack ( fun t -> t.Tag=TextTag.Punctuation && t.Text = ":") 
                |> Option.defaultValue 999999
                )
            
            
        let mutable bG :SolidColorBrush = null // used for giving the return value a different background
        for i=0 to ts.Length-1 do
            let t = ts.[i]
            len <- len + t.Text.Length

            match t.Tag with
            | TextTag.Parameter ->
                if len > maxCharInSignLine then
                        tb.Inlines.Add( new Run("\r\n    ", Background=bG))
                        len <- 0                
                // if a parameter is optional add a question mark to the signature
                match ts.[i-1].Text with
                |"?" ->  tb.Inlines.Add( new Run(t.Text , Foreground = gray, Background=bG )) // sometimes optional arguments have already a question mark but not always
                | _ ->
                    match td.optDefs |> Seq.tryFind ( fun oa -> oa.name = t.Text ) with
                    | Some od ->  tb.Inlines.Add( new Run("?"+t.Text , Foreground = gray , Background=bG))
                    | None    ->  tb.Inlines.Add( new Run(t.Text     , Foreground = black , Background=bG))

            | TextTag.Keyword ->
                if t.Text = "val" then bG <- null // val is the listing of members in a type , not a return value anymore
                tb.Inlines.Add( new Run(t.Text, Foreground = blue, Background=bG ))

            | TextTag.Operator -> tb.Inlines.Add( new Run(t.Text, Foreground = Brushes.Green, Background=bG ))
            | TextTag.Punctuation->
                match t.Text with
                | "?"        ->   tb.Inlines.Add( new Run(t.Text, Foreground = gray, Background=bG))
                | "*" | "->" ->  tb.Inlines.Add( new Run(t.Text, Foreground = fullred, Background=bG))                    
                |  _         ->  tb.Inlines.Add( new Run(t.Text, Foreground = purple , Background=bG))
                if i >= lastArrI then bG <- white // to have a white color on return value

            | TextTag.RecordField
            | TextTag.Method
            | TextTag.Property
            | TextTag.Field
            | TextTag.ModuleBinding
            | TextTag.UnionCase
            | TextTag.Member ->   tb.Inlines.Add( new Run(t.Text, Foreground = red, Background=bG ))

            | TextTag.Struct
            | TextTag.Class
            | TextTag.Interface
            | TextTag.Function
            | TextTag.Alias ->   tb.Inlines.Add( new Run(t.Text, Foreground = cyan, Background=bG ))

            | TextTag.TypeParameter ->   tb.Inlines.Add( new Run(t.Text, Foreground = cyan, Background=bG ))   // generative argument like 'T or 'a

            | TextTag.UnknownType
            | TextTag.UnknownEntity ->   tb.Inlines.Add( new Run(t.Text, Foreground = gray, Background=bG ))

            | TextTag.LineBreak ->
                len <- t.Text.Length // reset after line break
                tb.Inlines.Add( new Run(t.Text, Background=bG))

            | TextTag.Space -> 
                // skip one space after colon before type tag
                if   t.Text.Length=1 && ts.[max 0 (i-1)].Text=":" && ts.[max 0 (i-2)].Tag=TextTag.Parameter then () 
                if i = lastArrI+1 then tb.Inlines.Add( new Run(t.Text, Background=null))
                else tb.Inlines.Add( new Run(t.Text, Background=bG))

            | TextTag.Namespace
            | TextTag.ActivePatternCase
            | TextTag.ActivePatternResult
            | TextTag.Union
            | TextTag.Delegate
            | TextTag.Enum
            | TextTag.Event
            | TextTag.Local
            | TextTag.Record
            | TextTag.Module
            | TextTag.NumericLiteral
            | TextTag.StringLiteral
            | TextTag.Text
            | TextTag.UnknownType
            | TextTag.UnknownEntity ->    tb.Inlines.Add( new Run(t.Text, Background=bG))

        (*
        let debugHelp = 
            td.signature
            |> Seq.filter (fun t -> t.Tag <> TextTag.Punctuation && t.Tag <> TextTag.Space && t.Tag <> TextTag.Operator && t.Tag <> TextTag.LineBreak)
            |> Seq.map(fun t -> sprintf "%A" t.Tag)
            |> String.concat "|"
        tb.Inlines.Add( new Run("\r\n"+debugHelp,Foreground = lightgray))
        *)
        tb
    

    static let fixName s =  
        match s with 
        | "param"     -> "Parameters: "
        | "exception" -> "Exceptions: "
        | "typeparam" -> "Type Parameters: "
        | t           -> Str.up1 t + ": "

    // for F:System.IO.Path.InvalidPathChars -> System.IO.Path.InvalidPathChars
    static let fixTypeName (s:string) =  
        match s.IndexOf ":" with 
        | 1 -> 
            let t = s.Substring(2) 
            match t.IndexOf "`" with 
            | -1 -> t
            | i  -> t.Substring(0,i) 
        | _ -> s    

    static let trimIfOneLiner (s:string) = 
        let t = s.TrimStart() 
        match t.IndexOf '\n' with 
        | -1 -> t
        | i  -> s 

    static let codeRun (td:ToolTipData) t : seq<Run>= 
        [
        new Run(" ") 
        match td.optDefs |> Seq.tryFind ( fun oa -> oa.name = t ) with
        | Some od ->  new Run("?"+t ,FontFamily = Style.fontEditor, FontSize = Style.fontSize*1.1,  Foreground = gray,   Background = white) 
        | None    ->  new Run(t ,FontFamily = Style.fontEditor, FontSize = Style.fontSize*1.1,  Foreground = black,   Background = white)         
        new Run(" ") 
        ]

    /// check if List has at least two items 
    static let twoOrMore = function [] | [_] -> false |_ -> true       

    static let mainXmlBlock (node:XmlParser.Child, td:ToolTipData): TextBlockSelectable =
        let tb = new TextBlockSelectable()
        tb.FontSize   <- Style.fontSize  * 1.0
        tb.FontFamily <- Style.fontToolTip        
        let mutable last = "" 
        
        let rec loop (c:XmlParser.Child) parentName addTitle d = 
            match c with
            |Text t ->  
                // the main xml text description
                if parentName="para" then tb.Inlines.Add( new Run(t,  Foreground = darkblue)) // don't trim oneliners inside a para tag to keep ascii art from RhinoCommon.xml
                else                      tb.Inlines.Add( new Run(trimIfOneLiner t,  Foreground = darkblue, FontStyle = FontStyles.Italic)) 
                
            |Node n ->  
                if d=0 then                     
                    if last<>n.name && addTitle then // && n.name <> "?name?" then // to not repeat the parameter header every time
                        last <- n.name
                        tb.Inlines.Add( new LineBreak()) 
                        tb.Inlines.Add( new Run(fixName n.name,  Foreground = darkgray)) //FontWeight = FontWeights.Bold,     // Summary header, Parameter header ....               
                        tb.Inlines.Add( new LineBreak())                     
                    for at in n.attrs do // there is normally just one ! like param:name, paramref:name typeparam:name                         
                        tb.Inlines.AddRange( at.value |> fixTypeName|> codeRun td )
                        tb.Inlines.Add( new Run(": ",  Foreground = black))  
                    for c in List.rev n.children do 
                        loop c n.name false (d+1)
                    tb.Inlines.Add( new LineBreak()) 
                    
                elif n.children.IsEmpty && not n.attrs.IsEmpty then 
                    // e.g. for: <returns> <see langword="true" /> if <paramref name="objA" /> is the same instance as <paramref name="objB" /> or if both are null; otherwise, <see langword="false" />.</returns>
                    for at in n.attrs do 
                        if at.name="cref" then  tb.Inlines.AddRange(  at.value |> fixTypeName |>  codeRun td)
                        else                    tb.Inlines.AddRange(  at.value                |>  codeRun td)
                        tb.Inlines.Add( new Run(" ")) 
                else
                    match n.name with 
                    |"c"|"code" ->   for c in List.rev n.children do addCode c  (d+1)
                    |"para"     ->   for c in List.rev n.children do tb.Inlines.Add( new LineBreak()) ;loop    c n.name false (d+1)
                    |"br"       ->   for c in List.rev n.children do tb.Inlines.Add( new LineBreak()) ;loop    c n.name false (d+1) // only happens in netstandard.xml
                    | _         ->   for c in List.rev n.children do                                   loop    c n.name false (d+1)
        
        and addCode (c:XmlParser.Child) d = 
            match c with
            |Text t ->  tb.Inlines.AddRange(codeRun td t) // done in codeRun:  tb.Inlines.Add(" ")
            |Node n ->  loop c n.name false d
        

        match node with 
        |Node n when n.name="member" ->  
            let two = twoOrMore n.children
            for c in List.rev n.children do 
                loop c n.name two 0 
        | _ -> 
            loop node "" false 0  
        
        // remove last line break: 
        if tb.Inlines.LastInline  :? LineBreak then  tb.Inlines.Remove tb.Inlines.LastInline  |> ignore 
        
        tb        


    // make a fancy tooltip panel:
    static let makeToolTipPanel  ( tds:ToolTipData list, ted:ToolTipExtraData,  addPersistInfo:bool) = 
        let panel = new StackPanel(Orientation = Orientation.Vertical)
        let inline add(e:UIElement) =  panel.Children.Add e |> ignore            

        if addPersistInfo then 
            add <|  TextBlock(Text = "Press Ctrl + P to persist this window.", FontSize = Style.fontSize * 0.75) 
        
        match ted.declListItem with
        |None -> ()
        |Some dItem -> 
            let tb = new TextBlockSelectable(Text = dItem.Glyph.ToString() )
            tb.Foreground <- Brushes.DarkOrange |> darker 10
            tb.FontSize <- Style.fontSize  * 0.95
            tb.FontFamily <- Style.fontEditor
            //tb.FontWeight <- FontWeights.Bold
            add tb  
            
        match ted.semanticClass with
        |None -> ()
        |Some sem -> 
            let tb = new TextBlockSelectable(Text = sem.Type.ToString() )
            //let tb = new TextBlockSelectable(Text = $"{sem.Type}, {sem.Range.EndColumn-sem.Range.StartColumn} chars") //from {sem.Range.StartColumn}")
            tb.Foreground <- Brushes.DarkOrange |> darker 10
            tb.FontSize <- Style.fontSize  * 0.95
            tb.FontFamily <- Style.fontEditor
            //tb.FontWeight <- FontWeights.Bold
            add tb  

        
        let mutable assemblies = new HashSet<string>()
        let deDup = HashSet() // just because some typ provider signatures appears multiple times, filter them out with hashset
        for td in tds do
            let sign = td.signature |> Seq.map (fun tt -> tt.Text)  |> String.Concat
            if not <| deDup.Contains(sign) then // just because some type provider signatures appears multiple times, filter them out with hashset
                deDup.Add sign  |> ignore
                
                let subPanel = new StackPanel(Orientation = Orientation.Vertical)
                let inline subAdd(e:UIElement) =  subPanel.Children.Add e |> ignore 

                if td.name <> "" then
                    let tb = new TextBlockSelectable(Text= "Name: " + td.name)
                    tb.Foreground <- black
                    tb.FontSize <- Style.fontSize * 0.9
                    //tb.FontFamily <- Style.elronet
                    tb.FontWeight <- FontWeights.Bold
                    subAdd tb

                subAdd <| coloredSignature(td) // the main colored signature of a F# value

                // the main xml body
                match td.xmlDoc with
                |Ok (node, ass)     ->
                    if ass <> "" then assemblies.Add(ass) |> ignore
                    //if ass.Length > 10 then assemblies.Add("\r\n" + ass) |> ignore // it may be from more than one assembly? because of type extensions?
                    //else                    assemblies.Add(ass) |> ignore
                    subAdd <| mainXmlBlock (node, td)
                |Error errTxt  ->
                    subAdd<|  TextBlockSelectable(Text = errTxt, FontSize = Style.fontSize  * 0.80 , FontFamily = Style.fontToolTip, Foreground = gray )                   
                
                let border = Border()
                border.Child <- subPanel
                border.BorderThickness <- Thickness(1.0)
                border.BorderBrush <- Brushes.LightGray
                border.Padding <- Thickness(4.0)
                border.Margin <- Thickness(2.0)
                add border 
            
            // add full name:
            if td.fullName<>"" then 
                let tb = new TextBlockSelectable()     
                tb.Inlines.Add( new Run("Full name: ",  Foreground = darkgray))
                tb.Inlines.Add( new Run(td.fullName  ,  Foreground = darkblue))
                tb.Foreground <- darkblue
                tb.FontSize <- Style.fontSize  * 1.0
                tb.FontFamily <- Style.fontEditor
                add tb 

        if assemblies.Count > 0 then
            let tb = 
                if assemblies.Count = 1 then new TextBlockSelectable(Text= "assembly: "   + Seq.head assemblies)
                else                         new TextBlockSelectable(Text= "assemblies: " + String.concat "\r\n" assemblies)
            tb.FontSize <- Style.fontSize  * 0.85
            tb.Foreground <-black
            //tb.FontFamily <- new FontFamily ("Arial") // or use default of device
            add tb   
        else
            match ted.dllLocation with
            |None -> ()
            |Some f -> 
                    let tb = TextBlockSelectable(Text= "assembly path: " + f)
                    tb.FontSize <- Style.fontSize  * 0.85
                    tb.Foreground <-black
                    //tb.FontFamily <- new FontFamily ("Arial") // or use default of device
                    add tb 
        
        match ted.declLocation with
        |None -> ()
        |Some r ->                 
                let f = r.FileName.Replace('\\','/')
                if f <> "unknown" then 
                    let tb = TextBlockSelectable(Text = sprintf "define at: %s  Line:%d" f r.StartLine)
                    tb.FontSize <- Style.fontSize  * 0.85
                    tb.Foreground <-black
                    //tb.FontFamily <- new FontFamily ("Arial") // or use default of device
                    add tb 
                
        ScrollViewer(Content=panel , VerticalScrollBarVisibility = ScrollBarVisibility.Auto ) //TODO cant be scrolled, never gets focus? because completion window keeps focus on editor?

    

    /// Returns docstring und dll path
    static let findXmlDoc (cmt:FSharpXmlDoc) : Result<XmlParser.Child*DllPath, ErrMsg> = 
        //mostly copied from same named function in Docstring.fs
        match cmt with
        | FSharpXmlDoc.None -> 
            Error "*FSharpXmlDoc.None*"
        
        | FSharpXmlDoc.FromXmlText fsXmlDoc ->
            // this might be a xml Doc string that is not from an xml file but from the current .fsx document
            try                 
                let cs = 
                    fsXmlDoc.UnprocessedLines
                    |> String.concat Environment.NewLine
                    |> XmlParser.readAll
                match cs with 
                | []  -> Error ( "FSharpXmlDoc.FromXmlText empty")
                | cs  -> Ok (XmlParser.Node {name="member";  attrs=[];  children=cs}, "") // must be empty
            with e ->
                Error $"FSharpXmlDoc.FromXmlText: {e}"           
        
        | FSharpXmlDoc.FromXmlFile(dllFile, memberName) ->
            match DocString.getXmlDoc dllFile with
            |Ok (fi, nodeDict) -> 
                match nodeDict.TryGetValue memberName with 
                |true , node ->  Ok (node  , dllFile)
                |false, _    ->  Error $"no xml doc found for member '{memberName}' in \r\n'{fi.FullName}'\r\n"
            | Error e ->
                Error e         
 


    static let makeToolTipDataList (sdtt: ToolTipText, fullName:string, optDfes:ResizeArray<OptDefArg>) : ToolTipData list= 
        match sdtt with
        | ToolTipText.ToolTipText (els) ->
            match els with
            |[]  -> [] //{name = ""; signature= ""; xmlDocStr = Error "*FSharpStructuredToolTipElement list is empty*"}]
            |els ->
                [ for el in els do
                    match el with
                    | ToolTipElement.None ->
                        yield {name = ""; signature = [||]; fullName=""; optDefs=optDfes; xmlDoc = Error  "*FSharpStructuredToolTipElement.None*"}

                    | ToolTipElement.CompositionError(text) ->
                        yield {name = ""; signature = [||]; fullName=""; optDefs=optDfes; xmlDoc = Error ("*FSharpStructuredToolTipElement.CompositionError: "+ text)}

                    | ToolTipElement.Group(tooTipElemDataList) ->
                        for tooTipElemData in tooTipElemDataList do                            
                            yield { name      = Option.defaultValue "" tooTipElemData.ParamName
                                    signature = tooTipElemData.MainDescription 
                                    fullName  = fullName
                                    optDefs   = optDfes
                                    xmlDoc    = findXmlDoc tooTipElemData.XmlDoc}
                ]


    /// Returns the names of optional Arguments in a given method call.
    static let namesOfOptnlArgs(fsu:FSharpSymbolUse)  :ResizeArray<OptDefArg>= 
        let optDefs = ResizeArray<OptDefArg>(0)
        try
            match fsu.Symbol with
            | :? FSharpMemberOrFunctionOrValue as x ->
                for ps in x.CurriedParameterGroups do
                    for p in ps do
                        if p.IsOptionalArg then
                            optDefs.Add  {name = p.FullName} //; defVal="?" }
                            // TODO p.Attributes is always empty even for DefaultParameterValueAttribute ! why ?
                            // all below fails to get the default arg :
                            //match p.TryGetAttribute<System.Runtime.InteropServices.DefaultParameterValueAttribute>() with
                            //|None ->
                            //    optDefs.Add  {name = p.FullName; defVal="?" }
                            //|Some fa ->
                            //    if fa.ConstructorArguments.Count = 1 then
                            //        let (ty,value) = fa.ConstructorArguments.[0]
                            //        optDefs.Add  {name = p.FullName; defVal = value.ToString() }
                            //    else
                            //        ISeffLog.log.PrintfnDebugMsg "fa.ConstructorArguments: %A" fa.ConstructorArguments

                            //log.PrintfnDebugMsg "optional full name: %s" c.FullName
            | _ -> ()
        with e -> 
            () //ISeffLog.log.PrintfnAppErrorMsg "Error while trying to show a Tool tip in Seff.\r\nYou can ignore this error.\r\nin TypeInfo.namesOfOptnlArgs: %A" e
        optDefs
    


    
    static let mutable cachedToolTipData: list<ToolTipData> = []
    static let mutable cachedExtraData = {declListItem=None;semanticClass=None;declLocation=None;dllLocation=None }
    

    //--------------public values and functions -----------------

    static member loadingText = loadingTxt

    static member namesOfOptionalArgs(fsu:FSharpSymbolUse) = namesOfOptnlArgs(fsu)

    static member makeSeffToolTipDataList (sdtt: ToolTipText, fullName:string, optArgs:ResizeArray<OptDefArg>) = makeToolTipDataList (sdtt, fullName, optArgs)

    static member getPanel  (tds:ToolTipData list, ed:ToolTipExtraData) = 
        cachedToolTipData  <- tds
        cachedExtraData    <- ed
        makeToolTipPanel (tds, ed, true)

    /// regenerates a view of the last created panel so it can be used again in the popout window
    static member getPanelCached () = 
        makeToolTipPanel (cachedToolTipData, cachedExtraData, false)


    static member mouseHover(e: MouseEventArgs, iEditor:IEditor, tip:ToolTip) = 
        // see https://github.com/icsharpcode/AvalonEdit/blob/master/ICSharpCode.AvalonEdit/Editing/SelectionMouseHandler.cs#L477

        let doc = iEditor.AvaEdit.Document
        let pos = iEditor.AvaEdit.GetPositionFromPoint(e.GetPosition(iEditor.AvaEdit))
        if pos.HasValue then
            match iEditor.FileCheckState with
            | GettingCode _ | Checking _ |Failed | NotStarted -> ()
            | Done res ->
                let line = pos.Value.Line

                //TODO check for in string to give #r tooltip
                //TODO fails on ´´ backtick names
                //TODO test using FCS instead for finding words:  let partialLongName = QuickParse.GetPartialLongNameEx(pos.lineToCaret, colSetBack - 1)
                  
                

                let offset = doc.GetOffset(pos.Value.Location)
                let startOffset = TextUtilities.GetNextCaretPosition(doc, offset, LogicalDirection.Backward, CaretPositioningMode.WordBorderOrSymbol)// TODO fails on ´´ backtick names
                let endOffset =   TextUtilities.GetNextCaretPosition(doc, offset, LogicalDirection.Forward,  CaretPositioningMode.WordBorderOrSymbol)// returns -1 on empty lines;
                if startOffset < endOffset then // to skip empty lines
                    let docLine = doc.GetLineByOffset(offset)
                    let endCol = endOffset - docLine.Offset
                    let lineTxt = doc.GetText(docLine)
                    let word = doc.GetText(max 0 startOffset, endOffset-startOffset).Trim() // max function to avoid -1
                    if word<>"" then 
                        //ISeffLog.log.PrintfnDebugMsg "word = '%s' Line:%d starting at %d get from %d to %d: in '%s'" word line docLine.Offset startOffset endOffset lineTxt
                        tip.Content <- loadingTxt
                        tip.PlacementTarget <- iEditor.AvaEdit // required for property inheritance
                        tip.StaysOpen <- true
                        tip.IsOpen <- true

                        async{
                            // <summary>Compute a formatted tooltip for the given location</summary>
                            // <param name="line">The line number where the information is being requested.</param>
                            // <param name="colAtEndOfNames">The column number at the end of the identifiers where the information is being requested.</param>
                            // <param name="lineText">The text of the line where the information is being requested.</param>
                            // <param name="names">The identifiers at the location where the information is being requested.</param>
                            // <param name="tokenTag">Used to discriminate between 'identifiers', 'strings' and others. For strings,
                            //              an attempt is made to give a tooltip for a #r "..." location.
                            //              Use a value from FSharpTokenInfo.Tag, or FSharpTokenTag.Identifier, unless you have other information available.</param>
                            // <param name="userOpName">An optional string used for tracing compiler operations associated with this request.</param>

                            do! Async.SwitchToThreadPool()

                            let ttt    = res.checkRes.GetToolTip            (line, endCol, lineTxt, [word], FSharpTokenTag.Identifier)      //TODO, can this call be avoided use info from below symbol call ? // TODO move into checker
                            let symbol = res.checkRes.GetSymbolUseAtLocation(line, endCol, lineTxt, [word] )                                //only to get to info about optional parameters
                            let fullName = if symbol.IsSome then symbol.Value.Symbol.FullName else ""

                            let optArgs = if symbol.IsSome then namesOfOptnlArgs(symbol.Value) else ResizeArray(0)
                            let ttds = makeToolTipDataList (ttt, fullName ,optArgs) //TODO can this still be async ?
                        
                            do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context
                        
                            if List.isEmpty ttds then                                                     
                                tip.Content <- new TextBlock(Text = "No type info found for:\r\n'" + word + "'", FontSize = Style.fontSize  * 0.7,FontFamily = Style.fontToolTip, Foreground = gray )                            
                                //ed.TypeInfoToolTip.IsOpen <- false
                            else
                            
                                let sem, declLoc, dllLoc = 
                                    match symbol with 
                                    |None -> None,None,None
                                    |Some s ->                                    
                                        //ISeffLog.log.PrintfnAppErrorMsg $"s.Symbol.FullName: {s.Symbol.FullName}"
                                        //ISeffLog.log.PrintfnAppErrorMsg $"s.FileName:{s.FileName}"
                                        //ISeffLog.log.PrintfnDebugMsg $"s.Symbol.DeclarationLocation:{s.Symbol.DeclarationLocation}"
                                        //ISeffLog.log.PrintfnDebugMsg $"s.Symbol.Assembly.FileName:{s.Symbol.Assembly.FileName}"
                                        //let sems = res.checkRes.GetSemanticClassification(Some s.Range)
                                        //for sem in sems do ISeffLog.log.PrintfnDebugMsg $"GetSemanticClassification:{sem.Type}"                                    
                                        let l = s.Range
                                        let lineNo = l.StartLine
                                        let colSt  = l.StartColumn
                                        let colEn  = l.EndColumn                                    
                                        let sem = iEditor.SemanticRanges |> Array.tryFind (fun s -> let r = s.Range in r.StartLine=lineNo && r.EndLine=lineNo && r.StartColumn=colSt && r.EndColumn=colEn)                                        
                                        sem, s.Symbol.DeclarationLocation ,s.Symbol.Assembly.FileName
                            
                            
                                let ed = {declListItem=None; semanticClass=sem; declLocation=declLoc; dllLocation=dllLoc }
                                let ttPanel = TypeInfo.getPanel (ttds, ed )
                                if tip.IsOpen then
                                    // TODO hide tooltip and use use popup instead now, so it can be pinned?
                                    tip.Content <- ttPanel
                            } |> Async.StartImmediate //TODO: add Cancellation ?

                //e.Handled <- true //  don't set handled! so that on type errors the  Error tooltip still gets shown after this tooltip


