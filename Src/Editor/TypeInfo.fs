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
open Seff.Model


type OptDefArg   = {name:string } //; defVal:string}//  default value seems to be not available via FCS see below in: namesOfOptnlArgs(fsu:FSharpSymbolUse)

type ToolTipData = {name:string; signature:TaggedText[]; optDefs: ResizeArray<OptDefArg>;  xmlDocStr: Result<string*string,string>}


///a static class for creating tooltips 
type TypeInfo private () = 
        
    static let loadingTxt =  "Loading type info ..."

    static let gray         = Brushes.Gray                       |> freeze
    static let lightgray    = Brushes.Gray       |> brighter 100 |> freeze
    static let blue         = Brushes.Blue       |> darker    90 |> freeze
    static let darkblue     = Brushes.Blue       |> darker   150 |> freeze
    static let purple       = Brushes.Purple     |> brighter  40 |> freeze
    static let black        = Brushes.Black                      |> freeze
    static let red          = Brushes.DarkSalmon |> darker   120 |> freeze
    static let fullred      = Brushes.Red        |> darker    60 |> freeze
    static let cyan         = Brushes.DarkCyan   |> darker    60 |> freeze

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
                |"?" ->  tb.Inlines.Add( new Run (t.Text , Foreground = gray )) // sometimes optional arguments have already a question mark but not always
                | _ -> 
                    match td.optDefs |> Seq.tryFind ( fun oa -> oa.name = t.Text ) with 
                    | Some od ->  tb.Inlines.Add( new Run ("?"+t.Text , Foreground = gray )) 
                    | None    ->  tb.Inlines.Add( new Run (t.Text, Foreground = black )) 

            | TextTag.Keyword ->
                tb.Inlines.Add( new Run (t.Text, Foreground = blue )) 

            | TextTag.Operator -> tb.Inlines.Add( new Run (t.Text, Foreground = Brushes.Green ))
            | TextTag.Punctuation->
                match t.Text with 
                | "?" ->         tb.Inlines.Add( new Run (t.Text, Foreground = gray))   
                | "*" 
                | "->" -> 
                    if len > maxCharInSignLine then 
                        tb.Inlines.Add( new Run ("\r\n    "))
                        len <- 0
                    tb.Inlines.Add( new Run (t.Text, Foreground = fullred))//, FontWeight = FontWeights.Bold ))   
                |  _  ->  
                    tb.Inlines.Add( new Run (t.Text, Foreground = purple ))                  
                
            | TextTag.RecordField
            | TextTag.Method
            | TextTag.Property
            | TextTag.Field
            | TextTag.ModuleBinding
            | TextTag.UnionCase
            | TextTag.Member ->   tb.Inlines.Add( new Run (t.Text, Foreground = red ))              
                
            | TextTag.Struct
            | TextTag.Class
            | TextTag.Interface
            | TextTag.Function
            | TextTag.Alias ->   tb.Inlines.Add( new Run (t.Text, Foreground = cyan ))   
                
            | TextTag.TypeParameter ->   tb.Inlines.Add( new Run (t.Text, Foreground = cyan ))   // generative argument like 'T or 'a

            | TextTag.UnknownType
            | TextTag.UnknownEntity ->   tb.Inlines.Add( new Run (t.Text, Foreground = gray ))  

            | TextTag.LineBreak ->
                len <- t.Text.Length // reset after line berak
                tb.Inlines.Add( new Run (t.Text))

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
            | TextTag.Space
            | TextTag.StringLiteral
            | TextTag.Text
            | TextTag.UnknownType
            | TextTag.UnknownEntity ->    tb.Inlines.Add( new Run (t.Text))

        (* 
        let debugHelp = 
            td.signature
            |> Seq.filter (fun t -> t.Tag <> TextTag.Punctuation && t.Tag <> TextTag.Space && t.Tag <> TextTag.Operator && t.Tag <> TextTag.LineBreak)
            |> Seq.map(fun t -> sprintf "%A" t.Tag)
            |> String.concat "|"
        tb.Inlines.Add( new Run ("\r\n"+debugHelp,Foreground = lightgray))    
        *)
        tb
    
    /// for <c> and </c> in text
    static let markInlineCode(tx:string) : TextBlockSelectable =
        let tb = new TextBlockSelectable()
        tb.FontSize   <- Style.fontSize  * 0.90
        tb.FontFamily <- Style.fontToolTip
        tb.Foreground <- darkblue 
        let rec loop i = 
            if i < tx.Length then 
                match tx.IndexOf("<c>",i) with 
                | -1 -> 
                    tb.Inlines.Add( new Run(tx.Substring(i)))  // add til end, exit recursion
                | s -> 
                    match tx.IndexOf("</c>",s) with 
                    | -1 -> 
                        tb.Inlines.Add( new Run(tx.Substring(i)))// start found but not end , just add til end, exit recursion                        
                    | e -> 
                        tb.Inlines.Add( new Run(tx.Substring(i, s-i)))
                        tb.Inlines.Add( new Run(tx.Substring(s+3, e-s-3), 
                                                FontFamily = Style.fontEditor, 
                                                Foreground = black, 
                                                //FontWeight = FontWeights.Bold, 
                                                Background = lightgray
                                                ))
                        loop(e+4)
        loop 0
        tb


    // make a fancy tooltip panel:
    static let makeStackPanel  (it:DeclarationListItem option, tds:ToolTipData list,addPersistInfo:bool) = 
        let makePanelVert (xs:list<#UIElement>) =
            let p = new StackPanel(Orientation= Orientation.Vertical)
            for x in xs do p.Children.Add x |> ignore
            p
        
        let mutable assemblies = new HashSet<string>()
        let stackPanel = makePanelVert [
            if addPersistInfo then yield TextBlock(Text = "Press Ctrl + P to persist this window.", FontSize = Style.fontSize * 0.7) :> UIElement
            if it.IsSome then                 
                let tb = new TextBlockSelectable(Text = sprintf "%A" it.Value.Glyph)
                tb.Foreground <- Brushes.DarkOrange
                tb.FontSize <- Style.fontSize  * 0.85
                tb.FontFamily <- Style.fontEditor
                //tb.FontWeight <- FontWeights.Bold
                yield tb :> UIElement
            
                //let tb = new TextBlock(Text= sprintf "Kind:%A" it.Value.Kind)
            
            let deDup = HashSet() // just because some typ provider signatures apears mutiple times, filter them out with hashset
            for td in tds do
                let sign = td.signature |> Seq.map (fun tt -> tt.Text)  |> String.Concat
                if not <| deDup.Contains(sign) then // just because some typ provider signatures apeears mutiple times, filter them out with hashset
                    deDup.Add sign  |> ignore
                    
                    let border = Border()
                    border.Child <- makePanelVert [
                    
                        if td.name <> "" then 
                            let tb = new TextBlockSelectable(Text= "Name: " + td.name)
                            tb.Foreground <- black
                            tb.FontSize <- Style.fontSize * 0.9
                            //tb.FontFamily <- Style.elronet
                            tb.FontWeight <- FontWeights.Bold
                            yield tb 
                    
                        yield coloredSignature(td) // the main signature of a F# value
                
                        match td.xmlDocStr with 
                        |Ok (txt,ass)     -> 
                            if ass <>"" then assemblies.Add(ass) |> ignore //TODO could it be from more than one assembly? because of type extensions?
                            yield markInlineCode(txt)
                        |Error errTxt  -> 
                            yield TextBlockSelectable(Text = errTxt,FontSize = Style.fontSize  * 0.7,FontFamily = Style.fontToolTip, Foreground = gray )
                        ]
                    border.BorderThickness <- Thickness(1.0)
                    border.BorderBrush <- Brushes.LightGray
                    border.Padding <- Thickness(4.0)
                    border.Margin <- Thickness(2.0)
                    yield border :> UIElement
            
            if assemblies.Count > 0 then 
                let tb = 
                    if assemblies.Count = 1 then new TextBlockSelectable(Text= "assembly:\r\n" + Seq.head assemblies)
                    else                         new TextBlockSelectable(Text= "assemblies:\r\n" + String.concat "\r\n" assemblies)
                tb.FontSize <- Style.fontSize  * 0.80
                tb.Foreground <-black
                //tb.FontFamily <- new FontFamily ("Arial") // or use default of device
                yield tb :> UIElement
                ]
        ScrollViewer(Content=stackPanel , VerticalScrollBarVisibility = ScrollBarVisibility.Auto ) //TODO cant be scrolled, never gets focus? because completion window keeps focus on editor
    
  
    // --------------------------------------------------------------------------------------
    // Seff Formatting of tool-tip information displayed in F# IntelliSense
    // --------------------------------------------------------------------------------------
    
    
    static let unEscapeXml(txt:string) = // TODO dont do it like this ! use proper xml doc  parsing 
         txt.Replace("&lt;"   ,"<" )
            .Replace("&gt;"   ,">" )
            .Replace("&quot;" ,"\"")
            .Replace("&apos;" ,"'" )
            .Replace("&amp;"  ,"&" )  
    
    static let stripOffXmlComments(txt:string) =    // TODO dont do it like this ! use proper xml doc parsing 
         //printfn "%s" txt
         txt.Replace("<summary>"  , "" )
            .Replace("</summary>" , "" )
            .Replace("<remarks>"  , "Remarks: " )
            .Replace("</remarks>" , "" )
            .Replace("<category>" , "Category: " )
            .Replace("</category>", "" )
            .Replace("<returns>"  , "Returns:\r\n" )
            .Replace("</returns>" , "" )
            .Replace("<param name=\""   ,"    • " )
            .Replace("</param>"   , "" )            
            .Replace("<para>"     , "\r\n" )
            .Replace("</para>"   , "" )
            .Replace("<value>"    , "value:" )
            .Replace("</value>"   , "" )
            .Replace("<exception cref=\"T:" ,"Exception: " ) 
            .Replace("<exception cref=\"" ,"Exception: " ) 
            .Replace("</exception>" ,"" ) 
            .Replace("<see langword=\"","'")
            .Replace("<see cref=\"P:","'")
            .Replace("<see cref=\"T:","'")
            .Replace("<see cref=\"","'")
            .Replace("<a href=\"","'")
            .Replace("</a>","'")
            .Replace("\" />","'")
            .Replace("\">"  ,": " ) // to catch the end of <param name="value">  and other closings
        |> unEscapeXml


    
    /// returns docstring und dll path
    static let buildFormatComment (cmt:FSharpXmlDoc) =
        //mostly copied from same named function in Docstring.fs
        match cmt with
        | FSharpXmlDoc.FromXmlText xmlDoc -> 
            // Doc string that is not from an xml file but from the current .fsx document 
            let s = 
                xmlDoc.UnprocessedLines 
                |> String.concat Environment.NewLine 
                |> stripOffXmlComments // TODO this might need striping off more tags than <summary>
                |> Util.Str.trim            
            Ok (s,"") 
        | FSharpXmlDoc.None -> Error "*FSharpXmlDoc.None*"
        | FSharpXmlDoc.FromXmlFile(dllFile, memberName) ->
           match DocString.getXmlDoc dllFile with
           | Some doc ->
                if doc.ContainsKey memberName then
                    let docText = doc.[memberName].ToFullEnhancedString() 
                    let unEscDocText = unEscapeXml docText
                    Ok (unEscDocText  , dllFile)
                else 
                    let xmlf = Path.ChangeExtension(dllFile, ".xml")
                    let err = "no xml doc found for member "+memberName+" in \r\n"+xmlf+"\r\n"
                    //log.PrintfnDebugMsg "%s" err                    
                    Error (err)
           | None -> 
                Error ("xml doc file not found for: "+dllFile+"\r\n")
           

    static let getToolTipDatas (sdtt: ToolTipText, optDfes:ResizeArray<OptDefArg>) : ToolTipData list= 
        match sdtt with
        | ToolTipText.ToolTipText (els) ->
            match els with
            |[]  -> [] //{name = ""; signature= ""; xmlDocStr = Error "*FSharpStructuredToolTipElement list is empty*"}]
            |els -> 
                [ for el in els do 
                    match el with 
                    | ToolTipElement.None ->                    
                        yield {name = ""; signature = [||]; optDefs=optDfes; xmlDocStr = Error  "*FSharpStructuredToolTipElement.None*"}

                    | ToolTipElement.CompositionError(text) ->  
                        yield {name = ""; signature = [||]; optDefs=optDfes; xmlDocStr = Error ("*FSharpStructuredToolTipElement.CompositionError: "+ text)}

                    | ToolTipElement.Group(tooTipElsData) -> 
                        for tted in tooTipElsData do
                            yield { name      = Option.defaultValue "" tted.ParamName
                                    signature = tted.MainDescription
                                    optDefs   = optDfes
                                    xmlDocStr = buildFormatComment tted.XmlDoc}
                ]
    
 
    ///returns the names of optional Arguments in a given method call
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
        with e -> () //ISeffLog.log.PrintfnAppErrorMsg "Error while trying to show a Tool tip in Seff.\r\nYou can ignore this error.\r\nin TypeInfo.namesOfOptnlArgs: %A" e
        optDefs

    static let mutable cachedDeclarationListItem:DeclarationListItem option = None
    static let mutable cachedToolTipData: list<ToolTipData> = []

    //--------------public values and functions -----------------
    
    static member loadingText = loadingTxt
    
    static member namesOfOptionalArgs(fsu:FSharpSymbolUse) = namesOfOptnlArgs(fsu)

    static member getToolTipDataList (sdtt: ToolTipText, optArgs:ResizeArray<OptDefArg>) = getToolTipDatas (sdtt, optArgs) 
    
    static member getPanel  (it:DeclarationListItem option, tds:ToolTipData list) = 
        cachedDeclarationListItem <- it
        cachedToolTipData <- tds
        makeStackPanel (it, tds, true)
    
    /// regenerates a view of the last created panel so it can be used again in the popout window
    static member getPanelCached () =
        makeStackPanel (cachedDeclarationListItem, cachedToolTipData, false)

   
    static member mouseHover(e: MouseEventArgs, iEditor:IEditor, log:ISeffLog, tip:ToolTip) =
        // see https://github.com/icsharpcode/AvalonEdit/blob/master/AvalonEditB/Editing/SelectionMouseHandler.cs#L477
                
        let doc = iEditor.AvaEdit.Document
        let pos = iEditor.AvaEdit.GetPositionFromPoint(e.GetPosition(iEditor.AvaEdit))
        if pos.HasValue then
            match iEditor.FileCheckState with
            | GettingCode _ | Checking _ |Failed | NotStarted -> ()
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
                    

                        let ttds = getToolTipDatas (ttt, optArgs)
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


