﻿namespace Seff.Editor

open System
open System.Windows.Media
open AvalonEditB
open AvalonEditB.Rendering
open Seff.Util.General
open Seff.Util
open Seff.Util
open Seff.Model
open Seff.Editor.Selection
open FSharp.Compiler
open FSharp.Compiler.EditorServices
open AvalonLog.Brush

// see  https://github.com/dotnet/fsharp/blob/main/src/Compiler/Service/SemanticClassification.fs

module SemColor = 

    let ReferenceType                = Brushes.DarkTurquoise |> darker 80   |> freeze
    let ValueType                    = Brushes.Maroon         |> darker 40 |> freeze
    let UnionCase                    = Brushes.LightSkyBlue  |> darker 100  |> freeze
    let UnionCaseField               = Brushes.LightSkyBlue  |> darker 100 |> freeze
    let Function                     = Brushes.DarkGoldenrod |> darker 80 |> freeze
    let Property                     = Brushes.Indigo |> freeze
    let MutableVar                   = Brushes.Goldenrod |> freeze
    let Module                       = Brushes.SteelBlue |> freeze
    let Namespace                    = Brushes.Black |> freeze
    //let Printf                       = Brushes.Plum      |> freeze // cover by xshd
    let ComputationExpression        = Brushes.DarkGray |> freeze
    let IntrinsicFunction            = Brushes.DarkGray |> freeze
    let Enumeration                  = Brushes.DarkGray |> freeze
    let Interface                    = Brushes.DarkGray |> freeze
    let TypeArgument                 = Brushes.SlateBlue |> freeze
    let Operator                     = Brushes.Magenta |> darker 80 |> freeze
    let DisposableType               = Brushes.DarkOrchid |> freeze
    let DisposableTopLevelValue      = Brushes.DarkOrchid |> freeze
    let DisposableLocalValue         = Brushes.DarkOrchid |> freeze
    let Method                       = Brushes.DarkTurquoise |> darker 60 |> freeze
    let ExtensionMethod              = Brushes.RoyalBlue     |> darker 30 |> freeze
    let ConstructorForReferenceType  = Brushes.Brown |> freeze
    let ConstructorForValueType      = Brushes.SandyBrown |> darker 80 |> freeze
    let Literal                      = Brushes.SeaGreen |> freeze
    let RecordField                  = Brushes.DarkSlateBlue  |> freeze
    let MutableRecordField           = Brushes.Goldenrod |> freeze
    let RecordFieldAsFunction        = Brushes.Plum |> freeze
    let Exception                    = Brushes.HotPink |> darker 40   |> freeze
    let Field                        = Brushes.MediumPurple |> freeze
    let Event                        = Brushes.Olive |> freeze
    let Delegate                     = Brushes.DarkOliveGreen |> freeze
    let NamedArgument                = Brushes.PaleVioletRed |> darker 80 |> freeze
    let Value                        = Brushes.DarkSlateBlue |> freeze
    let LocalValue                   = Brushes.DarkSlateBlue |> freeze
    let Type                         = Brushes.Teal |> freeze
    let TypeDef                      = Brushes.Thistle |> darker 50 |> freeze
    let Plaintext                    = Brushes.OrangeRed |> darker 60 |> freeze   
    
    let UnUsed                       = Brushes.LightGray |> freeze   

