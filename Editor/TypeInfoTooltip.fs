﻿namespace Seff.Editor

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
open FSharp.Compiler
open FSharp.Compiler.SourceCodeServices
open System.Windows.Input
open System.Windows.Documents
open System.Collections.Generic


module Tooltips = 
    
    type ToolTipData = {name:string; signature:string; xmlDocStr: Result<string*string,string>}

    let private makePanelVert (xs:list<#UIElement>) =
        let p = new StackPanel(Orientation= Orientation.Vertical)
        for x in xs do p.Children.Add x |> ignore
        p

    // make a fancy tooltip:
    let stackPanel  (it:FSharpDeclarationListItem option, tds:ToolTipData list, fontSize) = 
        let mutable assemblies = new HashSet<string>()
        let stackPanel = makePanelVert [
            if it.IsSome then 
                let glyph = sprintf "%A" it.Value.Glyph
                let tb = new TextBlock(Text = glyph)
                tb.Foreground <- Brushes.DarkOrange
                tb.FontSize <- fontSize * 1.0
                tb.FontFamily <- Appearance.font
                tb.FontWeight <- FontWeights.Bold
                yield tb :> UIElement
            
                //let tb = new TextBlock(Text= sprintf "Kind:%A" it.Value.Kind)
            
            for td in tds do
                let subPanel = makePanelVert [
                    if td.name <> "" then 
                        let tb = new TextBlock(Text= "Name:" + td.name)
                        tb.Foreground <- Brushes.Black
                        tb.FontSize <- fontSize * 0.9
                        tb.FontFamily <- Appearance.font
                        tb.FontWeight <- FontWeights.Bold
                        yield tb 
                    if td.signature <> "" then 
                        let tb = new TextBlock(Text = td.signature)
                        tb.Foreground <- Brushes.Black
                        tb.FontSize <- fontSize * 1.2
                        tb.FontFamily <- Appearance.font
                        yield tb
                
                    let color, txt, scale  = 
                        match td.xmlDocStr with 
                        |Ok (txt,ass)     -> 
                            if ass <>"" then assemblies.Add(ass) |> ignore //could it be from more than one assembly? because of type extensions?
                            Brushes.DarkBlue, txt, 1.0  
                        |Error errTxt  -> 
                            Brushes.Gray, errTxt, 0.75
                    let tb = new TextBlock(Text= txt.Trim() )
                    tb.FontSize <- fontSize * scale
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
                tb.FontSize <- fontSize * 0.80
                tb.Foreground <- Brushes.Black
                //tb.FontFamily <- new FontFamily ("Arial") // or use default of device
                yield tb :> UIElement
                ]
        ScrollViewer(Content=stackPanel)
    
  
    // --------------------------------------------------------------------------------------
    // Seff Formatting of tool-tip information displayed in F# IntelliSense
    // --------------------------------------------------------------------------------------
    
    let buildFormatComment (cmt:FSharpXmlDoc) =
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
                    //log.Print "%s" err                    
                    Error (err)
           | None -> 
                Error ("xml doc file not found for: "+dllFile+"\r\n")



    let addQuestionmark (optArgs:ResizeArray<string>) (txt:string) = 
        let mutable res = txt
        for arg in optArgs do 
            res <- res.Replace(" "+arg+":", " ?"+arg+":")
        res

    let formated (sdtt: FSharpStructuredToolTipText, optArgs:ResizeArray<string>) = 
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

    let infoAboutOptinals(fsu:FSharpSymbolUse)=
        let D = ResizeArray<string>(0)               
        match fsu.Symbol with
        | :? FSharpMemberOrFunctionOrValue as x ->
            for cs in x.CurriedParameterGroups do
                for c in cs do 
                    if c.IsOptionalArg then                         
                        D.Add c.FullName
                        //log.Print "optional full name: %s" c.FullName
        | _ -> ()
        D
    

    let loadingText =  "Loading type info ..."

    let TextEditorMouseHover(ed:Editor, e: MouseEventArgs) =
        // see https://github.com/icsharpcode/AvalonEdit/blob/master/ICSharpCode.AvalonEdit/Editing/SelectionMouseHandler.cs#L477
        
        
        let aved = ed.AvaEdit        
        let doc = aved.Document
        let pos = aved.GetPositionFromPoint(e.GetPosition(aved))
        if pos.HasValue && ed.CheckerResult.IsSome then                            
            let line = pos.Value.Line            
                
            //TODO check for in string to give #r tooltip
            //TODO find word boundary yourself
                
            let offset = doc.GetOffset(pos.Value.Location)
            let startOffset = TextUtilities.GetNextCaretPosition(doc,offset, LogicalDirection.Backward, CaretPositioningMode.WordBorderOrSymbol)// TODO fails on ´´ backtick names
            let endOffset = TextUtilities.GetNextCaretPosition(doc,offset, LogicalDirection.Forward, CaretPositioningMode.WordBorderOrSymbol)// returns -1 on empty lines; TODO fails on ´´ backtick names
            if startOffset < endOffset then // to skip empty lines
                ed.TypeInfoToolTip.Content <- loadingText
                ed.TypeInfoToolTip.PlacementTarget <- aved // required for property inheritance
                ed.TypeInfoToolTip.IsOpen <- true 
                ed.TypeInfoToolTip.StaysOpen <- true
                    
                //e.Handled <- true // HACK. don't set handeled! so that on type errors the  Error tooltip still gets shown after this tooltip

                let docLine = doc.GetLineByOffset(offset)
                let endCol = endOffset - docLine.Offset
                let lineTxt = doc.GetText(docLine)          
                let word = doc.GetText(max 0 startOffset, endOffset-startOffset) // max function to avoid -1
                //log.Print "word = '%s' Line:%d starting at %d get from %d to %d: in '%s'" word line docLine.Offset startOffset endOffset lineTxt
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

                    let! stt =   ed.FsCheckerResult.Value.GetStructuredToolTipText(line,endCol,lineTxt,[word],FSharpTokenTag.Identifier)     //TODO, can this be avoided use info from below symbol call ?
                    let! symbls = ed.FsCheckerResult.Value.GetSymbolUseAtLocation(line,endCol,lineTxt,[word]) //only get to info about oprional paramters
                    let defArgs = if symbls.IsSome then infoAboutOptinals(symbls.Value) else ResizeArray(0) 
                        
                    do! Async.SwitchToContext Sync.syncContext

                    let ttds = formated (stt, defArgs)
                    if List.isEmpty ttds then
                        ed.TypeInfoToolTip.Content <- "No type info found."
                        //ed.TypeInfoToolTip.IsOpen <- false
                    else                            
                        let ttPanel = stackPanel (None , ttds, ed.AvaEdit.FontSize )
                        if ed.TypeInfoToolTip.IsOpen then 
                            // TODO hide tooltip and use use popup instead now, so it can be pinned?
                            ed.TypeInfoToolTip.Content <- ttPanel
                    } |> Async.StartImmediate //TODO: add Cancellation ?
    
    let TextEditorMouseHoverStopped( ed:Editor, e: MouseEventArgs) = 
            ed.TypeInfoToolTip.IsOpen <- false