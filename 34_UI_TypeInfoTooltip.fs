namespace Seff

open Seff.UtilWPF
open System
open System.Windows
open System.Windows.Controls
open System.Windows.Media
open System.IO
open System.Xml
open System.Text.RegularExpressions
open ICSharpCode.AvalonEdit.CodeCompletion
open ICSharpCode.AvalonEdit.Editing
open ICSharpCode.AvalonEdit.Document
open FSharp.Compiler
open FSharp.Compiler.SourceCodeServices


module Tooltips = 
    open System.Windows.Input
    open System.Windows.Documents

    //type XmlDocStr = Doc of string | Err of string | NoDoc

    type ToolTipData = {name:string; signature:string; xmlDocStr: Result<string,string>}

    // make a fancy tooltip:
    let stackPanel  (it:FSharpDeclarationListItem option) (tds:ToolTipData list) = 
        makePanelVert [
            if it.IsSome then 
                let tb = new TextBlock(Text= sprintf "%A" it.Value.Glyph)
                tb.Foreground <- Brushes.DarkOrange
                tb.FontSize <- Appearance.fontSize * 0.9
                tb.FontFamily <- Appearance.defaultFont
                tb.FontWeight <- FontWeights.Bold
                yield tb 
            
                //let tb = new TextBlock(Text= sprintf "Kind:%A" it.Value.Kind)
            
            for td in tds do
                if td.name <> ""then 
                    let tb = new TextBlock(Text= td.name)
                    tb.Foreground <- Brushes.Black
                    tb.FontSize <- Appearance.fontSize
                    tb.FontFamily <- Appearance.defaultFont
                    tb.FontWeight <- FontWeights.Bold
                    yield tb 
                if td.signature <> ""then 
                    let tb = new TextBlock(Text= td.signature)
                    tb.Foreground <- Brushes.Black
                    tb.FontSize <- Appearance.fontSize
                    tb.FontFamily <- Appearance.defaultFont
                    yield tb
                
                let color, txt,scale  = 
                    match td.xmlDocStr with 
                    |Ok txt     -> SolidColorBrush(Color.FromRgb(40uy,40uy,40uy)), txt, 0.95
                    |Error txt  -> Brushes.Gray, txt, 0.80
                let tb = new TextBlock(Text=txt)
                tb.FontSize <- Appearance.fontSize * scale
                tb.Foreground <- color
                //tb.FontFamily <- new FontFamily ("Arial") // or use default of device
                yield tb
                ]
    
    module Xml = 
        //taken form https://github.com/fsharp/FsAutoComplete/src/FsAutoComplete.Core/TipFormatter.fs
    
        // TODO: Improve this parser. Is there any other XmlDoc parser available?
        // maybe https://stackoverflow.com/questions/2315000/parsing-xml-file-with-f-linq-to-xml
        type  XmlDocMember(doc: XmlDocument) =
          let nl = Environment.NewLine
          let readContent (node: XmlNode) =
            match node with
            | null -> null
            | _ ->
                // Many definitions contain references like <paramref name="keyName" /> or <see cref="T:System.IO.IOException">
                // Replace them by the attribute content (keyName and System.IO.Exception in the samples above)
                // Put content in single quotes for possible formatting improvements on editor side.
                Regex.Replace(node.InnerXml,"""<\w+ \w+="(?:\w:){0,1}(.+?)" />""", "`$1`")
          let readChildren name (doc: XmlDocument) =
            doc.DocumentElement.GetElementsByTagName name
            |> Seq.cast<XmlNode>
            |> Seq.map (fun node -> node.Attributes.[0].InnerText.Replace("T:",""), readContent node)
            |> Map.ofSeq
          let summary = readContent doc.DocumentElement.ChildNodes.[0]
          let pars = readChildren "param" doc
          let exceptions = readChildren "exception" doc
          override x.ToString() =
            summary + nl + nl +
            (pars |> Seq.map (fun kv -> "`" + kv.Key + "`" + ": " + kv.Value) |> String.concat nl) +
            (if exceptions.Count = 0 then ""
             else nl + nl + "Exceptions:" + nl +
                  (exceptions |> Seq.map (fun kv -> "\t" + "`" + kv.Key + "`" + ": " + kv.Value) |> String.concat nl))

        let rec readXmlDoc (reader: XmlReader) (acc: Map<string,XmlDocMember>) =
          let acc' =
            match reader.Read() with
            | false -> None
            | true when reader.Name = "member" && reader.NodeType = XmlNodeType.Element ->
              try
                let key = reader.GetAttribute("name")
                use subReader = reader.ReadSubtree()
                let doc = XmlDocument()
                doc.Load(subReader)
                acc |> Map.add key (XmlDocMember doc) |> Some
              with
              | _ -> Some acc
            | _ -> Some acc
          match acc' with
          | None -> acc
          | Some acc' -> readXmlDoc reader acc'

        let getXmlDoc =
          let xmlDocCache = Collections.Concurrent.ConcurrentDictionary<string, Map<string, XmlDocMember>>()
          fun dllFile ->
            let xmlFile = Path.ChangeExtension(dllFile, ".xml")
            if xmlDocCache.ContainsKey xmlFile then
              Some xmlDocCache.[xmlFile]
            else          
              let rec exists filePath tryAgain = // In Linux, we need to check for upper case extension separately
                match File.Exists filePath, tryAgain with
                | true, _ -> Some filePath
                | false, false -> None
                | false, true ->
                  let filePath = Path.ChangeExtension(filePath, Path.GetExtension(filePath).ToUpper())
                  exists filePath false

              match exists xmlFile true with
              | None -> None
              | Some actualXmlFile ->
                // Prevent other threads from tying to add the same doc simultaneously
                xmlDocCache.AddOrUpdate(xmlFile, Map.empty, fun _ _ -> Map.empty) |> ignore
                try
                  use reader = XmlReader.Create actualXmlFile
                  let xmlDoc = readXmlDoc reader Map.empty
                  xmlDocCache.AddOrUpdate(xmlFile, xmlDoc, fun _ _ -> xmlDoc) |> ignore
                  Some xmlDoc
                with _ ->
                  None  // TODO: Remove the empty map from cache to try again in the next request?
    
    
    // --------------------------------------------------------------------------------------
    // Formatting of tool-tip information displayed in F# IntelliSense
    // --------------------------------------------------------------------------------------
    
    let buildFormatComment cmt =
        match cmt with
        | FSharpXmlDoc.Text s -> Ok s //"plain text Doc: \r\n" + s
        | FSharpXmlDoc.None -> Error "*FSharpXmlDoc.None*"
        | FSharpXmlDoc.XmlDocFileSignature(dllFile, memberName) ->
           match Xml.getXmlDoc dllFile with
           | Some doc ->
                if doc.ContainsKey memberName then Ok (string doc.[memberName])
                else Error ("*member not found in docXml: "+memberName+"\r\n for "+dllFile+"\r\n")
           | _ -> Error ("*doc file not found for: "+dllFile+"\r\n")


    let formated (sdtt: FSharpStructuredToolTipText) = 
        match sdtt with
        |FSharpToolTipText(els) ->
            match els with
            |[]  -> []//{name = ""; signature= ""; xmlDocStr = Error "*FSharpStructuredToolTipElement list is empty*"}]
            |els -> 
                [ for el in els do 
                    match el with 
                    | FSharpStructuredToolTipElement.None -> () //yield {name = ""; signature= ""; xmlDocStr = Error "*FSharpStructuredToolTipElement.None*"}
                    | FSharpStructuredToolTipElement.CompositionError(text) -> yield {name = ""; signature= ""; xmlDocStr = Error ("*FSharpStructuredToolTipElement.CompositionError: "+ text)}
                    | FSharpStructuredToolTipElement.Group(layouts) -> 
                        for layout in layouts do
                            yield { name = "" //Option.defaultValue "*noParamName*" layout.ParamName
                                    signature= Layout.showL layout.MainDescription
                                    xmlDocStr = buildFormatComment layout.XmlDoc}
                ]
            

                        //|> List.map (fun layout -> Option.defaultValue "noParamName" layout.ParamName)
                       // |> List.map (fun layout ->  Layout.showL layout.MainDescription + "\r\n" + buildFormatComment layout.XmlDoc)
                        //|> List.map (fun layout -> Option.map Layout.showL layout.Remarks |> Option.defaultValue "noRemarks")
                        //|> String.concat "\r\n-------------------------------------------------------------------\r\n" // line between overloads
                //sprintf "%s%s" tx eltxt) "" 
    
 

    // --------------------------------------------------------------------------------------
    // Type info ToolTip:
    // --------------------------------------------------------------------------------------

    let TextEditorMouseHover( e: MouseEventArgs) =
        // see https://github.com/icsharpcode/AvalonEdit/blob/master/ICSharpCode.AvalonEdit/Editing/SelectionMouseHandler.cs#L477
        if Tab.current.IsSome then
            let ed = Tab.currEditor
            let tab = Tab.currTab
            let doc = ed.Document
            let pos = ed.GetPositionFromPoint(e.GetPosition(Tab.currEditor))
            if pos.HasValue && tab.FsCheckerResult.IsSome then                            
                let line = pos.Value.Line            
                
                //TODO check for in strimng to give #r tooltip
                //TODO find word boundary yourself
                
                let offset = doc.GetOffset(pos.Value.Location)
                let startOffset = TextUtilities.GetNextCaretPosition(doc,offset, LogicalDirection.Backward, CaretPositioningMode.WordBorderOrSymbol)// TODO fails on ´´ backtick names
                let endOffset = TextUtilities.GetNextCaretPosition(doc,offset, LogicalDirection.Forward, CaretPositioningMode.WordBorderOrSymbol)// returns -1 on empty lines; TODO fails on ´´ backtick names
                if startOffset < endOffset then // to skip empty lines
                    tab.TypeInfoToolTip.Content <- "*loading type info ..."
                    tab.TypeInfoToolTip.PlacementTarget <- ed // required for property inheritance
                    tab.TypeInfoToolTip.IsOpen <- true            
                    //e.Handled <- true // HACK. don't se handeled! so that on type errors the  Error tooltip still gets shown after this tooltip

                    let docLine = doc.GetLineByOffset(offset)
                    let endCol = endOffset - docLine.Offset
                    let lineTxt = doc.GetText(docLine)          
                    let word = doc.GetText(max 0 startOffset, endOffset-startOffset) // max function to avoid -1
                    //Log.printf "word = '%s' Line:%d starting at %d get from %d to %d: in '%s'" word line docLine.Offset startOffset endOffset lineTxt
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
                        let! stt = tab.FsCheckerResult.Value.GetStructuredToolTipText(line,endCol,lineTxt,[word],FSharpTokenTag.Identifier)
                        let ttds = formated stt
                                                
                        do! Async.SwitchToContext Sync.syncContext
                        if List.isEmpty ttds then
                            tab.TypeInfoToolTip.IsOpen <- false
                        else                            
                            let ttPanel = stackPanel None ttds
                            if tab.TypeInfoToolTip.IsOpen then 
                                tab.TypeInfoToolTip.Content <- ttPanel
                        } |> Async.Start //TODO: add Cancellation ?
          
                

    let TextEditorMouseHoverStopped( e: MouseEventArgs) = 
        if Tab.current.IsSome then 
            Tab.currTab.TypeInfoToolTip.IsOpen <- false