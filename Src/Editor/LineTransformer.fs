namespace Seff.Editor

open System
open System.Windows.Media
open FSharp.Compiler
open FSharp.Compiler.EditorServices
open AvalonEditB
open AvalonEditB.Rendering
open AvalonLog.Brush
open Seff.Model
open System.Windows


type Change = {
    form:int
    till: int
    action: Action<VisualLineElement>
    }

/// for accessing the highlighting of a line in constant time
type LineTransformer() =    

    let lines = ResizeArray<ResizeArray<Change>>()

    member _.Lines = lines

    member _.Insert(line,c) =         
        while lines.Count <= line  do // fill up missing lines
            lines.Add ( ResizeArray())
        lines[line].Add c
        
    member _.ClearLine(line) =
        lines[line].Clear()
    
    member _.ClearAllLines() =
        for line in lines do 
            line.Clear()


/// An effcient DocumentColorizingTransformer using line number indices into a line transformer list.
type SemanticColorizer (lt:LineTransformer) = 
    inherit Rendering.DocumentColorizingTransformer()    

    /// This gets called for every visible line on every Redraw
    override this.ColorizeLine(line:Document.DocumentLine) =   
        let lineNo = line.LineNumber
        let offSt  = line.Offset    
        let offEn  = offSt + line.Length         
        
        if lineNo >= lt.Lines.Count then 
            ISeffLog.log.PrintfnDebugMsg $"Cant get line index {lineNo} from {lt.Lines.Count} lines in LineTransformer"
        else
            for ch in lt.Lines[lineNo] do  
                if   ch.form > offEn then ISeffLog.log.PrintfnDebugMsg $" ch.form {ch.form} > offEn {offEn}  in LineTransformer"
                elif ch.till > offEn then ISeffLog.log.PrintfnDebugMsg $" ch.till {ch.till} > offEn {offEn}  in LineTransformer"
                elif ch.till < offSt then ISeffLog.log.PrintfnDebugMsg $" ch.till {ch.till} < offSt {offSt}  in LineTransformer"
                elif ch.form < offSt then ISeffLog.log.PrintfnDebugMsg $" ch.form {ch.form} < offSt {offSt}  in LineTransformer"            
                else
                    base.ChangeLinePart(ch.form,ch.till,ch.action) 