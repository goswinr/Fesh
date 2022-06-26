﻿namespace Seff.Editor


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


type OptDefArg   = {name:string } //; defVal:string}//  default value seems to be not available via FCS see below in: namesOfOptnlArgs(fsu:FSharpSymbolUse)

type ToolTipData = {
    name:string; 
    signature:TaggedText[]
    optDefs: ResizeArray<OptDefArg> 
    xmlDoc: Result<XmlParser.Child*string,string>
    }

///a static class for creating tooltips
type TypeInfo private () = 

    static let loadingTxt =  "Loading type info ..."

    static let gray         = Brushes.Gray                       |> freeze
    static let lightgray    = Brushes.Gray       |> brighter 100 |> freeze
    static let blue         = Brushes.Blue       |> darker    90 |> freeze
    static let darkblue     = Brushes.Blue       |> darker   120 |> freeze
    static let darkpurple   = Brushes.Purple     |> darker    90 |> freeze
    static let purple       = Brushes.Purple     |> brighter  40 |> freeze
    static let black        = Brushes.Black                      |> freeze
    static let red          = Brushes.DarkSalmon |> darker   120 |> freeze
    static let fullred      = Brushes.Red        |> darker    60 |> freeze
    static let cyan         = Brushes.DarkCyan   |> darker    60 |> freeze
    static let white        = Brushes.White      |> freeze

    static let maxCharInSignLine = 100

    static let coloredSignature(td :ToolTipData): TextBlockSelectable = 
        let tb = TextBlockSelectable()
        tb.Foreground <- black
        tb.FontSize   <- Style.fontSize  * 1.1
        tb.FontFamily <- Style.fontEditor
        let ts = td.signature
        let mutable len = 0
        for i=0 to ts.Length-1 do
            let t = ts.[i]
            len <- len + t.Text.Length

            match t.Tag with
            | TextTag.Parameter ->
                // if a paramter is optional add a question mark to the signature
                match ts.[i-1].Text with
                |"?" ->  tb.Inlines.Add( new Run(t.Text , Foreground = gray )) // sometimes optional arguments have already a question mark but not always
                | _ ->
                    match td.optDefs |> Seq.tryFind ( fun oa -> oa.name = t.Text ) with
                    | Some od ->  tb.Inlines.Add( new Run("?"+t.Text , Foreground = gray ))
                    | None    ->  tb.Inlines.Add( new Run(t.Text     , Foreground = black ))

            | TextTag.Keyword ->
                tb.Inlines.Add( new Run(t.Text, Foreground = blue ))

            | TextTag.Operator -> tb.Inlines.Add( new Run(t.Text, Foreground = Brushes.Green ))
            | TextTag.Punctuation->
                match t.Text with
                | "?" ->         tb.Inlines.Add( new Run(t.Text, Foreground = gray))
                | "*"
                | "->" ->
                    if len > maxCharInSignLine then
                        tb.Inlines.Add( new Run("\r\n    "))
                        len <- 0
                    tb.Inlines.Add( new Run(t.Text, Foreground = fullred))//, FontWeight = FontWeights.Bold ))
                |  _  ->
                    tb.Inlines.Add( new Run(t.Text, Foreground = purple ))

            | TextTag.RecordField
            | TextTag.Method
            | TextTag.Property
            | TextTag.Field
            | TextTag.ModuleBinding
            | TextTag.UnionCase
            | TextTag.Member ->   tb.Inlines.Add( new Run(t.Text, Foreground = red ))

            | TextTag.Struct
            | TextTag.Class
            | TextTag.Interface
            | TextTag.Function
            | TextTag.Alias ->   tb.Inlines.Add( new Run(t.Text, Foreground = cyan ))

            | TextTag.TypeParameter ->   tb.Inlines.Add( new Run(t.Text, Foreground = cyan ))   // generative argument like 'T or 'a

            | TextTag.UnknownType
            | TextTag.UnknownEntity ->   tb.Inlines.Add( new Run(t.Text, Foreground = gray ))

            | TextTag.LineBreak ->
                len <- t.Text.Length // reset after line berak
                tb.Inlines.Add( new Run(t.Text))

            | TextTag.Space -> 
                // skip one space after colon before type tag
                if   t.Text.Length=1 && ts.[max 0 (i-1)].Text=":" && ts.[max 0 (i-2)].Tag=TextTag.Parameter then () 
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

    static let fixTypeName (s:string) =  // for F:System.IO.Path.InvalidPathChars -> System.IO.Path.InvalidPathChars
        match s.IndexOf ":" with 
        | 1 -> 
            let t = s.Substring(2) 
            match t.IndexOf "`" with 
            | -1 -> t
            | i  -> t.Substring(0,i) 
        | _ -> s           

    static let codeRun t = new Run(t ,FontFamily = Style.fontEditor, FontSize = Style.fontSize*1.05,  Foreground = black)//,   Background = white) 

    /// check if List has at least two items 
    static let twoOrMore = function [] | [_] -> false |_ -> true       

    static let mainXmlBlock  (node:XmlParser.Child): TextBlockSelectable =
        let tb = new TextBlockSelectable()
        tb.FontSize   <- Style.fontSize  * 0.95
        tb.FontFamily <- Style.fontToolTip
        tb.Foreground <- darkblue
        let mutable last = "" 
        
        let rec loop (c:XmlParser.Child) addTitle d = 
            match c with
            |Text t ->  tb.Inlines.Add( new Run(t+" ")) 
            |Node n ->  
                if d=0 then                     
                    if last<>n.name && addTitle then // && n.name <> "?name?" then // to not repeat the parameter header every time
                        last <- n.name
                        tb.Inlines.Add( new LineBreak()) 
                        tb.Inlines.Add( new Run(fixName n.name,  Foreground = gray)) //FontWeight = FontWeights.Bold,                    
                        tb.Inlines.Add( new LineBreak())                     
                    for at in n.attrs do // there is normaly just one ! like param:name, paramref:name typeparam:name                         
                        tb.Inlines.Add( at.value |> fixTypeName|> codeRun)
                        tb.Inlines.Add( new Run(": "))  
                    for c in List.rev n.children do 
                        loop c false (d+1)
                    tb.Inlines.Add( new LineBreak()) 
                    
                elif n.children.IsEmpty && not n.attrs.IsEmpty then 
                    // e.g. for: <returns> <see langword="true" /> if <paramref name="objA" /> is the same instance as <paramref name="objB" /> or if both are null; otherwise, <see langword="false" />.</returns>
                    for at in n.attrs do 
                        if at.name="cref" then  tb.Inlines.Add(  at.value |> fixTypeName |>  codeRun)
                        else                    tb.Inlines.Add(  at.value                |>  codeRun)
                        tb.Inlines.Add( new Run(" ")) 
                else
                    match n.name with 
                    |"c"|"code" ->   for c in List.rev n.children do addCode c (d+1)
                    | _         ->   for c in List.rev n.children do loop    c false (d+1)
        
        and addCode (c:XmlParser.Child) d = 
            match c with
            |Text t ->  tb.Inlines.Add(codeRun t); tb.Inlines.Add(" ")
            |Node _ ->  loop c false d
        

        match node with 
        |Node n when n.name="member" ->  
            let two = twoOrMore n.children
            for c in List.rev n.children do 
                loop c two 0 
        | _ -> 
           loop node false 0  
        
        // remove last line break: 
        if tb.Inlines.LastInline  :? LineBreak then  tb.Inlines.Remove tb.Inlines.LastInline  |> ignore 
        
        tb        


    // make a fancy tooltip panel:
    static let makeToolTipPanel  (it:DeclarationListItem option, tds:ToolTipData list, addPersistInfo:bool) = 
        let panel = new StackPanel(Orientation = Orientation.Vertical)
        let inline add(e:UIElement) =  panel.Children.Add e |> ignore            

        if addPersistInfo then 
            add <|  TextBlock(Text = "Press Ctrl + P to persist this window.", FontSize = Style.fontSize * 0.7) 
        
        if it.IsSome then
            let tb = new TextBlockSelectable(Text = sprintf "%A" it.Value.Glyph)
            tb.Foreground <- Brushes.DarkOrange
            tb.FontSize <- Style.fontSize  * 0.85
            tb.FontFamily <- Style.fontEditor
            //tb.FontWeight <- FontWeights.Bold
            add tb         
        
        let mutable assemblies = new HashSet<string>()
        let deDup = HashSet() // just because some typ provider signatures apears mutiple times, filter them out with hashset
        for td in tds do
            let sign = td.signature |> Seq.map (fun tt -> tt.Text)  |> String.Concat
            if not <| deDup.Contains(sign) then // just because some type provider signatures apears mutiple times, filter them out with hashset
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

                subAdd <| coloredSignature(td) // the main coored signature of a F# value

                // the main xml body
                match td.xmlDoc with
                |Ok (node,ass)     ->
                    assemblies.Add(ass) |> ignore // it be from more than one assembly? because of type extensions?
                    subAdd<| mainXmlBlock node
                |Error errTxt  ->
                    subAdd<|  TextBlockSelectable(Text = errTxt, FontSize = Style.fontSize  * 0.75 , FontFamily = Style.fontToolTip, Foreground = gray )                   
                
                let border = Border()
                border.Child <- subPanel
                border.BorderThickness <- Thickness(1.0)
                border.BorderBrush <- Brushes.LightGray
                border.Padding <- Thickness(4.0)
                border.Margin <- Thickness(2.0)
                add border 

        if assemblies.Count > 0 then
            let tb = 
                if assemblies.Count = 1 then new TextBlockSelectable(Text= "assembly:\r\n" + Seq.head assemblies)
                else                         new TextBlockSelectable(Text= "assemblies:\r\n" + String.concat "\r\n" assemblies)
            tb.FontSize <- Style.fontSize  * 0.80
            tb.Foreground <-black
            //tb.FontFamily <- new FontFamily ("Arial") // or use default of device
            add tb 
                
        ScrollViewer(Content=panel , VerticalScrollBarVisibility = ScrollBarVisibility.Auto ) //TODO cant be scrolled, never gets focus? because completion window keeps focus on editor?

    

    /// Returns docstring und dll path
    static let findXmlDoc (cmt:FSharpXmlDoc) : Result<XmlParser.Child*string,string> = 
        //mostly copied from same named function in Docstring.fs
        match cmt with
        | FSharpXmlDoc.None -> 
            Error "*FSharpXmlDoc.None*"
        
        | FSharpXmlDoc.FromXmlText xmlDoc ->
            // this might be a xml Doc string that is not from an xml file but from the current .fsx document
            try                 
                let cs = 
                    xmlDoc.UnprocessedLines
                    |> String.concat Environment.NewLine
                    |> XmlParser.readAll
                match cs with 
                | []  -> Error ( "FSharpXmlDoc.FromXmlText empty")
                | cs  -> Ok (XmlParser.Node {name="member";  attrs=[];  children=cs}, "this file")
            with e ->
                Error $"FSharpXmlDoc.FromXmlText: {e}"           
        
        | FSharpXmlDoc.FromXmlFile(dllFile, memberName) ->
           match DocString.getXmlDoc dllFile with
           |Ok (fi,nodeDict) -> 
                match nodeDict.TryGetValue memberName with 
                |true , node ->  Ok (node  , dllFile)
                |false, _    ->  Error $"no xml doc found for member '{memberName}' in \r\n'{fi.FullName}'\r\n"
           | Error e ->
                Error e         
 


    static let makeToolTipDataList (sdtt: ToolTipText, optDfes:ResizeArray<OptDefArg>) : ToolTipData list= 
        match sdtt with
        | ToolTipText.ToolTipText (els) ->
            match els with
            |[]  -> [] //{name = ""; signature= ""; xmlDocStr = Error "*FSharpStructuredToolTipElement list is empty*"}]
            |els ->
                [ for el in els do
                    match el with
                    | ToolTipElement.None ->
                        yield {name = ""; signature = [||]; optDefs=optDfes; xmlDoc = Error  "*FSharpStructuredToolTipElement.None*"}

                    | ToolTipElement.CompositionError(text) ->
                        yield {name = ""; signature = [||]; optDefs=optDfes; xmlDoc = Error ("*FSharpStructuredToolTipElement.CompositionError: "+ text)}

                    | ToolTipElement.Group(tooTipElemDataList) ->
                        for tooTipElemData in tooTipElemDataList do                            
                            yield { name      = Option.defaultValue "" tooTipElemData.ParamName
                                    signature = tooTipElemData.MainDescription
                                    optDefs   = optDfes
                                    xmlDoc    = findXmlDoc tooTipElemData.XmlDoc}
                ]


    /// Returns the names of optional Arguments in a given method call.
    static let namesOfOptnlArgs(fsu:FSharpSymbolUse) :ResizeArray<OptDefArg>= 
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

    static let mutable cachedDeclarationListItem:DeclarationListItem option = None
    static let mutable cachedToolTipData: list<ToolTipData> = []

    //--------------public values and functions -----------------

    static member loadingText = loadingTxt

    static member namesOfOptionalArgs(fsu:FSharpSymbolUse) = namesOfOptnlArgs(fsu)

    static member makeSeffToolTipDataList (sdtt: ToolTipText, optArgs:ResizeArray<OptDefArg>) = makeToolTipDataList (sdtt, optArgs)

    static member getPanel  (it:DeclarationListItem option, tds:ToolTipData list) = 
        cachedDeclarationListItem <- it
        cachedToolTipData <- tds
        makeToolTipPanel (it, tds, true)

    /// regenerates a view of the last created panel so it can be used again in the popout window
    static member getPanelCached () = 
        makeToolTipPanel (cachedDeclarationListItem, cachedToolTipData, false)


    static member mouseHover(e: MouseEventArgs, iEditor:IEditor, log:ISeffLog, tip:ToolTip) = 
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
                let startOffset = TextUtilities.GetNextCaretPosition(doc,offset, LogicalDirection.Backward, CaretPositioningMode.WordBorderOrSymbol)// TODO fails on ´´ backtick names
                let endOffset =   TextUtilities.GetNextCaretPosition(doc,offset, LogicalDirection.Forward,  CaretPositioningMode.WordBorderOrSymbol)// returns -1 on empty lines;
                if startOffset < endOffset then // to skip empty lines
                    let docLine = doc.GetLineByOffset(offset)
                    let endCol = endOffset - docLine.Offset
                    let lineTxt = doc.GetText(docLine)
                    let word = doc.GetText(max 0 startOffset, endOffset-startOffset) // max function to avoid -1
                    //log.PrintfnDebugMsg "word = '%s' Line:%d starting at %d get from %d to %d: in '%s'" word line docLine.Offset startOffset endOffset lineTxt

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

                        let ttt =    res.checkRes.GetToolTip            (line, endCol, lineTxt, [word], FSharpTokenTag.Identifier)      //TODO, can this call be avoided use info from below symbol call ? // TODO move into checker
                        let symbls = res.checkRes.GetSymbolUseAtLocation(line, endCol, lineTxt, [word] )                                //only to get to info about optional paramters
                        let optArgs = if symbls.IsSome then namesOfOptnlArgs(symbls.Value) else ResizeArray(0)

                        do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context

                        let ttds = makeToolTipDataList (ttt, optArgs) ///TODO can this still be async ?
                        if List.isEmpty ttds then
                            let w = word.Trim()
                            //if w <> "" then     tip.Content <- "No type info found for:\r\n" + word
                            if w <> "" then     tip.Content <- new TextBlock(Text = "No type info found for:\r\n" + word, FontSize = Style.fontSize  * 0.7,FontFamily = Style.fontToolTip, Foreground = gray )
                            else                tip.Content <- "No tip"
                            //ed.TypeInfoToolTip.IsOpen <- false
                        else
                            let ttPanel = TypeInfo.getPanel (None , ttds)
                            if tip.IsOpen then
                                // TODO hide tooltip and use use popup instead now, so it can be pinned?
                                tip.Content <- ttPanel
                        } |> Async.StartImmediate //TODO: add Cancellation ?

                //e.Handled <- true //  don't set handeled! so that on type errors the  Error tooltip still gets shown after this tooltip


