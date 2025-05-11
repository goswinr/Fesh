namespace Fesh.Editor

open System
open Avalonia.Media

open FSharp.Compiler
open FSharp.Compiler.EditorServices
open FSharp.Compiler.CodeAnalysis

open AvaloniaEdit
open AvaloniaEdit.Rendering
open AvaloniaLog.ImmBrush

open Fesh
open Fesh.Model
open Avalonia.Media.Immutable

// see  https://github.com/dotnet/fsharp/blob/main/src/Compiler/Service/SemanticClassification.fs


type SemActions() =

    let c_ValueType                    = Brushes.MediumOrchid  |> darker 40
    let c_ReferenceType                = Brushes.MediumVioletRed  |> darker 60
    let c_Type                         = Brushes.MediumVioletRed  |> darker 40
    let c_UnionCase                    = Brushes.LightSkyBlue  |> darker 100
    let c_UnionCaseField               = Brushes.LightSkyBlue  |> darker 100
    let c_Function                     = Brushes.DarkGoldenrod |> darker 40
    let c_Property                     = Brushes.DarkTurquoise |> darker 110
    let c_MutableVar                   = Brushes.Goldenrod     |> darker 20
    let c_MutableRecordField           = Brushes.Goldenrod     |> darker 20
    let c_Module                       = Brushes.Black
    let c_Namespace                    = Brushes.Black
    //let c_Printf                       = Brushes.Plum      // covered by xshd highlighting
    let c_ComputationExpression        = Brushes.Indigo
    let c_IntrinsicFunction            = Brushes.DarkBlue
    let c_Enumeration                  = Brushes.Indigo
    let c_Interface                    = Brushes.MediumVioletRed  |> darker 20
    let c_TypeArgument                 = Brushes.SlateBlue
    let c_Operator                     = Brushes.MediumSlateBlue
    let c_DisposableType               = Brushes.DarkOrchid
    let c_DisposableTopLevelValue      = Brushes.DarkOrchid
    let c_DisposableLocalValue         = Brushes.DarkOrchid
    let c_Method                       = Brushes.DarkTurquoise |> darker 60
    let c_ExtensionMethod              = Brushes.DarkTurquoise |> darker 30
    let c_ConstructorForReferenceType  = Brushes.Brown
    let c_ConstructorForValueType      = Brushes.SandyBrown    |> darker 80
    let c_Literal                      = Brushes.SeaGreen
    let c_RecordField                  = Brushes.DarkSlateGray |> darker 10
    let c_RecordFieldAsFunction        = Brushes.Plum
    let c_Exception                    = Brushes.HotPink |> darker 40
    let c_Field                        = Brushes.MediumPurple
    let c_Event                        = Brushes.Olive
    let c_Delegate                     = Brushes.DarkOliveGreen
    let c_NamedArgument                = Brushes.PaleVioletRed |> darker 80
    let c_Value                        = Brushes.DarkRed       |> darker 20
    let c_LocalValue                   = Brushes.DarkRed       |> darker 40
    let c_TypeDef                      = Brushes.Purple
    let c_Plaintext                    = Brushes.OrangeRed     |> darker 60

    let c_UnUsed                       = Brushes.Gray |> brighter 40

    let badIndentBrush =
        let opacity = 40uy // 0uy is transparent, 255uy is opaque, transparent to show column rulers behind
        let r,g,b = 255uy, 180uy, 0uy // orange
        Color.FromArgb(opacity,r,g,b)
        |> ImmutableSolidColorBrush


    /// this allows using the cursive version of Cascadia Mono
    // let stylisticSet1 =
    //     {new DefaultTextRunTypographyProperties() with  // TODO fix in AvaloniaEdit
    //         override this.StylisticSet1 with get() = true
    //     }

    let makeCursive (el:VisualLineElement) =
        // let f = el.TextRunProperties.Typeface.FontFamily
        // let tf = new Typeface(f, FontStyle.Italic, FontWeights.Bold, FontStretches.Normal)
        // eprintfn $"makeCursive {f}"
        // el.TextRunProperties.SetTypeface(tf)
        el.TextRunProperties.SetTypeface(StyleState.italicBoldEditorTf)
        // el.TextRunProperties.SetTypographyProperties(stylisticSet1) // for cursive set of Cascadia Mono  // TODO fix in AvaloniaEdit


    member val ReferenceTypeA              = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_ReferenceType              ))
    member val ReferenceType               = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_ReferenceType              ))
    member val ValueType                   = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_ValueType                  ))
    member val UnionCase                   = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_UnionCase                  ))
    member val UnionCaseField              = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_UnionCaseField             ))
    member val Function                    = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_Function                   ))
    member val Property                    = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_Property                   ))
    member val MutableVar                  = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_MutableVar                 ))
    member val Module                      = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_Module                     ))
    member val Namespace                   = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_Namespace                  ))
    //member val Printf                    = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_Printf                     )) // covered by xshd
    member val ComputationExpression       = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_ComputationExpression      ))
    member val IntrinsicFunction           = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_IntrinsicFunction          ))
    member val Enumeration                 = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_Enumeration                ))
    member val Interface                   = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_Interface                  ))
    member val TypeArgument                = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_TypeArgument               ))
    member val Operator                    = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_Operator                   ))
    member val DisposableType              = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_DisposableType             ))
    member val DisposableTopLevelValue     = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_DisposableTopLevelValue    ))
    member val DisposableLocalValue        = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_DisposableLocalValue       ))
    member val Method                      = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_Method                     ))
    member val ExtensionMethod             = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_ExtensionMethod            ))
    member val ConstructorForReferenceType = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_ConstructorForReferenceType))
    member val ConstructorForValueType     = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_ConstructorForValueType    ))
    member val Literal                     = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_Literal                    ))
    member val RecordField                 = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_RecordField                ))
    member val MutableRecordField          = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_MutableRecordField         ))
    member val RecordFieldAsFunction       = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_RecordFieldAsFunction      ))
    member val Exception                   = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_Exception                  ))
    member val Field                       = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_Field                      ))
    member val Event                       = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_Event                      ))
    member val Delegate                    = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_Delegate                   ))
    member val NamedArgument               = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_NamedArgument              ))
    member val Value                       = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_Value                      ))
    member val LocalValue                  = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_LocalValue                 ))//; makeCursive el
    member val Type                        = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_Type                       ))
    member val TypeDef                     = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_TypeDef                    ))
    member val Plaintext                   = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_Plaintext                  ))

    member val UnUsed                      = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c_UnUsed);el.TextRunProperties.SetTypeface(StyleState.boldEditorTf))

    member val BadIndentAction             = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(badIndentBrush))


