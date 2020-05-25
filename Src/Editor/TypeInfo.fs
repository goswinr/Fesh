namespace Seff.Editor

open Seff
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
open ICSharpCode.AvalonEdit
open FSharp.Compiler
open FSharp.Compiler.SourceCodeServices
open System.Windows.Input
open System.Windows.Documents
open System.Collections.Generic

type ToolTipData = {name:string; signature:string; xmlDocStr: Result<string*string,string>}


///a static class for creating tooltips 
type TypeInfo private () = 
        
    static let loadingTxt =  "Loading type info ..."

    // make a fancy tooltip:
    static let stackPanel  (it:FSharpDeclarationListItem option, tds:ToolTipData list) = 
        let makePanelVert (xs:list<#UIElement>) =
            let p = new StackPanel(Orientation= Orientation.Vertical)
            for x in xs do p.Children.Add x |> ignore
            p
        
        let mutable assemblies = new HashSet<string>()
        let stackPanel = makePanelVert [
            if it.IsSome then                 
                let tb = new TextBlock(Text = sprintf "%A" it.Value.Glyph)
                tb.Foreground <- Brushes.DarkOrange
                tb.FontSize <- Style.fontSize  * 0.85
                tb.FontFamily <- Style.fontEditor
                //tb.FontWeight <- FontWeights.Bold
                yield tb :> UIElement
            
                //let tb = new TextBlock(Text= sprintf "Kind:%A" it.Value.Kind)
            
            for td in tds do
                let subPanel = makePanelVert [
                    if td.name <> "" then 
                        let tb = new TextBlock(Text= "Name:" + td.name)
                        tb.Foreground <- Brushes.Black
                        tb.FontSize <- Style.fontSize * 0.9
                        //tb.FontFamily <- Style.elronet
                        tb.FontWeight <- FontWeights.Bold
                        yield tb 
                    if td.signature <> "" then 
                        let tb = new TextBlock(Text = td.signature)
                        tb.Foreground <- Brushes.Black
                        tb.FontSize <- Style.fontSize  * 1.0
                        tb.FontFamily <- Style.fontEditor
                        yield tb
                
                    let color, txt, scale  = 
                        match td.xmlDocStr with 
                        |Ok (txt,ass)     -> 
                            if ass <>"" then assemblies.Add(ass) |> ignore //could it be from more than one assembly? because of type extensions?
                            Brushes.DarkBlue, txt, 0.9 
                        |Error errTxt  -> 
                            Brushes.Gray, errTxt, 0.75
                    let tb = new TextBlock(Text= txt.Trim() )
                    tb.FontSize <- Style.fontSize  * scale
                    tb.FontFamily <- Style.fontToolTip
                    tb.Foreground <- color                    
                    yield tb ]

                let border = Border()
                border.Child <- subPanel
                border.BorderThickness <- Thickness(1.0)
                border.BorderBrush <- Brushes.LightGray
                border.Padding <- Thickness(4.0)
                border.Margin <- Thickness(2.0)
                yield border :> UIElement
            
            if assemblies.Count > 0 then 
                let tb = 
                    if assemblies.Count = 1 then new TextBlock(Text= "assembly:\r\n" + Seq.head assemblies)
                    else                         new TextBlock(Text= "assemblies:\r\n" + String.concat "\r\n" assemblies)
                tb.FontSize <- Style.fontSize  * 0.80
                tb.Foreground <- Brushes.Black
                //tb.FontFamily <- new FontFamily ("Arial") // or use default of device
                yield tb :> UIElement
                ]
        ScrollViewer(Content=stackPanel , VerticalScrollBarVisibility = ScrollBarVisibility.Auto ) //TODO cant be scrolled, never gets focus? because completion window keeps focus on editor
    
  
    // --------------------------------------------------------------------------------------
    // Seff Formatting of tool-tip information displayed in F# IntelliSense
    // --------------------------------------------------------------------------------------
    
    static let buildFormatComment (cmt:FSharpXmlDoc) =
        match cmt with
        | FSharpXmlDoc.Text s -> Ok (s,"") // "plain text Doc: \r\n" + s
        | FSharpXmlDoc.None -> Error "*FSharpXmlDoc.None*"
        | FSharpXmlDoc.XmlDocFileSignature(dllFile, memberName) ->
           match DocString.getXmlDoc dllFile with
           | Some doc ->
                if doc.ContainsKey memberName then 
                    let docText = doc.[memberName].ToEnhancedString()
                    //let docText =  doc.[memberName].ToString()
                    Ok (docText  , dllFile)
                else 
                    let xmlf = Path.ChangeExtension(dllFile, ".xml")
                    let err = "no xml doc found for member'"+memberName+"' in \r\n"+xmlf+"\r\n"
                    //log.PrintDebugMsg "%s" err                    
                    Error (err)
           | None -> 
                Error ("xml doc file not found for: "+dllFile+"\r\n")


    static let addQuestionmark (optArgs:ResizeArray<string>) (txt:string) = 
        let mutable res = txt
        for arg in optArgs do 
            res <- res.Replace(" "+arg+":", " ?"+arg+":")
        res

    static let formated (sdtt: FSharpStructuredToolTipText, optArgs:ResizeArray<string>) = 
        match sdtt with
        |FSharpToolTipText(els) ->
            match els with
            |[]  -> [] //{name = ""; signature= ""; xmlDocStr = Error "*FSharpStructuredToolTipElement list is empty*"}]
            |els -> 
                [ for el in els do 
                    match el with 
                    | FSharpStructuredToolTipElement.None ->                    yield {name = ""; signature= ""; xmlDocStr = Error  "*FSharpStructuredToolTipElement.None*"}
                    | FSharpStructuredToolTipElement.CompositionError(text) ->  yield {name = ""; signature= ""; xmlDocStr = Error ("*FSharpStructuredToolTipElement.CompositionError: "+ text)}
                    | FSharpStructuredToolTipElement.Group(layouts) -> 
                        for layout in layouts do 
                            yield { name =     Option.defaultValue "" layout.ParamName
                                    signature= Layout.showL layout.MainDescription   |> addQuestionmark optArgs                                 
                                    xmlDocStr = buildFormatComment layout.XmlDoc}
                ]
     
    // --------------------------------------------------------------------------------------
    // Seff Type info ToolTip:
    // --------------------------------------------------------------------------------------

    ///returns the names of optional Arguments in a given method call
    static let namesOfOptnlArgs(fsu:FSharpSymbolUse,log:ISeffLog)=
        let D = ResizeArray<string>(0)               
        try
            match fsu.Symbol with
            | :? FSharpMemberOrFunctionOrValue as x ->
                for cs in x.CurriedParameterGroups do
                    for c in cs do 
                        if c.IsOptionalArg then                         
                            D.Add c.FullName
                            //log.PrintDebugMsg "optional full name: %s" c.FullName
            | _ -> ()
        with e -> log.PrintAppErrorMsg "Error in TypeInfo.namesOfOptnlArgs: %A" e
        D
    
    //--------------public values and functions -----------------
    
    static member loadingText = loadingTxt
    
    static member getFormated (sdtt: FSharpStructuredToolTipText, optArgs:ResizeArray<string>) = formated (sdtt, optArgs) 
    
    static member getPanel  (it:FSharpDeclarationListItem option, tds:ToolTipData list) = stackPanel (it, tds)

    static member namesOfOptionalArgs(fsu:FSharpSymbolUse,log:ISeffLog) = namesOfOptnlArgs(fsu,log)

    static member mouseHover(e: MouseEventArgs, iEditor:IEditor, log:ISeffLog, tip:ToolTip) =
        // see https://github.com/icsharpcode/AvalonEdit/blob/master/ICSharpCode.AvalonEdit/Editing/SelectionMouseHandler.cs#L477
                
        let doc = iEditor.AvaEdit.Document
        let pos = iEditor.AvaEdit.GetPositionFromPoint(e.GetPosition(iEditor.AvaEdit))
        if pos.HasValue then
            match iEditor.CheckState with
            | Running _ |Failed | NotStarted -> ()
            | Done res -> 
                let line = pos.Value.Line            
                
                //TODO check for in string to give #r tooltip
                //TODO fails on ´´ backtick names
                //TODO test using Fsharp instead for finding words:  let partialLongName = QuickParse.GetPartialLongNameEx(pos.lineToCaret, colSetBack - 1)
                
                let offset = doc.GetOffset(pos.Value.Location)
                let startOffset = TextUtilities.GetNextCaretPosition(doc,offset, LogicalDirection.Backward, CaretPositioningMode.WordBorderOrSymbol)// TODO fails on ´´ backtick names
                let endOffset =   TextUtilities.GetNextCaretPosition(doc,offset, LogicalDirection.Forward,  CaretPositioningMode.WordBorderOrSymbol)// returns -1 on empty lines; 
                if startOffset < endOffset then // to skip empty lines
                    let docLine = doc.GetLineByOffset(offset)
                    let endCol = endOffset - docLine.Offset
                    let lineTxt = doc.GetText(docLine)          
                    let word = doc.GetText(max 0 startOffset, endOffset-startOffset) // max function to avoid -1
                    //log.PrintDebugMsg "word = '%s' Line:%d starting at %d get from %d to %d: in '%s'" word line docLine.Offset startOffset endOffset lineTxt
                
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

                        let! stt =    res.checkRes.GetStructuredToolTipText(line,endCol,lineTxt,[word],FSharpTokenTag.Identifier)       //TODO, can this be avoided use info from below symbol call ? // TODO move into checker
                        let! symbls = res.checkRes.GetSymbolUseAtLocation(  line,endCol,lineTxt,[word])                                 //only get to info about oprional paramters
                        let optArgs = if symbls.IsSome then namesOfOptnlArgs(symbls.Value,log) else ResizeArray(0) 
                        
                        do! Async.SwitchToContext Sync.syncContext
                    

                        let ttds = formated (stt, optArgs)
                        if List.isEmpty ttds then
                            let w= word.Trim()
                            if w <> "" then     tip.Content <- "No type info found for:\r\n" + word
                            else                tip.Content <- "No tip"
                            //ed.TypeInfoToolTip.IsOpen <- false
                        else                            
                            let ttPanel = stackPanel (None , ttds)
                            if tip.IsOpen then 
                                // TODO hide tooltip and use use popup instead now, so it can be pinned?
                                tip.Content <- ttPanel
                        } |> Async.StartImmediate //TODO: add Cancellation ?
    
               //e.Handled <- true //  don't set handeled! so that on type errors the  Error tooltip still gets shown after this tooltip      


