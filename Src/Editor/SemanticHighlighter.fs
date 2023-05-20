namespace Seff.Editor

open System
open System.Windows.Media

open FSharp.Compiler
open FSharp.Compiler.EditorServices
open FSharp.Compiler.CodeAnalysis

open AvalonEditB
open AvalonEditB.Rendering
open AvalonLog.Brush

open Seff.Model

// see  https://github.com/dotnet/fsharp/blob/main/src/Compiler/Service/SemanticClassification.fs

module SemColor = 

    let ValueType                    = freeze <| Brushes.MediumOrchid  |> darker 40
    let ReferenceType                = freeze <| Brushes.MediumVioletRed  |> darker 60  
    let Type                         = freeze <| Brushes.MediumVioletRed  |> darker 60
    let UnionCase                    = freeze <| Brushes.LightSkyBlue  |> darker 100 
    let UnionCaseField               = freeze <| Brushes.LightSkyBlue  |> darker 100
    let Function                     = freeze <| Brushes.DarkGoldenrod |> darker 40
    let Property                     = freeze <| Brushes.DarkTurquoise |> darker 110
    let MutableVar                   = freeze <| Brushes.Goldenrod     |> darker 20
    let MutableRecordField           = freeze <| Brushes.Goldenrod     |> darker 20
    let Module                       = freeze <| Brushes.Black
    let Namespace                    = freeze <| Brushes.Black  
    //let Printf                     = freeze <| Brushes.Plum      // covered by xshd
    let ComputationExpression        = freeze <| Brushes.DarkGray
    let IntrinsicFunction            = freeze <| Brushes.DarkBlue
    let Enumeration                  = freeze <| Brushes.Indigo
    let Interface                    = freeze <| Brushes.DarkGray
    let TypeArgument                 = freeze <| Brushes.SlateBlue
    let Operator                     = freeze <| Brushes.MediumSlateBlue
    let DisposableType               = freeze <| Brushes.DarkOrchid
    let DisposableTopLevelValue      = freeze <| Brushes.DarkOrchid
    let DisposableLocalValue         = freeze <| Brushes.DarkOrchid
    let Method                       = freeze <| Brushes.DarkTurquoise |> darker 60
    let ExtensionMethod              = freeze <| Brushes.DarkTurquoise |> darker 30
    let ConstructorForReferenceType  = freeze <| Brushes.Brown
    let ConstructorForValueType      = freeze <| Brushes.SandyBrown    |> darker 80
    let Literal                      = freeze <| Brushes.SeaGreen
    let RecordField                  = freeze <| Brushes.DarkSlateGray |> darker 10
    let RecordFieldAsFunction        = freeze <| Brushes.Plum
    let Exception                    = freeze <| Brushes.HotPink |> darker 40  
    let Field                        = freeze <| Brushes.MediumPurple
    let Event                        = freeze <| Brushes.Olive
    let Delegate                     = freeze <| Brushes.DarkOliveGreen
    let NamedArgument                = freeze <| Brushes.PaleVioletRed |> darker 80
    let Value                        = freeze <| Brushes.DarkRed       |> darker 20
    let LocalValue                   = freeze <| Brushes.DarkRed       |> darker 40
    let TypeDef                      = freeze <| Brushes.Thistle       |> darker 50
    let Plaintext                    = freeze <| Brushes.OrangeRed     |> darker 60   
                                        
    let UnUsed                       = freeze <| Brushes.Gray |> brighter 30   

type SemActions() =
    

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
        el.TextRunProperties.SetTypeface(Seff.StyleState.italicBoldEditorTf)
        el.TextRunProperties.SetTypographyProperties(stylisticSet1) // for cursive set of cascadia mono


    member val ReferenceTypeA              = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.ReferenceType              ))
    member val ReferenceType               = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.ReferenceType              ))
    member val ValueType                   = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.ValueType                  ))
    member val UnionCase                   = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.UnionCase                  ))
    member val UnionCaseField              = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.UnionCaseField             ))
    member val Function                    = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.Function                   ))
    member val Property                    = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.Property                   ))
    member val MutableVar                  = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.MutableVar                 ))
    member val Module                      = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.Module                     ))
    member val Namespace                   = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.Namespace                  ))
    //member val Printf                    = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.Printf                     )) // covered by xshd
    member val ComputationExpression       = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.ComputationExpression      ))
    member val IntrinsicFunction           = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.IntrinsicFunction          ))
    member val Enumeration                 = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.Enumeration                ))
    member val Interface                   = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.Interface                  ))
    member val TypeArgument                = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.TypeArgument               ))
    member val Operator                    = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.Operator                   ))
    member val DisposableType              = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.DisposableType             ))
    member val DisposableTopLevelValue     = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.DisposableTopLevelValue    ))
    member val DisposableLocalValue        = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.DisposableLocalValue       ))
    member val Method                      = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.Method                     ))
    member val ExtensionMethod             = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.ExtensionMethod            ))
    member val ConstructorForReferenceType = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.ConstructorForReferenceType))
    member val ConstructorForValueType     = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.ConstructorForValueType    ))
    member val Literal                     = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.Literal                    ))
    member val RecordField                 = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.RecordField                ))
    member val MutableRecordField          = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.MutableRecordField         ))
    member val RecordFieldAsFunction       = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.RecordFieldAsFunction      ))
    member val Exception                   = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.Exception                  ))
    member val Field                       = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.Field                      ))
    member val Event                       = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.Event                      ))
    member val Delegate                    = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.Delegate                   ))
    member val NamedArgument               = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.NamedArgument              ))
    member val Value                       = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.Value                      ))
    member val LocalValue                  = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.LocalValue                 ))//; makeCursive el
    member val Type                        = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.Type                       ))
    member val TypeDef                     = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.TypeDef                    ))
    member val Plaintext                   = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.Plaintext                  ))
                                
    member val UnUsed                      = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(SemColor.UnUsed))