// type alias
type Sc = SemanticClassificationType

/// A DocumentColorizingTransformer.
/// Used to do semantic highlighting
/// includes Bad Indentation and Unused declarations
type SemanticHighlighter (state: InteractionState) =

    let codeLines = state.CodeLines

    let getUnusedDecl(checkRes:FSharpCheckFileResults, id): ResizeArray<Text.range> =
        async{
            let unusedDecl = ResizeArray<Text.range>()
            let! uds = UnusedDeclarations.getUnusedDeclarations(checkRes,true)
            for ud in uds do
                unusedDecl.Add ud

            let getLine lineNo =
                match codeLines.GetLineText(lineNo,id) with
                | ValueNone -> "open"
                | ValueSome lnTxt ->  lnTxt

            let! uos = UnusedOpens.getUnusedOpens(checkRes,getLine)
            for uo in uos do
                unusedDecl.Add uo
            return unusedDecl
            }
        |> Async.RunSynchronously


    //let action (el:VisualLineElement,brush:ImmutableSolidColorBrush,r:Text.Range) = el.TextRunProperties.SetForegroundBrush(Brushes.Red)
    let defaultIndenting = state.Editor.Options.IndentationSize

    let trans = state.TransformersSemantic
    let semActs = SemActions()

    let foundSemanticsEv = new Event<int64>()

    [<CLIEvent>]
    member _.FoundSemantics = foundSemanticsEv.Publish

    /// also includes bad indenting
    member _.UpdateSemHiLiTransformers(checkRes:FSharpCheckFileResults, id) =
        if state.IsLatest id then
            let allRanges = checkRes.GetSemanticClassification(None)

            let newTrans = ResizeArray<ResizeArray<LinePartChange>>(trans.LineCount+4)

            // (1) find semantic highlight:
            let rec loopSemantic i =
                if i = allRanges.Length then
                    true // reached end
                else
                    let sem = allRanges.[i]
                    let r = sem.Range
                    let lineNo = max 1 r.StartLine
                    match codeLines.GetLine(lineNo,id) with
                    | ValueNone -> false // exit early
                    | ValueSome ln ->
                        let inline push(f,t,a)     =  LineTransformers.Insert(newTrans, lineNo,{from=f; till=t; act=a})
                        let inline pushCorr(f,t,a) =
                            if codeLines.CorrespondingId = id then // to avoid out of range exception on codeLines.FullCode
                                // because some times the range of a property starts before the point
                                // search from back to find last dot, there may be more than one
                                // at file end the end column in the reported range might be equal to FullCode.Length, so we do -1 to avoid a ArgumentOutOfRangeException.
                                let st =
                                    try
                                        match codeLines.FullCode.LastIndexOf('.', t-1, t-f) with
                                        | -1 -> f
                                        |  i -> i + 1
                                    with
                                        | _ -> eprintfn $"SemanticHighlighter: pushCorr: ArgumentOutOfRangeException: {sem.Type} {f} to {t} for {codeLines.FullCode.Length} chars"; f
                                push(st,t,a)

                        // skip semantic highlighting for these, covered in xshd:
                        let inline skipFunc(st:int, en:int)=
                            if codeLines.CorrespondingId <> id then true // to avoid out of range exception
                            else
                                let w = codeLines.FullCode.[st..en]
                                w.StartsWith    "failwith"
                                || w.StartsWith "failIfFalse" // from FsEx
                                || w.StartsWith "print"
                                || w.StartsWith "eprint"

                        let inline skipModul(st:int, en:int)=
                            if codeLines.CorrespondingId <> id then true // to avoid out of range exception
                            else
                                let w = codeLines.FullCode.[st..en]
                                w.StartsWith "Printf"

                        let st = ln.offStart + r.StartColumn
                        let en = ln.offStart + r.EndColumn
                        //IFeshLog.log.PrintfnDebugMsg $"{lineNo}:{sem.Type} {r.StartColumn} to {r.EndColumn}"

                        match sem.Type with
                        | Sc.ReferenceType               -> push(st,en, semActs.ReferenceType              )
                        | Sc.ValueType                   -> push(st,en, semActs.ValueType                  )
                        | Sc.UnionCase                   -> push(st,en, semActs.UnionCase                  )
                        | Sc.UnionCaseField              -> push(st,en, semActs.UnionCaseField             )
                        | Sc.Function                    -> if not(skipFunc(st,en)) then pushCorr(st,en, semActs.Function)
                        | Sc.Property                    -> pushCorr(st,en, semActs.Property)// correct so that a string or number literal before the dot does not get colored
                        | Sc.MutableVar                  -> push(st,en, semActs.MutableVar                 )
                        | Sc.Module                      -> if not(skipModul(st,en)) then push(st,en, semActs.Module)
                        | Sc.Namespace                   -> push(st,en, semActs.Namespace                  )
                        | Sc.ComputationExpression       -> push(st,en, semActs.ComputationExpression      )
                        | Sc.IntrinsicFunction           -> push(st,en, semActs.IntrinsicFunction          )
                        | Sc.Enumeration                 -> push(st,en, semActs.Enumeration                )
                        | Sc.Interface                   -> push(st,en, semActs.Interface                  )
                        | Sc.TypeArgument                -> push(st,en, semActs.TypeArgument               )
                        | Sc.Operator                    -> push(st,en, semActs.Operator                   )
                        | Sc.DisposableType              -> push(st,en, semActs.DisposableType             )
                        | Sc.DisposableTopLevelValue     -> push(st,en, semActs.DisposableTopLevelValue    )
                        | Sc.DisposableLocalValue        -> push(st,en, semActs.DisposableLocalValue       )
                        | Sc.Method                      -> pushCorr(st,en, semActs.Method)// correct so that a string or number literal before the dot does not get colored
                        | Sc.ExtensionMethod             -> pushCorr(st,en, semActs.ExtensionMethod)
                        | Sc.ConstructorForReferenceType -> push(st,en, semActs.ConstructorForReferenceType)
                        | Sc.ConstructorForValueType     -> push(st,en, semActs.ConstructorForValueType    )
                        | Sc.Literal                     -> push(st,en, semActs.Literal                    )
                        | Sc.RecordField                 -> push(st,en, semActs.RecordField                )
                        | Sc.MutableRecordField          -> push(st,en, semActs.MutableRecordField         )
                        | Sc.RecordFieldAsFunction       -> push(st,en, semActs.RecordFieldAsFunction      )
                        | Sc.Exception                   -> push(st,en, semActs.Exception                  )
                        | Sc.Field                       -> push(st,en, semActs.Field                      )
                        | Sc.Event                       -> push(st,en, semActs.Event                      )
                        | Sc.Delegate                    -> push(st,en, semActs.Delegate                   )
                        | Sc.NamedArgument               -> push(st,en, semActs.NamedArgument              )
                        | Sc.Value                       -> push(st,en, semActs.Value                      )
                        | Sc.LocalValue                  -> push(st,en, semActs.LocalValue                 )
                        | Sc.Type                        -> push(st,en, semActs.Type                       )
                        | Sc.TypeDef                     -> push(st,en, semActs.TypeDef                    )
                        | Sc.Plaintext                   -> push(st,en, semActs.Plaintext                  )
                        | Sc.Printf                      -> () //push(st,en, semActs.Printf                ) // covered in xshd file
                        | _ -> () // the above actually covers all SemanticClassificationTypes

                        loopSemantic (i+1)

            // (2) find bad indents:
            let rec loopIndent lnNo =
                if lnNo > codeLines.LastLineIdx then
                    true // reached end
                else
                    match codeLines.GetLine(lnNo,id) with
                    | ValueNone -> false // exit early
                    | ValueSome ln ->
                        if ln.indent % defaultIndenting <> 0 then // indent is not a multiple of defaultIndenting
                            if ln.indent <> ln.len then // exclude all white lines
                                LineTransformers.Insert(newTrans, lnNo , {from=ln.offStart; till=ln.offStart+ln.indent; act=semActs.BadIndentAction} )
                        loopIndent (lnNo+1)


            // (3) find unused declarations:
            let getUnused () =
                let unusedDeclarations = getUnusedDecl(checkRes, id)
                let rec loopUnused i =
                    let count = unusedDeclarations.Count
                    if i = count then
                        true // reached end
                    elif i > count then
                        false // something went wrong, probably unusedDeclarations was replaced with another list
                    else
                        let r = unusedDeclarations.[i]
                        let lineNo = max 1 r.StartLine
                        match codeLines.GetLine(lineNo,id) with
                        | ValueNone -> false
                        | ValueSome offLn ->
                            let st = offLn.offStart + r.StartColumn
                            let en = offLn.offStart + r.EndColumn
                            LineTransformers.Insert(newTrans,lineNo, {from=st; till=en; act=semActs.UnUsed})
                            loopUnused (i+1)
                loopUnused 0


            if loopSemantic 0
            && loopIndent 1 // lines start at 1
            && getUnused() then
                trans.Update(newTrans)
                foundSemanticsEv.Trigger(id)





