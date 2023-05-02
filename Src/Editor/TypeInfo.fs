namespace Seff.Editor

open System

open System.Collections.Generic
open System.Windows
open System.Windows.Controls
open System.Windows.Media
open System.Windows.Input
open System.Windows.Documents

open AvalonLog.Brush

open FsEx.Wpf // for TextBlockSelectable

open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.EditorServices    // Misc functionality for editors, e.g. interface stub generation
open FSharp.Compiler.Symbols           // FSharpEntity etc
open FSharp.Compiler.Text              // ISourceFile, Range, TaggedText and other things
open FSharp.Compiler.Tokenization      // FSharpLineTokenizer , and keywords etc.
open FSharp.Compiler.Syntax

open Seff
open Seff.Util
open Seff.Util.General
open Seff.Model
open Seff.XmlParser


type OptDefArg  = string //{ name:string ; defVal:string} //  TODO actual default value from attribute  seems to be not available via FCS see below in: namesOfOptnlArgs(fsu:FSharpSymbolUse)

type DllPath = string
type ErrMsg = string
type PathWithNameSpace =string

type ToolTipData = {
    name          : string; 
    signature     : TaggedText[]
    fullName      : PathWithNameSpace
    optDefs       : ResizeArray<string> 
    xmlDoc        : Result<XmlParser.Child*DllPath,ErrMsg>
    }

type ToolTipExtraData ={
    declListItem  : DeclarationListItem option        // only for autocomplete tooltips
    semanticClass : SemanticClassificationItem option // only for mouse over type info tooltips
    declLocation  : Range option                      // only for mouse over type info tooltips
    dllLocation   : string option                     // only for mouse over type info tooltips
    }
    

