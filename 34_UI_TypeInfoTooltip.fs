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

    type ToolTipData = {name:string; signature:string; xmlDocStr: Result<string*string,string>}

    // make a fancy tooltip:
    let stackPanel  (it:FSharpDeclarationListItem option) (tds:ToolTipData list) = 
        let mutable assembly = ""
        makePanelVert [
            if it.IsSome then 
                let glyph = sprintf "%A" it.Value.Glyph
                let tb = new TextBlock(Text = glyph)
                tb.Foreground <- Brushes.DarkOrange
                tb.FontSize <- Appearance.fontSize * 1.0
                tb.FontFamily <- Appearance.defaultFont
                tb.FontWeight <- FontWeights.Bold
                yield tb :> UIElement
            
                //let tb = new TextBlock(Text= sprintf "Kind:%A" it.Value.Kind)
            
            for td in tds do
                let subPanel = makePanelVert [
                    if td.name <> "" then 
                        let tb = new TextBlock(Text= "Name:" + td.name)
                        tb.Foreground <- Brushes.Black
                        tb.FontSize <- Appearance.fontSize * 1.2
                        tb.FontFamily <- Appearance.defaultFont
                        tb.FontWeight <- FontWeights.Bold
                        yield tb 
                    if td.signature <> "" then 
                        let tb = new TextBlock(Text = td.signature)
                        tb.Foreground <- Brushes.Black
                        tb.FontSize <- Appearance.fontSize * 1.2
                        tb.FontFamily <- Appearance.defaultFont
                        yield tb
                
                    let color, txt,scale  = 
                        match td.xmlDocStr with 
                        |Ok (txt,ass)     -> 
                            if ass <>"" then assembly <- ass
                            Brushes.DarkBlue, txt, 1.1  //SolidColorBrush(Color.FromRgb(40uy,40uy,40uy))
                        |Error errTxt  -> 
                            Brushes.Gray, errTxt, 0.75
                    let tb = new TextBlock(Text= txt.Trim() )
                    tb.FontSize <- Appearance.fontSize * scale
                    tb.Foreground <- color
                    //tb.FontFamily <- new FontFamily ("Arial") // or use default of device
                    yield tb ]
                let border = Border()
                border.Child <- subPanel
                border.BorderThickness <- Thickness(1.0)
                border.BorderBrush <- Brushes.LightGray
                border.Padding <- Thickness(4.0)
                border.Margin <- Thickness(2.0)
                yield border :> UIElement
            
            if assembly<>"" then 
                let tb = new TextBlock(Text= "assembly:\r\n"+assembly)
                tb.FontSize <- Appearance.fontSize * 0.85
                tb.Foreground <- Brushes.Black
                //tb.FontFamily <- new FontFamily ("Arial") // or use default of device
                yield tb :> UIElement
                ]
    
  
    // --------------------------------------------------------------------------------------
    // Seff Formatting of tool-tip information displayed in F# IntelliSense
    // --------------------------------------------------------------------------------------
    
    let buildFormatComment (cmt:FSharpXmlDoc) =
        match cmt with
        | FSharpXmlDoc.Text s -> Ok (s,"") // "plain text Doc: \r\n" + s
        | FSharpXmlDoc.None -> Error "*FSharpXmlDoc.None*"
        | FSharpXmlDoc.XmlDocFileSignature(dllFile, memberName) ->
           match XmlToolTipFormatter.getXmlDoc dllFile with
           | Some doc ->
                if doc.ContainsKey memberName then 
                    //let docText = doc.[memberName].ToEnhancedString()
                    let docText =  string doc.[memberName]
                    Ok (docText  , dllFile)
                else 
                    let xmlf = Path.ChangeExtension(dllFile, ".xml")
                    let err = "no xml doc found for member'"+memberName+"' in \r\n"+xmlf+"\r\n"
                    //Log.printf "%s" err                    
                    Error (err)
           | None -> 
                Error ("*xml doc file not found for: "+dllFile+"\r\n")



    let addQuestionmark (optArgs:ResizeArray<string>) (txt:string) = 
        let mutable res = txt
        for arg in optArgs do 
            res <- res.Replace(arg, "?"+arg)
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
                        //Log.printf "optional full name: %s" c.FullName
        | _ -> ()
        D

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
                        
                        do! Async.SwitchToThreadPool()

                        let! stt =   tab.FsCheckerResult.Value.GetStructuredToolTipText(line,endCol,lineTxt,[word],FSharpTokenTag.Identifier)     //TODO, can this be avoided use info from below symbol call ?
                        let! symbls = tab.FsCheckerResult.Value.GetSymbolUseAtLocation(line,endCol,lineTxt,[word]) //only get info about oprional paramters
                        let defArgs = if symbls.IsSome then infoAboutOptinals(symbls.Value) else ResizeArray(0) 
                        
                        do! Async.SwitchToContext Sync.syncContext

                        let ttds = formated (stt, defArgs)
                        if List.isEmpty ttds then
                            tab.TypeInfoToolTip.IsOpen <- false
                        else                            
                            let ttPanel = stackPanel None ttds
                            if tab.TypeInfoToolTip.IsOpen then 
                                tab.TypeInfoToolTip.Content <- ttPanel
                        } |> Async.StartImmediate //TODO: add Cancellation ?
          

                

    let TextEditorMouseHoverStopped( e: MouseEventArgs) = 
        if Tab.current.IsSome then 
            Tab.currTab.TypeInfoToolTip.IsOpen <- false