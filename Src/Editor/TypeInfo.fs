namespace Fesh.Editor

open System

open System.Collections.Generic
open System.Windows
open System.Windows.Controls
open System.Windows.Media
open System.Windows.Input
open System.Windows.Documents

open AvalonLog.Brush

open Fittings // for TextBlockSelectable

open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.EditorServices    // Misc functionality for editors, e.g. interface stub generation
open FSharp.Compiler.Symbols           // FSharpEntity etc
open FSharp.Compiler.Text              // ISourceFile, Range, TaggedText and other things
open FSharp.Compiler.Tokenization      // FSharpLineTokenizer , and keywords etc.
open FSharp.Compiler.Syntax

open Fesh
open Fesh.Util
open Fesh.Util.General
open Fesh.Model
open Fesh.XmlParser
open AvalonEditB


type OptDefArg  = string //{ name:string ; defVal:string} //  TODO actual default value from attribute  seems to be not available via FCS see below in: namesOfOptnlArgs(fsu:FSharpSymbolUse)

type DllPath = string
type ErrMsg = string
type PathWithNameSpace =string

type ToolTipData = {
    name          : string;
    signature     : TaggedText[]
    fullName      : PathWithNameSpace
    //optDefs       : ResizeArray<string>
    xmlDoc        : Result<XmlParser.Child*DllPath,ErrMsg>
    }

type ToolTipExtraData ={
    declListItem  : DeclarationListItem option        // only for autocomplete tooltips
    semanticClass : SemanticClassificationItem option // only for mouse over type info tooltips
    declLocation  : Range option                      // only for mouse over type info tooltips
    dllLocation   : string option                     // only for mouse over type info tooltips
    }

/// to indicate if trailing or leading whitespace shall be trimmed
[<RequireQualifiedAccess>]
type Trim = No | Start | End | Both