///a static class for creating tooltips
type TypeInfo private () = 

    static let loadingTxt =  "Loading type info ..."

    static let black        = Brushes.Black                      |> freeze
    static let gray         = Brushes.Gray                       |> freeze
    static let purple       = Brushes.Purple     |> brighter  40 |> freeze
    
    static let blue         = Brushes.Blue       |> darker    90 |> freeze    
    static let red          = Brushes.DarkSalmon |> darker   120 |> freeze
    static let fullred      = Brushes.Red        |> darker    60 |> freeze
    static let cyan         = Brushes.DarkCyan   |> darker    60 |> freeze    


    static let maxCharInSignLine = 100

    static let loggedErrors = HashSet<string>()


    static let coloredSignature(td:ToolTipData): TextBlockSelectable = 
        let tb = TextBlockSelectable()
        tb.Foreground <- black
        tb.FontSize   <- StyleState.fontSize * 1.2
        tb.FontFamily <- StyleState.fontToolTip
        let ts = td.signature
        let mutable len = 0        
        
        let inline lengthCeck() =
            if len > maxCharInSignLine then
                tb.Inlines.Add( new Run("\r\n    "))
                len <- 0              
        
        for i=0 to ts.Length-1 do
            let t = ts.[i]
            len <- len + t.Text.Length

            match t.Tag with
            | TextTag.Parameter ->
                lengthCeck()              
                // if a parameter is optional add a question mark to the signature
                match ts.[i-1].Text with
                |"?" ->  tb.Inlines.Add( new Run(t.Text , Foreground = gray)) // sometimes optional arguments have already a question mark but not always
                | _ ->
                    match td.optDefs |> Seq.tryFind ( fun oa -> oa = t.Text ) with
                    | Some od ->  tb.Inlines.Add( new Run("?"+t.Text , Foreground = gray ))
                    | None    ->  tb.Inlines.Add( new Run(t.Text     , Foreground = black ))

            | TextTag.Keyword ->                
                lengthCeck() 
                tb.Inlines.Add( new Run(t.Text, Foreground = blue))

            | TextTag.Punctuation->
                match t.Text with
                | "?"        ->   tb.Inlines.Add( new Run(t.Text, Foreground = gray))
                | "*" | "->" ->  tb.Inlines.Add( new Run(t.Text, Foreground = fullred))                    
                |  _         ->  tb.Inlines.Add( new Run(t.Text, Foreground = purple ))                

            | TextTag.Operator //  also used for  DU names in `` ``  !?
            | TextTag.RecordField
            | TextTag.Method
            | TextTag.Property
            | TextTag.Field
            | TextTag.ModuleBinding
            | TextTag.UnionCase
            | TextTag.Member ->   tb.Inlines.Add( new Run(t.Text, Foreground = red))

            | TextTag.Struct
            | TextTag.Class
            | TextTag.Interface
            | TextTag.Function
            | TextTag.Alias ->   tb.Inlines.Add( new Run(t.Text, Foreground = cyan))

            | TextTag.TypeParameter ->   tb.Inlines.Add( new Run(t.Text, Foreground = cyan))   // generative argument like 'T or 'a

            | TextTag.UnknownType
            | TextTag.UnknownEntity ->   tb.Inlines.Add( new Run(t.Text, Foreground = gray))

            | TextTag.LineBreak ->
                len <- t.Text.Length // reset after line break
                tb.Inlines.Add( new Run(t.Text))

            | TextTag.Space -> 
                // skip one space after colon before type tag
                if t.Text.Length=1 && ts.[max 0 (i-1)].Text=":" && ts.[max 0 (i-2)].Tag=TextTag.Parameter then ()                 
                else tb.Inlines.Add( new Run(t.Text))

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
            | TextTag.UnknownEntity ->    tb.Inlines.Add( new Run(t.Text))

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
    /// check if List has at least two items 
    static let twoOrMore = function [] | [_] -> false |_ -> true   

    static let darkgray     = Brushes.Gray       |> darker    40 |> freeze
    static let darkblue     = Brushes.DarkSlateBlue |> darker 20 |> freeze
    static let white        = Brushes.White      |> darker    5  |> freeze

    static let codeRun (td:ToolTipData) t : seq<Run>= 
        [
        new Run(" ") 
        match td.optDefs |> Seq.tryFind ( fun oa -> oa = t ) with
        | Some od ->  new Run("?"+t ,FontFamily = StyleState.fontEditor, FontSize = StyleState.fontSize*1.1,  Foreground = gray,   Background = white) 
        | None    ->  new Run(t ,FontFamily = StyleState.fontEditor, FontSize = StyleState.fontSize*1.1,  Foreground = black,   Background = white)         
        new Run(" ") 
        ]

    static let mainXmlBlock (node:XmlParser.Child, td:ToolTipData): TextBlockSelectable =
        let tb = new TextBlockSelectable()
        tb.FontSize   <- StyleState.fontSize  * 1.0
        tb.FontFamily <- StyleState.fontToolTip        
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
            add <|  TextBlock(Text = "Press Ctrl + P to persist this window.", FontSize = StyleState.fontSize * 0.75) 
        
        match ted.declListItem with
        |None -> ()
        |Some dItem -> 
            let tb = new TextBlockSelectable(Text = dItem.Glyph.ToString() )
            tb.Foreground <- Brushes.DarkOrange |> darker 10
            tb.FontSize <- StyleState.fontSize  * 0.95
            tb.FontFamily <- StyleState.fontToolTip
            //tb.FontWeight <- FontWeights.Bold
            add tb  
            
        match ted.semanticClass with
        |None -> ()
        |Some sem -> 
            let tb = new TextBlockSelectable(Text = sem.Type.ToString() )
            //let tb = new TextBlockSelectable(Text = $"{sem.Type}, {sem.Range.EndColumn-sem.Range.StartColumn} chars") //from {sem.Range.StartColumn}")
            tb.Foreground <- Brushes.DarkOrange |> darker 10
            tb.FontSize <- StyleState.fontSize  * 0.95
            tb.FontFamily <- StyleState.fontToolTip
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
                    tb.FontSize <- StyleState.fontSize * 0.9
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
                    subAdd<|  TextBlockSelectable(Text = errTxt, FontSize = StyleState.fontSize  * 0.75 , FontFamily = StyleState.fontToolTip, Foreground = gray )                   
                
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
                //tb.Inlines.Add( new Run("Full name: ",  Foreground = darkgray))
                tb.Inlines.Add( new Run(td.fullName  ,  Foreground = darkblue))
                tb.Foreground <- darkblue
                tb.FontSize <- StyleState.fontSize  * 1.0
                tb.FontFamily <- StyleState.fontToolTip
                add tb 

        if assemblies.Count > 0 then
            let tb = 
                if assemblies.Count = 1 then new TextBlockSelectable(Text= "assembly: "   + Seq.head assemblies)
                else                         new TextBlockSelectable(Text= "assemblies: " + String.concat "\r\n" assemblies)
            tb.FontSize <- StyleState.fontSize  * 0.85
            tb.Foreground <-black
            //tb.FontFamily <- new FontFamily ("Arial") // or use default of device
            add tb   
        else
            match ted.dllLocation with
            |None -> ()
            |Some f -> 
                    let tb = TextBlockSelectable(Text= "assembly path: " + f)
                    tb.FontSize <- StyleState.fontSize  * 0.85
                    tb.Foreground <-black
                    //tb.FontFamily <- new FontFamily ("Arial") // or use default of device
                    add tb 
        
        match ted.declLocation with
        |None -> ()
        |Some r ->                 
                let f = r.FileName.Replace('\\','/')
                if f <> "unknown" then 
                    let tb = TextBlockSelectable(Text = sprintf "defined at: %s  Line:%d" f r.StartLine)
                    tb.FontSize <- StyleState.fontSize  * 0.85
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

    static let makeToolTipDataList (sdtt: ToolTipText, fullName:string, optDefs:ResizeArray<OptDefArg>) : ToolTipData list= 
        match sdtt with
        | ToolTipText.ToolTipText (els) ->
            match els with
            |[]  -> [] //{name = ""; signature= ""; xmlDocStr = Error "*FSharpStructuredToolTipElement list is empty*"}]
            |els ->
                [ for el in els do
                    match el with
                    | ToolTipElement.None ->
                        yield {name = ""; signature = [||]; fullName=""; optDefs=optDefs; xmlDoc = Error  "*no xml doc string*"}

                    | ToolTipElement.CompositionError(text) ->
                        if loggedErrors.Add(text) then // print only once
                            ISeffLog.log.PrintfnIOErrorMsg "Trying to get a Tooltip for 'fullName' failed with:\r\n%s" text
                        yield {name = ""; signature = [||]; fullName=""; optDefs=optDefs; xmlDoc = Error ("*FSharpStructuredToolTipElement.CompositionError:\r\n"+ text)}

                    | ToolTipElement.Group(tooTipElemDataList) ->
                        for tooTipElemData in tooTipElemDataList do                            
                            yield { name      = Option.defaultValue "" tooTipElemData.ParamName
                                    signature = tooTipElemData.MainDescription 
                                    fullName  = fullName
                                    optDefs   = optDefs
                                    xmlDoc    = findXmlDoc tooTipElemData.XmlDoc}
                ]


    /// Returns the names of optional Arguments in a given method call.
    static let namesOfOptnlArgs(fsu:FSharpSymbolUse, iEditor:IEditor)  :ResizeArray<OptDefArg>= 
        let optDefs = ResizeArray<OptDefArg>(0)        
        try
            match fsu.Symbol with
            | :? FSharpMemberOrFunctionOrValue as x ->
                for ps in x.CurriedParameterGroups do
                    for p in ps do
                        if p.IsOptionalArg then
                            optDefs.Add p.FullName
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
            //| :? FSharp.Compiler.DiagnosticsLogger.StopProcessingExn  -> //not public !!  
            if e.Message.Contains "must add a reference to assembly '" then 
                AutoFixErrors.check(e.Message,iEditor)
            else
                ISeffLog.log.PrintfnAppErrorMsg "GetOptTypeInfo Error: %s:\r\n%s" (e.GetType().FullName) e.Message            
                if notNull e.InnerException then 
                    ISeffLog.log.PrintfnAppErrorMsg "InnerException: %s:\r\n %s" (e.GetType().FullName) e.Message
            
        optDefs
    
    
    static let mutable cachedToolTipData: list<ToolTipData> = []
    static let mutable cachedExtraData = {declListItem=None;semanticClass=None;declLocation=None;dllLocation=None }
    
    static let fsKeywords = 
        FSharpKeywords.KeywordsWithDescription
        |> Seq.map fst
        |> HashSet

    //--------------public values and functions -----------------

    static member loadingText = loadingTxt

    static member namesOfOptionalArgs(fsu:FSharpSymbolUse, iEditor:IEditor) = namesOfOptnlArgs(fsu,iEditor)

    static member makeSeffToolTipDataList (sdtt: ToolTipText, fullName:string, optArgs:ResizeArray<string>) = makeToolTipDataList (sdtt, fullName, optArgs)

    static member getPanel  (tds:ToolTipData list, ed:ToolTipExtraData) = 
        cachedToolTipData  <- tds
        cachedExtraData    <- ed
        makeToolTipPanel (tds, ed, true)

    /// regenerates a view of the last created panel so it can be used again in the popout window
    static member getPanelCached () = 
        makeToolTipPanel (cachedToolTipData, cachedExtraData, false)


    static member mouseHover(e: MouseEventArgs, iEditor:IEditor, tip:ToolTip) = 
        // see https://github.com/icsharpcode/AvalonEdit/blob/master/ICSharpCode.AvalonEdit/Editing/SelectionMouseHandler.cs#L477
        
        match iEditor.FileCheckState with
        | GettingCode _ | Checking _ |CheckFailed | NotStarted -> ()
        | Done res when res.checkRes.HasFullTypeCheckInfo ->
            let av = iEditor.AvaEdit 
            match Mouse.getOffset (e,av) with
            | None -> ()
            | Some off -> 
                let doc = av.Document
                let ln = doc.GetLineByOffset(off)  
                let lineTxt = doc.GetText ln
                let lineNo = ln.LineNumber
                let offLn = off-ln.Offset
                let island = 
                    match QuickParse.GetCompleteIdentifierIsland false lineTxt offLn with 
                    |Some (word, colAtEndOfNames, isQuotedIdentifier)-> Some (word, colAtEndOfNames, isQuotedIdentifier)                    
                    |None -> // find operators because QuickParse.GetCompleteIdentifierIsland does not find them:                       
                        if offLn >= lineTxt.Length then 
                            None
                        else
                            let nextW =
                                let rec find i = 
                                    if i=lineTxt.Length then i//-1
                                    else
                                        let c = lineTxt[i]
                                        if c=' ' || c= '\r' || c='\n' then i//-1
                                        else find (i+1)
                                find offLn
                            let prevW =
                                let rec find i = 
                                    if i = -1 then 0
                                    else
                                        let c = lineTxt[i]
                                        if c=' ' || c= '\r' || c='\n' then i+1
                                        else find (i-1)
                                find offLn
                            if prevW<nextW then 
                                let word = lineTxt.Substring(prevW,nextW-prevW)
                                Some (word, nextW, false) //word, colAtEndOfNames, isQuotedIdentifier
                            else
                                None

                match island with 
                |None -> ()
                    //ISeffLog.log.PrintfnDebugMsg "QuickParse.GetCompleteIdentifierIsland failed : lineTxt:%A, txt: '%s'"  lineTxt (lineTxt.Substring(offLn-1,3))

                |Some (word, colAtEndOfNames, isQuotedIdentifier) -> 
                    tip.Content <- loadingTxt
                    tip.PlacementTarget <- iEditor.AvaEdit // required for property inheritance
                    tip.StaysOpen <- true
                    tip.IsOpen <- true
                    async{ 
                        let qualId  = PrettyNaming.GetLongNameFromString word   
                        //ISeffLog.log.PrintfnDebugMsg "GetToolTip:colAtEndOfNames:%A, lineTxt:%A, qualId:%A" colAtEndOfNames lineTxt qualId

                        // Compute a formatted tooltip for the given location
                        // line:  The line number where the information is being requested.</param>
                        // colAtEndOfNames:  The column number at the end of the identifiers where the information is being requested.</param>
                        // lineText:  The text of the line where the information is being requested.</param>
                        // names:  The identifiers at the location where the information is being requested.</param>
                        // tokenTag:  Used to discriminate between 'identifiers', 'strings' and others. For strings,
                        // an attempt is made to give a tooltip for a #r "..." location.
                        // Use a value from FSharpTokenInfo.Tag, or FSharpTokenTag.Identifier, unless you have other information available.</param>
                        // userOpName:  An optional string used for tracing compiler operations associated with this request.</param>
                        let ttt  =  
                            let r = res.checkRes.GetToolTip (lineNo, colAtEndOfNames, lineTxt, qualId, FSharpTokenTag.Identifier) 
                            match r with
                            | ToolTipText.ToolTipText (els) ->                                
                                match els with
                                |[]  -> 
                                    match qualId with 
                                    |[fsKeyword] when fsKeywords.Contains(fsKeyword) -> res.checkRes.GetKeywordTooltip qualId
                                    | _ ->                                 
                                        res.checkRes.GetToolTip (lineNo, colAtEndOfNames, lineTxt, qualId, FSharpTokenTag.String) // this gives info about referenced assemblies that the first try does not give                                       
                                |els -> r
                        
                        let symbol = res.checkRes.GetSymbolUseAtLocation(lineNo, colAtEndOfNames, lineTxt, qualId )  //only to get to info about optional parameters
                        let fullName = if symbol.IsSome then symbol.Value.Symbol.FullName else ""

                        let optArgs = if symbol.IsSome then namesOfOptnlArgs(symbol.Value, iEditor) else ResizeArray(0)
                        let ttds = makeToolTipDataList (ttt, fullName ,optArgs) //TODO can this still be async ?
                        
                        do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context
                        
                        if List.isEmpty ttds then                                                     
                            tip.Content <- new TextBlock(Text = "No type info found for:\r\n'" + word + "'", FontSize = StyleState.fontSize  * 0.65 ,FontFamily = StyleState.fontToolTip, Foreground = gray )                            
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
                    } |> Async.Start

                //e.Handled <- true //  don't set handled! so that on type errors the  Error tooltip still gets shown after this tooltip


        | Done res -> () // but checkRes.HasFullTypeCheckInfo is false