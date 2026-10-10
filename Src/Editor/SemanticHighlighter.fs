namespace Fesh.Editor

open System

open FSharp.Compiler
open FSharp.Compiler.EditorServices
open FSharp.Compiler.CodeAnalysis

open AvalonEditB

open Fesh.Model

// see  https://github.com/dotnet/fsharp/blob/main/src/Compiler/Service/SemanticClassification.fs


/// The colors are defined in SyntaxHighlightingFSharp.xshd, as 'Semantic.' colors.
/// They change with the theme, the Actions don't need to be recreated.
type SemActions() =

    member val ReferenceType               = SemanticColors.action "ReferenceType"
    member val ValueType                   = SemanticColors.action "ValueType"
    member val UnionCase                   = SemanticColors.action "UnionCase"
    member val UnionCaseField              = SemanticColors.action "UnionCaseField"
    member val Function                    = SemanticColors.action "Function"
    member val Property                    = SemanticColors.action "Property"
    member val MutableVar                  = SemanticColors.action "MutableVar"
    member val Module                      = SemanticColors.action "Module"
    member val Namespace                   = SemanticColors.action "Namespace"
    //member val Printf                    = SemanticColors.action "Printf" // covered by xshd
    member val ComputationExpression       = SemanticColors.action "ComputationExpression"
    member val IntrinsicFunction           = SemanticColors.action "IntrinsicFunction"
    member val Enumeration                 = SemanticColors.action "Enumeration"
    member val Interface                   = SemanticColors.action "Interface"
    member val TypeArgument                = SemanticColors.action "TypeArgument"
    member val Operator                    = SemanticColors.action "Operator"
    member val DisposableType              = SemanticColors.action "DisposableType"
    member val DisposableTopLevelValue     = SemanticColors.action "DisposableTopLevelValue"
    member val DisposableLocalValue        = SemanticColors.action "DisposableLocalValue"
    member val Method                      = SemanticColors.action "Method"
    member val ExtensionMethod             = SemanticColors.action "ExtensionMethod"
    member val ConstructorForReferenceType = SemanticColors.action "ConstructorForReferenceType"
    member val ConstructorForValueType     = SemanticColors.action "ConstructorForValueType"
    member val Literal                     = SemanticColors.action "Literal"
    member val RecordField                 = SemanticColors.action "RecordField"
    member val MutableRecordField          = SemanticColors.action "MutableRecordField"
    member val RecordFieldAsFunction       = SemanticColors.action "RecordFieldAsFunction"
    member val Exception                   = SemanticColors.action "Exception"
    member val Field                       = SemanticColors.action "Field"
    member val Event                       = SemanticColors.action "Event"
    member val Delegate                    = SemanticColors.action "Delegate"
    member val NamedArgument               = SemanticColors.action "NamedArgument"
    member val Value                       = SemanticColors.action "Value"
    member val LocalValue                  = SemanticColors.action "LocalValue"
    member val Type                        = SemanticColors.action "Type"
    member val TypeDef                     = SemanticColors.action "TypeDef"
    member val Plaintext                   = SemanticColors.action "Plaintext"

    member val UnUsed                      = SemanticColors.action "Unused"

    member val BadIndentAction             = SemanticColors.action "BadIndent"


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


    //let action (el:VisualLineElement,brush:SolidColorBrush,r:Text.Range) = el.TextRunProperties.SetForegroundBrush(Brushes.Red)
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