module SemAction = 
    open SemColor

    let ReferenceType                (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(ReferenceType              )
    let ValueType                    (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(ValueType                  )
    let UnionCase                    (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(UnionCase                  )
    let UnionCaseField               (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(UnionCaseField             )
    let Function                     (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(Function                   )
    let Property                     (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(Property                   )
    let MutableVar                   (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(MutableVar                 )
    let Module                       (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(Module                     )
    let Namespace                    (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(Namespace                  )
    //let Printf                       (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(Printf                     )
    let ComputationExpression        (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(ComputationExpression      )
    let IntrinsicFunction            (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(IntrinsicFunction          )
    let Enumeration                  (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(Enumeration                )
    let Interface                    (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(Interface                  )
    let TypeArgument                 (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(TypeArgument               )
    let Operator                     (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(Operator                   )
    let DisposableType               (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(DisposableType             )
    let DisposableTopLevelValue      (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(DisposableTopLevelValue    )
    let DisposableLocalValue         (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(DisposableLocalValue       )
    let Method                       (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(Method                     )
    let ExtensionMethod              (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(ExtensionMethod            )
    let ConstructorForReferenceType  (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(ConstructorForReferenceType)
    let ConstructorForValueType      (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(ConstructorForValueType    )
    let Literal                      (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(Literal                    )
    let RecordField                  (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(RecordField                )
    let MutableRecordField           (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(MutableRecordField         )
    let RecordFieldAsFunction        (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(RecordFieldAsFunction      )
    let Exception                    (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(Exception                  )
    let Field                        (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(Field                      )
    let Event                        (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(Event                      )
    let Delegate                     (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(Delegate                   )
    let NamedArgument                (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(NamedArgument              )
    let Value                        (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(Value                      )
    let LocalValue                   (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(LocalValue                 )
    let Type                         (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(Type                       )
    let TypeDef                      (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(TypeDef                    )
    let Plaintext                    (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(Plaintext                  )

    let UnUsed                       (el:VisualLineElement) = el.TextRunProperties.SetForegroundBrush(UnUsed )
   
// type alias
type Sc = SemanticClassificationType

/// A DocumentColorizingTransformer.
/// Used to do semantic highlighting
type SemanticColorizier (ied:TextEditor, edId:Guid, ch:Checker) = 
    inherit Rendering.DocumentColorizingTransformer()
    
    let mutable lastRun = 0L

    let mutable allRanges = [||]

    let mutable unusedDecl = ResizeArray()

    let setUnusedDecl(chRes:CheckResults)=
        unusedDecl.Clear()
        async{            
            let! us = UnusedDeclarations.getUnusedDeclarations(chRes.checkRes,true)
            for u in us do unusedDecl.Add u
            }   |> Async.RunSynchronously
    
    // skip semantic highlighting for these, covered in xshd:
    let skipFunc(st:int, en:int, res:CheckResults)=        
        match res.code with 
        |PartialCode _ -> false // should never happen
        |FullCode s -> 
            let w = s.[st..en]
            w.StartsWith    "failwith"
            || w.StartsWith "failIfFalse" // from FsEx
            || w.StartsWith "print"
            || w.StartsWith "eprint"

    let skipModul(st:int, en:int, res:CheckResults)=        
        match res.code with 
        |PartialCode _ -> false // should never happen
        |FullCode s -> 
            let w = s.[st..en]
            w.StartsWith "Printf"
    
    /// becaus some thims the range of a property starts before the point
    let correctStart(st:int, en:int, res:CheckResults) =        
        match res.code with 
        |PartialCode _ -> st // should never happen
        |FullCode s -> 
            let w = s.[st..en]
            match s.IndexOf('.',st,en-st) with 
            | -1 -> st 
            |  i -> i + 1


    let action (el:VisualLineElement,brush:SolidColorBrush,r:Text.Range) =
        el.TextRunProperties.SetForegroundBrush(Brushes.Red)
    
    member _.Ranges :SemanticClassificationItem [] = allRanges


    /// This gets called for every visible line on any view change
    override this.ColorizeLine(line:Document.DocumentLine) =
            match ch.GlobalCheckState with 
            |Checking _
            |GettingCode _
            |NotStarted 
            |Failed -> ()
            |Done chRes ->
                if chRes.editorId = edId then                     
                    if lastRun <> chRes.checkId then 
                        allRanges <- chRes.checkRes.GetSemanticClassification(None)
                        setUnusedDecl(chRes)
                        lastRun <-chRes.checkId                         
                    
                    let lineNo = line.LineNumber
                    let off = line.Offset    
                    
                    // TODO use binary search instead !!
                    for i=allRanges.Length-1 downto 0 do // doing a reverse search solves a highlighting problem where ranges overlap
                        let sem = allRanges.[i]
                        let r = sem.Range
                        if r.StartLine = lineNo && r.EndLine = lineNo then 
                            let st = off + sem.Range.StartColumn
                            let en = off + sem.Range.EndColumn
                            //try // beacuse other wise Seff crashes with  AppDomain.CurrentDomain.UnhandledException on bad ranges
                            match sem.Type with 
                            | Sc.ReferenceType               -> base.ChangeLinePart(st,en, SemAction.ReferenceType              )
                            | Sc.ValueType                   -> base.ChangeLinePart(st,en, SemAction.ValueType                  )
                            | Sc.UnionCase                   -> base.ChangeLinePart(st,en, SemAction.UnionCase                  )
                            | Sc.UnionCaseField              -> base.ChangeLinePart(st,en, SemAction.UnionCaseField             )
                            | Sc.Function                    -> if not(skipFunc(st,en,chRes)) then base.ChangeLinePart(st,en, SemAction.Function)
                            | Sc.Property                    -> base.ChangeLinePart(correctStart(st,en,chRes),en, SemAction.Property                   )
                            | Sc.MutableVar                  -> base.ChangeLinePart(st,en, SemAction.MutableVar                 )
                            | Sc.Module                      -> if not(skipModul(st,en,chRes)) then base.ChangeLinePart(st,en, SemAction.Module                     )
                            | Sc.Namespace                   -> base.ChangeLinePart(st,en, SemAction.Namespace                  )
                            //| Sc.Printf                      -> base.ChangeLinePart(st,en, SemAction.Printf                     )
                            | Sc.ComputationExpression       -> base.ChangeLinePart(st,en, SemAction.ComputationExpression      )
                            | Sc.IntrinsicFunction           -> base.ChangeLinePart(st,en, SemAction.IntrinsicFunction          )
                            | Sc.Enumeration                 -> base.ChangeLinePart(st,en, SemAction.Enumeration                )
                            | Sc.Interface                   -> base.ChangeLinePart(st,en, SemAction.Interface                  )
                            | Sc.TypeArgument                -> base.ChangeLinePart(st,en, SemAction.TypeArgument               )
                            | Sc.Operator                    -> base.ChangeLinePart(st,en, SemAction.Operator                   )
                            | Sc.DisposableType              -> base.ChangeLinePart(st,en, SemAction.DisposableType             )
                            | Sc.DisposableTopLevelValue     -> base.ChangeLinePart(st,en, SemAction.DisposableTopLevelValue    )
                            | Sc.DisposableLocalValue        -> base.ChangeLinePart(st,en, SemAction.DisposableLocalValue       )
                            | Sc.Method                      -> base.ChangeLinePart(st,en, SemAction.Method                     )
                            | Sc.ExtensionMethod             -> base.ChangeLinePart(st,en, SemAction.ExtensionMethod            )
                            | Sc.ConstructorForReferenceType -> base.ChangeLinePart(st,en, SemAction.ConstructorForReferenceType)
                            | Sc.ConstructorForValueType     -> base.ChangeLinePart(st,en, SemAction.ConstructorForValueType    )
                            | Sc.Literal                     -> base.ChangeLinePart(st,en, SemAction.Literal                    )
                            | Sc.RecordField                 -> base.ChangeLinePart(st,en, SemAction.RecordField                )
                            | Sc.MutableRecordField          -> base.ChangeLinePart(st,en, SemAction.MutableRecordField         )
                            | Sc.RecordFieldAsFunction       -> base.ChangeLinePart(st,en, SemAction.RecordFieldAsFunction      )
                            | Sc.Exception                   -> base.ChangeLinePart(st,en, SemAction.Exception                  )
                            | Sc.Field                       -> base.ChangeLinePart(st,en, SemAction.Field                      )
                            | Sc.Event                       -> base.ChangeLinePart(st,en, SemAction.Event                      )
                            | Sc.Delegate                    -> base.ChangeLinePart(st,en, SemAction.Delegate                   )
                            | Sc.NamedArgument               -> base.ChangeLinePart(st,en, SemAction.NamedArgument              )
                            | Sc.Value                       -> base.ChangeLinePart(st,en, SemAction.Value                      )
                            | Sc.LocalValue                  -> base.ChangeLinePart(st,en, SemAction.LocalValue                 )
                            | Sc.Type                        -> base.ChangeLinePart(st,en, SemAction.Type                       )
                            | Sc.TypeDef                     -> base.ChangeLinePart(st,en, SemAction.TypeDef                    )
                            | Sc.Plaintext                   -> base.ChangeLinePart(st,en, SemAction.Plaintext                  )   
                            | _ -> ()
                            //with e -> 
                            //    ISeffLog.printError $"sem.Type {sem.Type}:\r\n on Line {line} for {r}"
                            //    ISeffLog.printError $"off {off}, st:{st}, en:{en}"
                            //    ISeffLog.printError $"MSG:{e}" 
                        
                        for r in unusedDecl do                     
                            if r.StartLine = lineNo && r.EndLine = lineNo then 
                                let st = off + r.StartColumn
                                let en = off + r.EndColumn
                                base.ChangeLinePart(st,en, SemAction.UnUsed) 

module SemanticHighlighting =

    let setup (ed:TextEditor, edId:Guid, ch:Checker)  =        
        let semHiLi = new SemanticColorizier(ed,edId,ch)        
        ed.TextArea.TextView.LineTransformers.Add(semHiLi)        
        semHiLi