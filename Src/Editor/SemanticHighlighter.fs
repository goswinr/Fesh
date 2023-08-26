namespace Seff.Editor

open System
open System.Windows.Media

open FSharp.Compiler
open FSharp.Compiler.EditorServices
open FSharp.Compiler.CodeAnalysis

open AvalonEditB
open AvalonEditB.Rendering
open AvalonLog.Brush

open Seff
open Seff.Model

// see  https://github.com/dotnet/fsharp/blob/main/src/Compiler/Service/SemanticClassification.fs
 

type SemActions() =
    
    let c_ValueType                    = freeze <| Brushes.MediumOrchid  |> darker 40
    let c_ReferenceType                = freeze <| Brushes.MediumVioletRed  |> darker 60  
    let c_Type                         = freeze <| Brushes.MediumVioletRed  |> darker 40
    let c_UnionCase                    = freeze <| Brushes.LightSkyBlue  |> darker 100 
    let c_UnionCaseField               = freeze <| Brushes.LightSkyBlue  |> darker 100
    let c_Function                     = freeze <| Brushes.DarkGoldenrod |> darker 40
    let c_Property                     = freeze <| Brushes.DarkTurquoise |> darker 110
    let c_MutableVar                   = freeze <| Brushes.Goldenrod     |> darker 20
    let c_MutableRecordField           = freeze <| Brushes.Goldenrod     |> darker 20
    let c_Module                       = freeze <| Brushes.Black
    let c_Namespace                    = freeze <| Brushes.Black  
    //let c_Printf                       = freeze <| Brushes.Plum      // covered by xshd highlighting
    let c_ComputationExpression        = freeze <| Brushes.Indigo
    let c_IntrinsicFunction            = freeze <| Brushes.DarkBlue
    let c_Enumeration                  = freeze <| Brushes.Indigo
    let c_Interface                    = freeze <| Brushes.MediumVioletRed  |> darker 20
    let c_TypeArgument                 = freeze <| Brushes.SlateBlue
    let c_Operator                     = freeze <| Brushes.MediumSlateBlue
    let c_DisposableType               = freeze <| Brushes.DarkOrchid
    let c_DisposableTopLevelValue      = freeze <| Brushes.DarkOrchid
    let c_DisposableLocalValue         = freeze <| Brushes.DarkOrchid
    let c_Method                       = freeze <| Brushes.DarkTurquoise |> darker 60
    let c_ExtensionMethod              = freeze <| Brushes.DarkTurquoise |> darker 30
    let c_ConstructorForReferenceType  = freeze <| Brushes.Brown
    let c_ConstructorForValueType      = freeze <| Brushes.SandyBrown    |> darker 80
    let c_Literal                      = freeze <| Brushes.SeaGreen
    let c_RecordField                  = freeze <| Brushes.DarkSlateGray |> darker 10
    let c_RecordFieldAsFunction        = freeze <| Brushes.Plum
    let c_Exception                    = freeze <| Brushes.HotPink |> darker 40  
    let c_Field                        = freeze <| Brushes.MediumPurple
    let c_Event                        = freeze <| Brushes.Olive
    let c_Delegate                     = freeze <| Brushes.DarkOliveGreen
    let c_NamedArgument                = freeze <| Brushes.PaleVioletRed |> darker 80
    let c_Value                        = freeze <| Brushes.DarkRed       |> darker 20
    let c_LocalValue                   = freeze <| Brushes.DarkRed       |> darker 40
    let c_TypeDef                      = freeze <| Brushes.Purple       
    let c_Plaintext                    = freeze <| Brushes.OrangeRed     |> darker 60   
                                        
    let c_UnUsed                       = freeze <| Brushes.Gray |> brighter 40 

    let badIndentBrush =        
        //Color.FromArgb(30uy,255uy,140uy,0uy) // a very light transparent Orange, transparent to show column rulers behind
        Color.FromArgb(40uy,255uy,255uy,0uy) // a very light transparent Yellow, transparent to show column rulers behind
        |> SolidColorBrush
        |> freeze

    /// this allows using the cursive version of Cascadia Mono
    let stylisticSet1 = 
        {new DefaultTextRunTypographyProperties() with 
            override this.StylisticSet1 with get() = true
        }

    let makeCursive (el:VisualLineElement) =              
        // let f = el.TextRunProperties.Typeface.FontFamily
        // let tf = new Typeface(f, FontStyles.Italic, FontWeights.Bold, FontStretches.Normal)
        // eprintfn $"makeCursive {f}"
        // el.TextRunProperties.SetTypeface(tf)
        el.TextRunProperties.SetTypeface(StyleState.italicBoldEditorTf)
        el.TextRunProperties.SetTypographyProperties(stylisticSet1) // for cursive set of Cascadia Mono


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
type SemanticHighlighter (state: InteractionState) = 

    let mutable unusedDeclarations = ResizeArray<Text.range>()

    let codeLines = state.CodeLines

    let setUnusedDecl(checkRes:FSharpCheckFileResults,id) =         
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
            unusedDeclarations <- unusedDecl
            }  
        |> Async.RunSynchronously
    
    // skip semantic highlighting for these, covered in xshd:
    let skipFunc(st:int, en:int)=        
        let w = codeLines.FullCode.[st..en]
        w.StartsWith    "failwith"
        || w.StartsWith "failIfFalse" // from FsEx
        || w.StartsWith "print"
        || w.StartsWith "eprint"

    let skipModul(st:int, en:int)=        
        let w = codeLines.FullCode.[st..en]
        w.StartsWith "Printf"
    
    /// because some times the range of a property starts before the point
    let correctStart(st:int, en:int) = 
        // search from back to find last dot, there may be more than one
        match codeLines.FullCode.LastIndexOf('.', en-1, en-st) with // at file end the end column in the reported range might be equal to FullCode.Length, so we do -1 to avoid a ArgumentOutOfRangeException.
        | -1 -> st 
        |  i -> i + 1
       

    //let action (el:VisualLineElement,brush:SolidColorBrush,r:Text.Range) = el.TextRunProperties.SetForegroundBrush(Brushes.Red)
    let defaultIndenting = state.Editor.Options.IndentationSize

    let trans = state.TransformersSemantic   
    let semActs = SemActions()

    let foundSemanticsEv = new Event<int64>()

    [<CLIEvent>] 
    member _.FoundSemantics = foundSemanticsEv.Publish
    
    member _.UpdateSemHiLiTransformers(checkRes:FSharpCheckFileResults, id) =
        if state.IsLatest id then                   
            let allRanges = checkRes.GetSemanticClassification(None)
            
            let newTrans = ResizeArray<ResizeArray<LinePartChange>>(trans.LineCount+4)

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
                        // (1) find bad indents:
                        if ln.indent % defaultIndenting <> 0 then      
                            LineTransformers.Insert(newTrans, lineNo , {from=ln.offStart; till=ln.offStart+ln.indent; act=semActs.BadIndentAction} )
                                        
                        // (2) find semantic highlight:  
                        let st = ln .offStart + r.StartColumn                 
                        let en = ln .offStart + r.EndColumn
                        //ISeffLog.log.PrintfnDebugMsg $"{lineNo}:{sem.Type} {r.StartColumn} to {r.EndColumn}"

                        let inline push(f,t,a) =  LineTransformers.Insert(newTrans,lineNo,{from=f; till=t; act=a})

                        match sem.Type with 
                        | Sc.ReferenceType               -> push(st,en, semActs.ReferenceType              )
                        | Sc.ValueType                   -> push(st,en, semActs.ValueType                  )
                        | Sc.UnionCase                   -> push(st,en, semActs.UnionCase                  )
                        | Sc.UnionCaseField              -> push(st,en, semActs.UnionCaseField             )
                        | Sc.Function                    -> if not(skipFunc(st,en)) then push(correctStart(st,en),en, semActs.Function)
                        | Sc.Property                    -> push(correctStart(st,en),en, semActs.Property  )// correct so that a string or number literal before the dot does not get colored
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
                        | Sc.Method                      -> push(correctStart(st,en),en, semActs.Method    )// correct so that a string or number literal before the dot does not get colored
                        | Sc.ExtensionMethod             -> push(correctStart(st,en),en, semActs.ExtensionMethod)
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

            if loopSemantic 0 then 
                setUnusedDecl(checkRes,  id)
                if state.IsLatest id then    
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
                    
                    if loopUnused 0 then
                        trans.Update(newTrans)
                        foundSemanticsEv.Trigger(id)   





