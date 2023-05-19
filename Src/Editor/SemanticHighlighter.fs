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

module SemAction = 
    open SemColor

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


    let ReferenceTypeA              = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(ReferenceType              ))
    let ReferenceType               = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(ReferenceType              ))
    let ValueType                   = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(ValueType                  ))
    let UnionCase                   = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(UnionCase                  ))
    let UnionCaseField              = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(UnionCaseField             ))
    let Function                    = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(Function                   ))
    let Property                    = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(Property                   ))
    let MutableVar                  = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(MutableVar                 ))
    let Module                      = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(Module                     ))
    let Namespace                   = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(Namespace                  ))
    //let Printf                    = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(Printf                     )) // covered by xshd
    let ComputationExpression       = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(ComputationExpression      ))
    let IntrinsicFunction           = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(IntrinsicFunction          ))
    let Enumeration                 = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(Enumeration                ))
    let Interface                   = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(Interface                  ))
    let TypeArgument                = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(TypeArgument               ))
    let Operator                    = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(Operator                   ))
    let DisposableType              = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(DisposableType             ))
    let DisposableTopLevelValue     = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(DisposableTopLevelValue    ))
    let DisposableLocalValue        = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(DisposableLocalValue       ))
    let Method                      = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(Method                     ))
    let ExtensionMethod             = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(ExtensionMethod            ))
    let ConstructorForReferenceType = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(ConstructorForReferenceType))
    let ConstructorForValueType     = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(ConstructorForValueType    ))
    let Literal                     = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(Literal                    ))
    let RecordField                 = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(RecordField                ))
    let MutableRecordField          = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(MutableRecordField         ))
    let RecordFieldAsFunction       = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(RecordFieldAsFunction      ))
    let Exception                   = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(Exception                  ))
    let Field                       = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(Field                      ))
    let Event                       = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(Event                      ))
    let Delegate                    = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(Delegate                   ))
    let NamedArgument               = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(NamedArgument              ))
    let Value                       = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(Value                      ))
    let LocalValue                  = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(LocalValue                 ))//; makeCursive el
    let Type                        = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(Type                       ))
    let TypeDef                     = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(TypeDef                    ))
    let Plaintext                   = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(Plaintext                  ))
                                    
    let UnUsed                      = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(UnUsed))
        

// type alias
type Sc = SemanticClassificationType