///a static class for creating tooltips
type TypeInfo private () =

    static let loadingTxt =  "Loading type info ..."

    static let black        = Brushes.Black                      |> freeze
    static let gray         = Brushes.Gray                       |> freeze
    static let errMsgGray    = Brushes.LightGray                  |> freeze
    static let purple       = Brushes.Purple     |> brighter  40 |> freeze

    static let blue         = Brushes.Blue       |> darker    90 |> freeze
    static let red          = Brushes.DarkSalmon |> darker   120 |> freeze
    static let fullRed      = Brushes.Red        |> darker    60 |> freeze
    static let cyan         = Brushes.DarkCyan   |> darker    60 |> freeze


    static let maxCharInSignLine = 150

    static let loggedErrors = HashSet<string>()


    static let coloredSignature(td:ToolTipData): TextBlockSelectable =
        let tb = TextBlockSelectable()
        tb.Foreground <- black
        tb.FontSize   <- StyleState.fontSize * 1.0 // prev 1.2
        tb.FontFamily <- StyleState.fontToolTip
        tb.TextWrapping <- TextWrapping.Wrap
        let ts = td.signature
        let mutable len = 0
        let mutable skipOptAndDefAttr = false
        let mutable nextParamIsOpt = false
        let mutable literal = ""
        let mutable prev = ""

        let inline lengthCheck() =
            if len > maxCharInSignLine then
                tb.Inlines.Add( new Run("\r\n    "))
                len <- 0

        let append col (txt:string) =
            if not skipOptAndDefAttr then
                len <- len + txt.Length
                prev <- txt
                if notNull col then
                    tb.Inlines.Add( new Run(txt, Foreground = col))
                else
                    tb.Inlines.Add( new Run(txt))

        for i=0 to ts.Length-1 do
            let t = ts.[i]
            // IFeshLog.log.PrintfnDebugMsg $"{i}: {skipOptAndDefAttr}'{t.Text}' Tag:{t.Tag} "

            match t.Tag with
            | TextTag.Parameter ->
                lengthCheck()
                // if a parameter is optional add a question mark to the signature
                match ts.[i-1].Text with
                |"?" -> append gray t.Text // sometimes optional arguments have already a question mark but not always
                | _ ->
                    if nextParamIsOpt then append gray ("?"+t.Text)
                    else                   append black t.Text

                    // match td.optDefs |> Seq.tryFind ( fun oa -> oa = t.Text ) with
                    // | Some _  -> append gray ("?"+t.Text)
                    // | None    -> append black t.Text

                nextParamIsOpt <- false

            | TextTag.Keyword ->
                if skipOptAndDefAttr then
                    if t.Text <> "null" then
                        literal <- t.Text
                else
                    lengthCheck()
                    append blue t.Text

            | TextTag.Punctuation->
                match t.Text with
                | "?"        -> append gray t.Text
                | "*" | "->" -> append fullRed t.Text
                | ">]"       ->
                    append purple t.Text
                    skipOptAndDefAttr <- false // skipOptAndDefAttr might be set to true fom a System.Runtime.InteropServices.Optional attribute
                | "[<" ->
                    if i+4 < ts.Length &&
                        (  ts.[i+1].Text = "Optional" // to skip the System.Runtime.InteropServices.Optional attribute
                        || ts.[i+2].Text = "Optional"
                        || ts.[i+3].Text = "Optional") then
                            skipOptAndDefAttr <- true
                            nextParamIsOpt <- true
                    else
                        append purple t.Text

                | ":" ->
                    if literal <> "" then
                        append gray ("="+literal)
                        literal <- ""
                    append purple t.Text
                |  _ ->
                    append purple t.Text

            | TextTag.Operator //  also used for  DU names in `` ``  !?
            | TextTag.RecordField
            | TextTag.Method
            | TextTag.Property
            | TextTag.Field
            | TextTag.ModuleBinding
            | TextTag.UnionCase
            | TextTag.Member -> append red t.Text

            | TextTag.Class -> append cyan t.Text

            | TextTag.NumericLiteral
            | TextTag.StringLiteral -> // is it a DefaultParameterValue ??
                if skipOptAndDefAttr then
                    if t.Text <> "\"\"" then
                        literal <- t.Text
                else
                    append null t.Text

            | TextTag.Struct
            | TextTag.Interface
            | TextTag.Function
            | TextTag.Alias -> append cyan t.Text

            | TextTag.TypeParameter -> append cyan t.Text   // generative argument like 'T or 'a

            | TextTag.UnknownType   -> append null t.Text
            | TextTag.UnknownEntity -> append null t.Text


            | TextTag.LineBreak ->
                len <- t.Text.Length // reset after line break
                append null t.Text

            | TextTag.Space ->
                let s = t.Text
                // skip one space after colon before type tag
                if i>1 && s.Length=1 && prev=":" && ts.[i-2].Tag=TextTag.Parameter then
                    ()
                elif prev = " " then // skip space after space
                    ()
                else
                    append null s

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
            | TextTag.Text
            | TextTag.UnknownType
            | TextTag.UnknownEntity ->  append null t.Text

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
        match s.IndexOf ':' with
        | 1 ->
            let t = s.Substring(2)
            match t.IndexOf '`' with
            | -1 -> t
            | i  -> t.Substring(0,i)
        | _ -> s

    /// trim start whitespace if the line has no returns
    static let trimStartIfOneLiner (s:string) =
        match s.IndexOf '\n' with
        | -1 -> s.TrimStart()
        | _  -> s


    // removes the first line return if it is only preceded by whitespace
    static let trimStartIfHasRet (s:string) =
        let rec loop i =
            if i = s.Length then
                0
            else
                match s[i] with
                | ' '  -> loop (i+1)
                | '\r' -> loop (i+1)
                | '\n' -> i+1
                |  _   -> i

        match loop 0 with
        | 0 -> s
        | j -> s.Substring(j)


    /// check if List has at least two items
    static let twoOrMore = function [] | [ _ ] -> false | _ -> true

    static let darkGray     = Brushes.Gray          |> darker    40 |> freeze
    static let darkblue     = Brushes.DarkSlateBlue |> darker 20 |> freeze
    static let white        = Brushes.White         |> darker    5  |> freeze

    // static let codeRun (td:ToolTipData) (code:string) : seq<Run> =
    static let codeRun (code:string) : seq<Run> =
        let tx = code.TrimEnd()
        [
        new Run(" ")
        new Run(tx ,FontFamily = StyleState.fontEditor, FontSize = StyleState.fontSize*1.1,  Foreground = black,   Background = white)
        // match td.optDefs |> Seq.tryFind ( fun oa -> oa = tx ) with
        // | Some _  ->  new Run("?"+tx ,FontFamily = StyleState.fontEditor, FontSize = StyleState.fontSize*1.1,  Foreground = gray,    Background = white)
        // | None    ->  new Run(tx     ,FontFamily = StyleState.fontEditor, FontSize = StyleState.fontSize*1.1,  Foreground = black,   Background = white)
        new Run(" ")
        ]


    static let debugPrint (c:Child) =
        let rec printx  i (c:Child) =
            let ind = String(' ',  i*4)
            match c with
            | Text t ->
                IFeshLog.printColor 150 150 150 $"{ind}Text:"
                IFeshLog.printnColor 150 150 0 $"{t}"
            | Node n ->
                IFeshLog.printColor 150 0 150 $"{ind}Node"
                IFeshLog.printColor 150 150 150 ":"
                IFeshLog.printColor 150 150 0 $"{n.name}"
                for at in n.attrs do
                    IFeshLog.printColor 0 150 150 $"; attr"
                    IFeshLog.printColor 150 150 150 ":"
                    IFeshLog.printColor 0 150 150 $"{at.name}"
                    IFeshLog.printColor 150 150 150 "="
                    IFeshLog.printColor 150 0 150 $"{at.value}"
                IFeshLog.printnColor 150 150 150 ""
                for c in n.children do
                    printx (i+1) c
        printx 0 c


    static let nodesHasAttrsOnly(n:XmlParser.Node) =
        (n.children.IsEmpty && not n.attrs.IsEmpty)
        ||
        (
        // catch cases from netstandard.xml from nuget where a name appears twice:
        // <paramref name="totalWidth">totalWidth</paramref>
        n.children.Length = 1 &&
        n.attrs.Length = 1 &&
        n.attrs.Head.name = "name" &&
        n.attrs.Head.value = (match n.children.Head with Text t -> t |Node _ -> "")
        )

    // static let mainXmlBlock (node:XmlParser.Child, td:ToolTipData): TextBlockSelectable =
    static let mainXmlBlock (node:XmlParser.Child): TextBlockSelectable =
        let tb = new TextBlockSelectable()
        tb.FontSize   <- StyleState.fontSize  * 1.0
        tb.FontFamily <- StyleState.fontToolTip
        tb.TextWrapping <- TextWrapping.Wrap
        let mutable last = ""

        let rec loop (this:XmlParser.Child) parentName addTitle (trim:Trim) depth =
            match this with
            |Text t ->
                //printf $"{parentName} {depth} {trim}: '"
                //eprintf $"{t}"
                //printfn "'"
                let txt =
                    if parentName="para" then // don't trim one-liner inside a para tag to keep ASCII art from RhinoCommon.xml
                        t
                    else
                        match trim with
                        |Trim.Both   -> t.TrimEnd() |> trimStartIfHasRet|> trimStartIfOneLiner
                        |Trim.End    -> t.TrimEnd()
                        |Trim.Start  -> t |> trimStartIfHasRet|> trimStartIfOneLiner
                        |Trim.No     -> t |> trimStartIfHasRet
                tb.Inlines.Add( new Run(txt,  Foreground = darkblue, FontStyle = FontStyles.Italic))


            |Node n ->
                if depth=0 then
                    if last <> n.name && addTitle then // && n.name <> "?name?" then // to not repeat the parameter header every time
                        last <- n.name
                        tb.Inlines.Add( new LineBreak())
                        tb.Inlines.Add( new Run(fixName n.name,  Foreground = darkGray)) //FontWeight = FontWeights.Bold,     // Summary header, Parameter header ....
                        tb.Inlines.Add( new LineBreak())
                    for at in n.attrs do // there is normally just one ! like param:name, paramref:name typeparam:name
                        tb.Inlines.AddRange( at.value |> fixTypeName|> codeRun )
                        tb.Inlines.Add( new Run(": ",  Foreground = black))

                    let childs = n.children  |> Array.ofList
                    let lasti = childs.Length - 1
                    for i=lasti downto 0 do
                        let nextTrim =
                            if   i=lasti && i = 0 then Trim.Both
                            elif i=lasti          then Trim.Start // lasti will be first Text Run
                            elif            i = 0 then Trim.End   // index 0 wil be last Text Run
                            else                       Trim.No
                        //printfn $"i:{i} , lasti:{lasti} {nextTrim}: {childs[i]}"
                        loop childs[i] n.name false nextTrim (depth+1)

                    tb.Inlines.Add( new LineBreak())


                elif nodesHasAttrsOnly n then
                    // e.g. for: <returns> <see langword="true" /> if <paramref name="objA" /> is the same instance as <paramref name="objB" /> or if both are null; otherwise, <see langword="false" />.</returns>
                    for at in n.attrs do
                        //printfn $"{n.name} {at.name}:{at.value }"
                        if   at.name="cref"  then  tb.Inlines.AddRange(  at.value |> fixTypeName |>  codeRun )
                        else                       tb.Inlines.AddRange(  at.value                |>  codeRun )
                        //tb.Inlines.Add( new Run(" "))

                else
                    //for at in n.attrs do printfn $"ELSE:{n.name} {at.name}:{at.value } n.children.IsEmpty:{n.children.IsEmpty}={n.children.Length} n.attrs.IsEmpty:{n.attrs.IsEmpty}={n.attrs.Length}"
                    //if not n.children.IsEmpty then printfn $"ELSE:{n.name}:{n.children.Head}"
                    match n.name with
                    |"c"|"code" ->   for c in List.rev n.children do addCode c  (depth+1)
                    |"para"     ->   for c in List.rev n.children do tb.Inlines.Add( new LineBreak()) ;loop    c n.name false Trim.No (depth+1)
                    |"br"       ->   for c in List.rev n.children do tb.Inlines.Add( new LineBreak()) ;loop    c n.name false Trim.No (depth+1) // only happens in netstandard.xml
                    | _         ->   for c in List.rev n.children do                                   loop    c n.name false Trim.No (depth+1)

        and addCode (this:XmlParser.Child) depth =
            match this with
            |Text t ->  tb.Inlines.AddRange(codeRun t) // done in codeRun:  tb.Inlines.Add(" ")
            |Node n ->  loop this n.name false Trim.No depth


        match node with
        |Node n when n.name="member" ->
            let two = twoOrMore n.children
            for ch in List.rev n.children do
                loop ch n.name two Trim.No 0
        | _ ->
            loop node "" false Trim.No 0

        // remove last line break:
        if tb.Inlines.LastInline  :? LineBreak then  tb.Inlines.Remove tb.Inlines.LastInline  |> ignore

        // match node with
        // |Node n -> for c in n.children do debugPrint c
        // | c ->  debugPrint c

        tb


    // make a fancy tooltip panel in a ScrollViewer:
    static let makeToolTipPanel  ( tds:ToolTipData list, ted:ToolTipExtraData,  addPersistInfo:bool) :ScrollViewer =
        let panel = new StackPanel(Orientation = Orientation.Vertical)
        let scrollViewer = new ScrollViewer(Content=panel , VerticalScrollBarVisibility = ScrollBarVisibility.Auto ) //TODO cant be scrolled, never gets focus? because completion window keeps focus on editor?
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
            tb.TextWrapping <- TextWrapping.Wrap
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
            tb.TextWrapping <- TextWrapping.Wrap
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
                    let tb = new TextBlockSelectable(Text = "Name: " + td.name)
                    tb.Foreground <- black
                    tb.FontSize <- StyleState.fontSize * 0.9
                    tb.FontWeight <- FontWeights.Bold
                    tb.TextWrapping <- TextWrapping.Wrap
                    subAdd tb

                subAdd <| coloredSignature(td) // the main colored signature of a F# value

                // the main xml body
                match td.xmlDoc with
                |Ok (node, ass)     ->
                    if ass <> "" then assemblies.Add(ass) |> ignore
                    //if ass.Length > 10 then assemblies.Add("\r\n" + ass) |> ignore // it may be from more than one assembly? because of type extensions?
                    //else                    assemblies.Add(ass) |> ignore
                    subAdd <| mainXmlBlock (node)
                |Error errTxt  ->
                    subAdd<|  TextBlockSelectable(Text = errTxt, TextWrapping = TextWrapping.Wrap, FontSize = StyleState.fontSize  * 0.70 , Foreground = errMsgGray)//,FontFamily = StyleState.fontToolTip )

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
                tb.Inlines.Add( new Run(td.fullName  ,  Foreground = darkblue))
                tb.Foreground <- darkblue
                tb.FontSize <- StyleState.fontSize  * 1.0
                tb.FontFamily <- StyleState.fontToolTip
                tb.TextWrapping <- TextWrapping.Wrap
                add tb

        if assemblies.Count > 0 then
            let tb =
                if assemblies.Count = 1 then new TextBlockSelectable(Text= "assembly: "   + Seq.head assemblies)
                else                         new TextBlockSelectable(Text= "assemblies: " + String.concat "\r\n" assemblies)
            tb.FontSize <- StyleState.fontSize  * 0.85
            tb.Foreground <-black
            tb.TextWrapping <- TextWrapping.Wrap
            //tb.FontFamily <- new FontFamily ("Arial") // or use default of device
            add tb
        else
            match ted.dllLocation with
            |None -> ()
            |Some f ->
                    let tb = TextBlockSelectable(Text= "assembly path: " + f)
                    tb.FontSize <- StyleState.fontSize  * 0.85
                    tb.Foreground <-black
                    tb.TextWrapping <- TextWrapping.Wrap
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
                    tb.TextWrapping <- TextWrapping.Wrap
                    //tb.FontFamily <- new FontFamily ("Arial") // or use default of device
                    add tb

        scrollViewer


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
            |Ok (_, nodeDict) -> // _ = xmlFileInfo
                //printfn $"reading xml:{xmlFileInfo.FullName}"
                match nodeDict.TryGetValue memberName with
                |true , node ->  Ok (node  , dllFile)
                |false, _    ->  Error "no xml" //$"no xml doc found for member '{memberName}' in \r\n'{xmlFi.FullName}'\r\n"
            | Error e ->
                Error e

    static let makeToolTipDataList (sdtt: ToolTipText, fullName:string) = //, optDefs:ResizeArray<OptDefArg>) : ToolTipData list =
        match sdtt with
        | ToolTipText.ToolTipText (els) ->
            match els with
            |[]  -> [] //{name = ""; signature= ""; xmlDocStr = Error "*FSharpStructuredToolTipElement list is empty*"}]
            |els ->
                [ for el in els do
                    match el with
                    | ToolTipElement.None ->
                        yield {name = ""; signature = [||]; fullName=""; xmlDoc = Error  "*no xml doc string*"}

                    | ToolTipElement.CompositionError(text) ->
                        if loggedErrors.Add(text) then // print only once
                            IFeshLog.log.PrintfnIOErrorMsg "Trying to get a Tooltip for 'fullName' failed with:\r\n%s" text
                        yield {name = ""; signature = [||]; fullName=""; xmlDoc = Error ("*FSharpStructuredToolTipElement.CompositionError:\r\n"+ text)}

                    | ToolTipElement.Group(tooTipElemDataList) ->
                        for tooTipElemData in tooTipElemDataList do
                            yield { name      = Option.defaultValue "" tooTipElemData.ParamName
                                    signature = tooTipElemData.MainDescription
                                    fullName  = fullName
                                    xmlDoc    = findXmlDoc tooTipElemData.XmlDoc}
                ]


    /// Returns the names of optional Arguments in a given method call.
    static let namesOfOptnlArgsUNUSED(fsu:FSharpSymbolUse)  :ResizeArray<OptDefArg>=
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
                            //        IFeshLog.log.PrintfnDebugMsg "fa.ConstructorArguments: %A" fa.ConstructorArguments

                            //log.PrintfnDebugMsg "optional full name: %s" c.FullName
            | _ -> ()
        with e ->
            //| :? FSharp.Compiler.DiagnosticsLogger.StopProcessingExn  -> //not public !!
            if e.Message.Contains "must add a reference to assembly '" then
                AutoFixErrors.check(e.Message)
            else
                IFeshLog.log.PrintfnAppErrorMsg "GetOptTypeInfo Error: %s:\r\n%s" (e.GetType().FullName) e.Message
                if notNull e.InnerException then
                    IFeshLog.log.PrintfnAppErrorMsg "InnerException: %s:\r\n %s" (e.GetType().FullName) e.Message

        optDefs

    static let mutable cachedToolTipData: list<ToolTipData> = []
    static let mutable cachedExtraData = {declListItem=None;semanticClass=None;declLocation=None;dllLocation=None }

    static let fsKeywords =
        FSharpKeywords.KeywordsWithDescription
        |> Seq.map fst
        |> HashSet

    //--------------public values and functions -----------------

    static member loadingText = loadingTxt

    //static member namesOfOptionalArgs(fsu:FSharpSymbolUse) = namesOfOptnlArgs(fsu)

    static member makeFeshToolTipDataList (sdtt: ToolTipText, fullName:string) = //, optArgs:ResizeArray<string>) =
        makeToolTipDataList (sdtt, fullName) //, optArgs)

    static member getPanel  (tds:ToolTipData list, ed:ToolTipExtraData) =
        cachedToolTipData  <- tds
        cachedExtraData    <- ed
        makeToolTipPanel (tds, ed, true)

    /// regenerates a view of the last created panel so it can be used again in the pop-out window
    static member getPanelCached () =
        makeToolTipPanel (cachedToolTipData, cachedExtraData, false)


    static member mouseHover(e: MouseEventArgs, iEditor:IEditor, tip:ToolTip) =
        // see https://github.com/icsharpcode/AvalonEdit/blob/master/ICSharpCode.AvalonEdit/Editing/SelectionMouseHandler.cs#L477

        match iEditor.FileCheckState with
        | NotChecked | WaitForCompl _ | WaitForErr _ -> ()
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
                    //IFeshLog.log.PrintfnDebugMsg "QuickParse.GetCompleteIdentifierIsland failed : lineTxt:%A, txt: '%s'"  lineTxt (lineTxt.Substring(offLn-1,3))

                |Some (word, colAtEndOfNames, _ ) -> // _ = isQuotedIdentifier
                    tip.Content <- loadingTxt
                    let tView = av.TextArea.TextView
                    let pos = doc.GetLocation(off)
                    let tvPos = new TextViewPosition(pos.Line,pos.Column)
                    let pt = tView.GetVisualPosition(tvPos, Rendering.VisualYPosition.LineBottom)
                    let ptInclScroll = pt - tView.ScrollOffset
                    tip.PlacementTarget <- av.TextArea
                    tip.PlacementRectangle <- new Rect(ptInclScroll.X, ptInclScroll.Y, 0., 0.)
                    tip.Placement <- Primitives.PlacementMode.Bottom // Error Tooltip is on Top //https://docs.microsoft.com/en-us/dotnet/framework/wpf/controls/popup-placement-behavior
                    tip.VerticalOffset <- -5.0

                    tip.StaysOpen <- true
                    tip.IsOpen <- true
                    async{
                        let qualId  = PrettyNaming.GetLongNameFromString word
                        //IFeshLog.log.PrintfnDebugMsg "GetToolTip:colAtEndOfNames:%A, lineTxt:%A, qualId:%A" colAtEndOfNames lineTxt qualId

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
                                | _ -> r

                        let symbol = res.checkRes.GetSymbolUseAtLocation(lineNo, colAtEndOfNames, lineTxt, qualId )  //only to get to info about optional parameters
                        let fullName = if symbol.IsSome then symbol.Value.Symbol.FullName else ""

                        //let optArgs = if symbol.IsSome then namesOfOptnlArgs(symbol.Value) else ResizeArray(0)
                        let tooltipDataList = makeToolTipDataList (ttt, fullName )//,optArgs) //TODO can this still be async ?

                        do! Async.SwitchToContext Fittings.SyncWpf.context

                        if List.isEmpty tooltipDataList then
                            tip.Content <- new TextBlock(Text = "No type info found for:\r\n'" + word + "'", FontSize = StyleState.fontSize  * 0.65 , FontFamily = StyleState.fontToolTip , Foreground = gray )
                            //ed.TypeInfoToolTip.IsOpen <- false
                        else
                            let sem, declLoc, dllLoc =
                                match symbol with
                                |None -> None,None,None
                                |Some s ->
                                    //IFeshLog.log.PrintfnAppErrorMsg $"s.Symbol.FullName: {s.Symbol.FullName}"
                                    //IFeshLog.log.PrintfnAppErrorMsg $"s.FileName:{s.FileName}"
                                    //IFeshLog.log.PrintfnDebugMsg $"s.Symbol.DeclarationLocation:{s.Symbol.DeclarationLocation}"
                                    //IFeshLog.log.PrintfnDebugMsg $"s.Symbol.Assembly.FileName:{s.Symbol.Assembly.FileName}"
                                    //let sems = res.checkRes.GetSemanticClassification(Some s.Range)
                                    //for sem in sems do IFeshLog.log.PrintfnDebugMsg $"GetSemanticClassification:{sem.Type}"
                                    //let l = s.Range
                                    //let lineNo = l.StartLine
                                    //let colSt  = l.StartColumn
                                    //let colEn  = l.EndColumn
                                    //let sem = iEditor.SemanticRanges |> Array.tryFind (fun s -> let r = s.Range in r.StartLine=lineNo && r.EndLine=lineNo && r.StartColumn=colSt && r.EndColumn=colEn)
                                    let sem =
                                        res.checkRes.GetSemanticClassification(Some s.Range)
                                        |> Array.tryHead

                                    sem , s.Symbol.DeclarationLocation , s.Symbol.Assembly.FileName

                            let ed = {declListItem=None; semanticClass=sem; declLocation=declLoc; dllLocation=dllLoc }
                            let ttPanel = TypeInfo.getPanel (tooltipDataList, ed )
                            if tip.IsOpen then // showing the "loading" text till here.
                                tip.Content <- ttPanel
                    } |> Async.Start

                //e.Handled <- true //  don't set handled! so that on type errors the  Error tooltip still gets shown after this tooltip


        | Done _ -> () // should never happen because here res.checkRes.HasFullTypeCheckInfo is false