// type alias
type Sc = SemanticClassificationType

/// A DocumentColorizingTransformer.
/// Used to do semantic highlighting
type SemanticHighlighter (state: InteractionState) = 

    let mutable unusedDecl = ResizeArray()

    let setUnusedDecl(checkRes:FSharpCheckFileResults)=
        unusedDecl.Clear()
        async{            
            let! us = UnusedDeclarations.getUnusedDeclarations(checkRes,true)
            for u in us do unusedDecl.Add u
            }  
        |> Async.RunSynchronously
    
    // skip semantic highlighting for these, covered in xshd:
    let skipFunc(st:int, en:int)=        
        let w = state.CodeLines.FullCode.[st..en]
        w.StartsWith    "failwith"
        || w.StartsWith "failIfFalse" // from FsEx
        || w.StartsWith "print"
        || w.StartsWith "eprint"

    let skipModul(st:int, en:int)=        
        let w = state.CodeLines.FullCode.[st..en]
        w.StartsWith "Printf"
    
    /// because some times the range of a property starts before the point
    let correctStart(st:int, en:int) =        
        match state.CodeLines.FullCode.IndexOf('.',st,en-st) with 
        | -1 -> st 
        |  i -> i + 1

    //let action (el:VisualLineElement,brush:SolidColorBrush,r:Text.Range) = el.TextRunProperties.SetForegroundBrush(Brushes.Red)
  
    let trans = state.TransformersSemantic   
    let semActs = SemActions()

    let foundSemanticsEv = new Event<unit>()

    [<CLIEvent>] 
    member _.FoundSemantics = foundSemanticsEv.Publish
    
    member _.UpdateSemHiLiTransformers(checkRes:FSharpCheckFileResults, id) =
        if state.DocChangedId.Value = id then
            //lastCode <- fullCode        
            let allRanges = checkRes.GetSemanticClassification(None)
            
            trans.ClearAllLines()// do as late as possible , offset shifting should do its work till then 
                        
            for i = 0 to allRanges.Length-1 do // doing a reverse search solves a highlighting problem where ranges overlap(previously)
                let sem = allRanges.[i]
                let r = sem.Range            
                let lineNo = max 1 r.StartLine
                match state.CodeLines.GetLine(lineNo,id) with 
                | ValueNone -> ()
                | ValueSome offLn ->                
                    let st = offLn.offStart + r.StartColumn                 
                    let en = offLn.offStart + r.EndColumn

                    let inline push(f,t,a) = trans.Insert(lineNo,{from=f; till=t; act=a})
            
                    match sem.Type with 
                    | Sc.ReferenceType               -> push(st,en, semActs.ReferenceType              )
                    | Sc.ValueType                   -> push(st,en, semActs.ValueType                  )
                    | Sc.UnionCase                   -> push(st,en, semActs.UnionCase                  )
                    | Sc.UnionCaseField              -> push(st,en, semActs.UnionCaseField             )
                    | Sc.Function                    -> if not(skipFunc(st,en)) then push(st,en, semActs.Function)
                    | Sc.Property                    -> push(correctStart(st,en),en, semActs.Property  )// correct so that a string or number literal before the dot does not get colored
                    | Sc.MutableVar                  -> push(st,en, semActs.MutableVar                 )
                    | Sc.Module                      -> if not(skipModul(st,en)) then push(st,en, semActs.Module )
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
                    
                    
                    //with e ->  
                    //    ISeffLog.printError  $"sem.type {sem.Type}:\r\n on line {line} for {r}"
                    //    ISeffLog.printError  $"offSt {offSt} to offEn{offEn} for , st:{st} to en:{en}"
                    //    ISeffLog.printError  $"MSG:{e}"
             
                
            for r in unusedDecl do                     
                let lineNo = max 1 r.StartLine
                match state.CodeLines.GetLine(lineNo,id) with 
                | ValueNone -> ()
                | ValueSome offLn ->  
                    let st = offLn.offStart + r.StartColumn                
                    let en = offLn.offStart + r.EndColumn
                    trans.Insert(lineNo, {from=st; till=en; act=semActs.UnUsed})

        foundSemanticsEv.Trigger()
    
    member _.TransformerLineCount = trans.LineCount // used only for debugging ?

(*  // DELETE
            if r.StartLine = lineNo && r.EndLine = lineNo then 
                let st = offSt + r.StartColumn
                let en = offSt + r.EndColumn
                if en >  offSt  && en <= offEn && st >= offSt  && st <  offEn && en < lastCode.Length then
                    push(st,en, semActs.UnUsed) 


type DebugColorizer () = 
    inherit Rendering.DocumentColorizingTransformer()
        
    override this.ColorizeLine(line:Document.DocumentLine) = 
        let lineNo = line.LineNumber
        if lineNo = 101  then   
            ISeffLog.log.PrintfnDebugMsg $"redraw line 101"                        

module SemanticHighlighting =

    let setup (ed:TextEditor, edId:Guid, ch:Checker)  =        
        // for first highlighting after file opening only:
        ch.OnCheckedForErrors.Add(fun (e,_)-> 
            if e.SemanticRanges.Length = 0 then // len 0 means semantic coloring has never run
                ed.TextArea.TextView.Redraw()
                )
        
        let semHiLi = new SemanticColorizer(ed,edId,ch)        
        ed.TextArea.TextView.LineTransformers.Add(semHiLi)        
        //ed.TextArea.TextView.LineTransformers.Add(new DebugColorizer())        
        semHiLi


*)


