namespace Seff.Editor

open Seff
open Seff.Config
open Seff.Util
open Seff.Util.General
open System
open System.Windows
open System.Windows.Controls
open System.Windows.Media

open ICSharpCode.AvalonEdit
open ICSharpCode.AvalonEdit.Editing
open ICSharpCode.AvalonEdit.Document
open FSharp.Compiler
open FSharp.Compiler.SourceCodeServices
open ICSharpCode




type Bracket = 
    | OpAnRec 
    | OpArr 

    | OpRect 
    | OpCurly 
    | OpRound 

    | ClAnRec 
    | ClArr 
      
    | ClRect
    | ClCurly 
    | ClRound 

[<Struct>]
type BracketColor = {bracket: Bracket; color:SolidColorBrush}


/// Highlight-all-occurrences-of-selected-text in Text View
type BracketHighlighter (ed:TextEditor) = 
    inherit AvalonEdit.Rendering.DocumentColorizingTransformer()    

    let colErr = Brushes.Red

    let colors = [|
        Brushes.DarkMagenta
        Brushes.Green
        Brushes.Blue
        Brushes.DarkOrange
        Brushes.Azure
        |]
    
    let idx = ref -1
    
    let nextColor()= 
        incr idx
        if !idx= colors.Length then idx := 0
        colors.[!idx]
    
    let Brs = ResizeArray<Bracket>()
    let Offs = ResizeArray<int>()
    let Cols = ResizeArray<SolidColorBrush>()
    //let LocalBr = ResizeArray<int*SolidColorBrush>()

    member this.FindBrackets (ed:IEditor) =
        match ed.FileCheckState.FullCodeAndId with 
        | NoCode ->() 
        | CodeID (tx,_) ->
            let len = tx.Length            
            
            Brs.Clear()
            Offs.Clear()
            Cols.Clear()

            let rec find i = // TODO ad tracking for comments and symbols in strings
                if i < len  then 
                    // opening brackets
                    if  tx.[i]='{' then
                        if i + 1 < len && tx.[i+1] = '|' then Brs.Add  OpAnRec ; Offs.Add i ; find (i+2)
                        else                                  Brs.Add  OpCurly ; Offs.Add i ; find (i+1)
                    elif  tx.[i]='[' then
                        if i + 1 < len && tx.[i+1] = '|' then Brs.Add  OpArr  ; Offs.Add i  ; find (i+2)
                        else                                  Brs.Add  OpRect ; Offs.Add i  ; find (i+1)
                    elif  
                        tx.[i]='(' then                       Brs.Add  OpRound ; Offs.Add i ; find (i+1)

                    // closing brackets
                    elif tx.[i] = '|' then 
                        if i + 1 < len then 
                            if   tx.[i+1] = ']' then Brs.Add ClArr  ; Offs.Add i  ; find (i+2)
                            elif tx.[i+1] = '}' then Brs.Add ClRect ; Offs.Add i  ; find (i+2)
                            else                                                    find (i+1)

                    elif  tx.[i]='}' then Brs.Add ClCurly ; Offs.Add i ; find (i+1)
                    elif  tx.[i]=']' then Brs.Add ClRect  ; Offs.Add i ; find (i+1)
                    elif  tx.[i]=')' then Brs.Add ClRound ; Offs.Add i ; find (i+1)
                    else                                                 find (i+1)
            find 0     

            let Cols = ResizeArray<SolidColorBrush>(Offs.Count)
            let st = Collections.Generic.Stack<BracketColor>()
            for i=0 to  Brs.Count - 1 do                 
                Cols.Add  (            
                    match Brs.[i] with 
                    | OpAnRec | OpArr  | OpRect  | OpCurly | OpRound  -> 
                        let col = nextColor()
                        st.Push {bracket = Brs.[i]; color = col}
                        col

                    | ClAnRec -> if st.Count > 0 && st.Peek().bracket = OpAnRec then st.Pop().color else  colErr
                    | ClArr   -> if st.Count > 0 && st.Peek().bracket = OpArr   then st.Pop().color else  colErr  
                    | ClRect  -> if st.Count > 0 && st.Peek().bracket = OpRect  then st.Pop().color else  colErr
                    | ClCurly -> if st.Count > 0 && st.Peek().bracket = OpCurly then st.Pop().color else  colErr
                    | ClRound -> if st.Count > 0 && st.Peek().bracket = OpRound then st.Pop().color else  colErr )
                                                                                          
            ed.Log.PrintDebugMsg "found %d Brackets, redraw" Brs.Count
            ed.Log.PrintDebugMsg "Brs %d Offs %d  Cols %d" Brs.Count Cols.Count  Offs.Count
            ed.AvaEdit.TextArea.TextView.Redraw() 

    member val Log : ISeffLog option= None with get , set
            

    /// This gets called for every visible line on any view change
    override this.ColorizeLine(line:Document.DocumentLine) =       
        if Brs.Count > 0 &&  Cols.Count = Offs.Count then //failwithf "offs %d vs Cols %d" Cols.Count  Offs.Count
            let st = line.Offset
            let en = line.EndOffset
            for i = 0 to Offs.Count - 1 do // or binary serach
                let off = Offs.[i]
                if off >= st && off < en - 1 then 
                    //printfn "Bracket %d to %d " off (off+1)
                
                    match Brs.[i] with 
                    | ClRound | OpRect | OpCurly | OpRound  | ClRect | ClCurly  -> base.ChangeLinePart( off,off+2, fun el -> el.TextRunProperties.SetForegroundBrush(Cols.[i]))   
                    | OpAnRec | OpArr | ClAnRec | ClArr                         -> base.ChangeLinePart( off,off+1, fun el -> el.TextRunProperties.SetForegroundBrush(Cols.[i]))                
                
            else
                this.Log.Value.PrintDebugMsg "Brs %d Offs %d  Cols %d" Brs.Count Cols.Count  Offs.Count

    
    
    static member Setup(ed:IEditor, ch:Checker) =   
        let brh = BracketHighlighter(ed.AvaEdit)
        brh.Log <- Some ed.Log
        ed.AvaEdit.TextArea.TextView.LineTransformers.Add(brh)
        ch.OnFullCodeAvailabe.Add ( fun ched ->
            if ched.Id = ed.Id then 
                ed.Log.PrintInfoMsg "checking Breackets"
                brh.FindBrackets(ed) )
        