/// A DocumentColorizingTransformer.
/// Used to do semantic highlighting
type SemanticHighlighter (state: InteractionState) = 
    (*
    inherit Rendering.DocumentColorizingTransformer() // DELETE
    let mutable lastCheckId = -1L

    let mutable allRanges: SemanticClassificationItem[] = [||]
    *)
    
    let mutable lastCode = ""

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
        let w = lastCode.[st..en]
        w.StartsWith    "failwith"
        || w.StartsWith "failIfFalse" // from FsEx
        || w.StartsWith "print"
        || w.StartsWith "eprint"

    let skipModul(st:int, en:int)=        
        let w = lastCode.[st..en]
        w.StartsWith "Printf"
    
    /// because some times the range of a property starts before the point
    let correctStart(st:int, en:int) =        
        match lastCode.IndexOf('.',st,en-st) with 
        | -1 -> st 
        |  i -> i + 1

    let action (el:VisualLineElement,brush:SolidColorBrush,r:Text.Range) =
        el.TextRunProperties.SetForegroundBrush(Brushes.Red)
  
    let trans = state.TransformersSemantic   

    let foundSemanticsEv = new Event<unit>()

    [<CLIEvent>] 
    member _.FoundSemantics = foundSemanticsEv.Publish
    
    member _.UpdateSemHiLiTransformers(checkRes:FSharpCheckFileResults, id) =
        if state.DocChangedId.Value = id then
            //lastCode <- fullCode        
            let allRanges = checkRes.GetSemanticClassification(None)
            
            //for i = allRanges.Length-1 downto 0 do // doing a reverse search solves a highlighting problem where ranges overlap(previously)
            for i = 0 to allRanges.Length-1 do 
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
                    | Sc.ReferenceType               -> push(st,en, SemAction.ReferenceType              )
                    | Sc.ValueType                   -> push(st,en, SemAction.ValueType                  )
                    | Sc.UnionCase                   -> push(st,en, SemAction.UnionCase                  )
                    | Sc.UnionCaseField              -> push(st,en, SemAction.UnionCaseField             )
                    | Sc.Function                    -> if not(skipFunc(st,en)) then push(st,en, SemAction.Function)
                    | Sc.Property                    -> push(correctStart(st,en),en, SemAction.Property  )// correct so that a string or number literal before the dot does not get colored
                    | Sc.MutableVar                  -> push(st,en, SemAction.MutableVar                 )
                    | Sc.Module                      -> if not(skipModul(st,en)) then push(st,en, SemAction.Module )
                    | Sc.Namespace                   -> push(st,en, SemAction.Namespace                  )
                    | Sc.ComputationExpression       -> push(st,en, SemAction.ComputationExpression      )
                    | Sc.IntrinsicFunction           -> push(st,en, SemAction.IntrinsicFunction          )
                    | Sc.Enumeration                 -> push(st,en, SemAction.Enumeration                )
                    | Sc.Interface                   -> push(st,en, SemAction.Interface                  )
                    | Sc.TypeArgument                -> push(st,en, SemAction.TypeArgument               )
                    | Sc.Operator                    -> push(st,en, SemAction.Operator                   )
                    | Sc.DisposableType              -> push(st,en, SemAction.DisposableType             )
                    | Sc.DisposableTopLevelValue     -> push(st,en, SemAction.DisposableTopLevelValue    )
                    | Sc.DisposableLocalValue        -> push(st,en, SemAction.DisposableLocalValue       )
                    | Sc.Method                      -> push(correctStart(st,en),en, SemAction.Method    )// correct so that a string or number literal before the dot does not get colored
                    | Sc.ExtensionMethod             -> push(correctStart(st,en),en, SemAction.ExtensionMethod)
                    | Sc.ConstructorForReferenceType -> push(st,en, SemAction.ConstructorForReferenceType)
                    | Sc.ConstructorForValueType     -> push(st,en, SemAction.ConstructorForValueType    )
                    | Sc.Literal                     -> push(st,en, SemAction.Literal                    )
                    | Sc.RecordField                 -> push(st,en, SemAction.RecordField                )
                    | Sc.MutableRecordField          -> push(st,en, SemAction.MutableRecordField         )
                    | Sc.RecordFieldAsFunction       -> push(st,en, SemAction.RecordFieldAsFunction      )
                    | Sc.Exception                   -> push(st,en, SemAction.Exception                  )
                    | Sc.Field                       -> push(st,en, SemAction.Field                      )
                    | Sc.Event                       -> push(st,en, SemAction.Event                      )
                    | Sc.Delegate                    -> push(st,en, SemAction.Delegate                   )
                    | Sc.NamedArgument               -> push(st,en, SemAction.NamedArgument              )
                    | Sc.Value                       -> push(st,en, SemAction.Value                      )
                    | Sc.LocalValue                  -> push(st,en, SemAction.LocalValue                 )
                    | Sc.Type                        -> push(st,en, SemAction.Type                       )
                    | Sc.TypeDef                     -> push(st,en, SemAction.TypeDef                    )
                    | Sc.Plaintext                   -> push(st,en, SemAction.Plaintext                  )  
                    | Sc.Printf                      -> () //push(st,en, SemAction.Printf                ) // covered in xshd file 
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
                    trans.Insert(lineNo, {from=st; till=en; act=SemAction.UnUsed})

        foundSemanticsEv.Trigger()
    
    member _.TransformerLineCount = trans.LineCount

(*  // DELETE
            if r.StartLine = lineNo && r.EndLine = lineNo then 
                let st = offSt + r.StartColumn
                let en = offSt + r.EndColumn
                if en >  offSt  && en <= offEn && st >= offSt  && st <  offEn && en < lastCode.Length then
                    push(st,en, SemAction.UnUsed) 


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


