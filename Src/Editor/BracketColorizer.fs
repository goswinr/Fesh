namespace Seff.Editor

open Seff
open Seff.Model
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
open System.Collections.Generic


type BracketKind = 
    | OpAnRec // {|
    | OpArr  // [|

    | OpRect // [
    | OpCurly 
    | OpRound 

    | ClAnRec // |}
    | ClArr 
      
    | ClRect
    | ClCurly 
    | ClRound 

[<Struct>]
type BracketInfo = {bracket: BracketKind; off:int;  color:SolidColorBrush; idx:int}


/// Highlight-all-occurrences-of-selected-text in Text View
type BracketHighlighter (ed:TextEditor) = 
    inherit AvalonEdit.Rendering.DocumentColorizingTransformer()    

    let colErr = Brushes.Red

    let unclosedBg = Brushes.Pink |> brighter 25
    //let pairBg =     Brushes.Gray |> brighter 85
    let pairBg =     Brushes.Moccasin// |> brighter 125

    let colors = [|
        null // the first one is null ( to keep the coloring from xshd file)
        Brushes.Magenta //|> darker 60
        Brushes.DarkGreen //    |> darker 70
        Brushes.DarkOrange
        Brushes.Blue    |> brighter 40
        Brushes.Yellow  |> darker 40
        |]

    let nextColor i = colors.[i % colors.Length] 
    
    let Brs        = ResizeArray<BracketKind>()
    let Offs       = ResizeArray<int>()
    let Cols       = ResizeArray<SolidColorBrush>()
    let Unclosed   = ResizeArray<int>()
    let UnclosedBr = ResizeArray<BracketKind>()
    let PairStarts = Dictionary<int,BracketInfo>()
    let PairEnds   = Dictionary<int,BracketInfo>()

    let mutable pairStart = -1
    let mutable pairEnd = -1
    let mutable pairLen = -1

    //member val Log : ISeffLog option= None with get , set

    member this.FindBrackets (ed:IEditor) =
        match ed.FileCheckState.FullCodeAndId with 
        | NoCode ->() 
        | CodeID (tx,_) ->
            let len2 = tx.Length  - 1          
            
            Brs.Clear()
            Offs.Clear()
            Cols.Clear()
            Unclosed.Clear() 
            UnclosedBr.Clear()
            PairStarts.Clear()
            PairEnds.Clear()
            pairStart <- -1
            pairEnd   <- -1
            pairLen   <- -1

            let mutable inComment      = false
            let mutable inBlockComment = false
            let mutable inString       = false
            let mutable inAtString     = false // with @
            let mutable inRawString    = false // with @


            let rec find i = 
                if i < len2  then 
                    let t0 = tx.[i]
                    let t1 = tx.[i+1]
                    if inComment then 
                        if  t0='\n' then inComment <- false          ; find (i+1)  
                        else find (i+1)  
                    
                    elif inBlockComment then 
                          if  t0='*' && t1 = ')' then inBlockComment <- false  ; find (i+2)  
                          else find (i+1)  
                        
                    elif inString then 
                        if   t0='\\' && t1 = '"'  then     find (i+2) //an escaped quote in a string
                        elif t0= '"' then  inString <- false;    find (i+1)
                        else find (i+1)
                        
                    elif inAtString then
                        if    t0= '"' then  inAtString <- false;    find (i+1)
                        else find (i+1)
                            
                    elif inRawString then
                        if  t0='"' && t1 = '"' && i+1 < len2 && tx.[i+2] = '"' then  inRawString <- false;    find (i+3)
                        else find (i+1)
                                
                    else // in Code
                        // opening brackets                        
                        if  t0='{' then
                            if  t1 = '|' then Brs.Add  OpAnRec ; Offs.Add i ; find (i+2)
                            else                    Brs.Add  OpCurly ; Offs.Add i ; find (i+1)
                        elif  t0='[' then
                            if   t1 = '|' then Brs.Add  OpArr  ; Offs.Add i  ; find (i+2)
                            else                     Brs.Add  OpRect ; Offs.Add i  ; find (i+1)
                        elif  
                            t0='(' then
                                if    t1 = ')' then                             find (i+2) // skip '(' flollowed by ')'
                                elif  t1 = '*' then   inBlockComment <- true ;  find (i+2) // skip '(' flollowed by ')'
                                
                                else                     Brs.Add  OpRound ; Offs.Add i ; find (i+1)

                        // closing brackets
                        elif t0 = '|' then
                            if   t1 = ']' then Brs.Add ClArr  ; Offs.Add i  ; find (i+2)
                            elif t1 = '}' then Brs.Add ClRect ; Offs.Add i  ; find (i+2)
                            else                                                    find (i+1)

                        elif  t0='}' then Brs.Add ClCurly ; Offs.Add i ; find (i+1)
                        elif  t0=']' then Brs.Add ClRect  ; Offs.Add i ; find (i+1)
                        elif  t0=')' then Brs.Add ClRound ; Offs.Add i ; find (i+1)
                        
                        // escape cases:                        

                        elif  t0='@' && t1 = '"' then inAtString <- true    ; find (i+2)
                        elif  t0='"' && t1 = '"' && i+1 < len2 && tx.[i+2] = '"' then  inRawString <- true;    find (i+3)
                        elif  t0='"'  then inString <- true            ; find (i+1)
                        elif  t0='/'  && t1 = '/'  then inComment <- true    ; find (i+2)                        
                        // if char just jump over it 
                        elif  t0='\'' then 
                            if    i+1  < len2 && tx.[i+2] = '\''                     then   find (i+3) // a regular  character, including quote "
                            elif  i+2  < len2 && t1 = '\\' && tx.[i+3]  = '\'' then   find (i+4) // a simple escaped character
                            elif  i+6  < len2 && t1 = '\\' && tx.[i+2] = 'u' && tx.[i+7]  = '\'' then   find (i+8) // a 16 bit unicode character
                            elif  i+10 < len2 && t1 = '\\' && tx.[i+2] = 'U' && tx.[i+11] = '\'' then   find (i+12) // a 32 bit unicode character
                            else find (i+1)
                        else   
                            find (i+1)
               
                // check last char
                elif i>2 && i = tx.Length - 1  && not inComment && not inString then 
                    let t0 = tx.[i]
                    if tx.[i-1] = '|' then
                        if   t0 = ']' then Brs.Add ClArr  ; Offs.Add i  
                        elif t0 = '}' then Brs.Add ClRect ; Offs.Add i
                    elif t0='{' then Brs.Add OpCurly ; Offs.Add i 
                    elif t0='[' then Brs.Add OpRect  ; Offs.Add i  
                    elif t0='(' then Brs.Add OpRound ; Offs.Add i                 
                    elif t0='}' then Brs.Add ClCurly ; Offs.Add i 
                    elif t0=']' then Brs.Add ClRect  ; Offs.Add i 
                    elif t0=')' then Brs.Add ClRound ; Offs.Add i 
                   
                    
            find 0     

            // find matching brackets and colors via a Stack
            let st = Collections.Generic.Stack<BracketInfo>()
            for i=0 to  Brs.Count - 1 do
                match Brs.[i] with 
                | OpAnRec | OpArr  | OpRect  | OpCurly | OpRound  -> 
                    let col = nextColor (st.Count)
                    st.Push {bracket = Brs.[i]; off= Offs.[i]; color = col; idx=i}
                    Cols.Add col
                | ClAnRec | ClArr  | ClRect  | ClCurly | ClRound  -> 
                    if st.Count = 0 then 
                        Cols.Add colErr
                    else
                        let bc = st.Peek()
                        let ok = 
                            match Brs.[i] with
                            | ClAnRec -> bc.bracket = OpAnRec 
                            | ClArr   -> bc.bracket = OpArr     
                            | ClRect  -> bc.bracket = OpRect  
                            | ClCurly -> bc.bracket = OpCurly 
                            | ClRound -> bc.bracket = OpRound  
                            | OpAnRec | OpArr  | OpRect  | OpCurly | OpRound  -> false
                        if ok then 
                            st.Pop ()  |> ignore
                            Cols.Add bc.color
                            PairEnds.[bc.off] <- {bracket = Brs.[i]; off= Offs.[i]; color = bc.color; idx=i}
                            PairStarts.[Offs.[i]] <- bc
                        else
                            Cols.Add colErr

            //for k in PairEnds.Keys do   ed.Log.PrintfnDebugMsg   "start %d end  %d" k PairEnds.[k]
            //for k in PairStarts.Keys do   ed.Log.PrintfnDebugMsg "end  %d start  %d" k PairStarts.[k]

            

            //for i=0 to Cols.Count-1 do ed.Log.PrintfnDebugMsg "%A in %A at %d" Brs.[i] Cols.[i] Offs.[i]
            //ed.Log.PrintfnDebugMsg "%d Brackets" Brs.Count
            //if Brs.Count = 0 then ed.Log.PrintfnDebugMsg "inComment   inBlockComment  inString  %b %b %b" inComment   inBlockComment  inString 

            // find error in remainig stack items:
            for e in st do
                Unclosed.Add   e.off
                UnclosedBr.Add e.bracket

            ed.AvaEdit.TextArea.TextView.Redraw() 
 
    member this.HighlightPair(ed:IEditor) =
        if ed.AvaEdit.TextArea.Selection.Length = 0 then
            let pos = ed.AvaEdit.TextArea.Caret.Offset
            for i = 0 to Offs.Count - 1 do // or binary serach
                let off = Offs.[i]
                if off = pos || off = pos - 1  then
                    //this.Log.Value.PrintfnDebugMsg "Bracket %d to %d on Line %d " off (off+1) line.LineNumber                
                    
                    match Brs.[i] with 
                    | OpAnRec | OpArr | OpRect | OpCurly | OpRound   -> 
                        pairStart <- off
                        let ok,pe = PairEnds.TryGetValue(pairStart)
                        if ok then pairEnd <- pe.off
                        else
                            //ed.Log.PrintfnAppErrorMsg "Cant find corresponding End bracket for %A in %s" Brs.[i] (Selection.currentLine ed.AvaEdit)
                            pairEnd <- -1
                        
                    | ClRound | ClRect | ClCurly| ClAnRec | ClArr    ->
                        pairEnd <- off
                        let ok,ps = PairStarts.TryGetValue(pairEnd)
                        if ok then pairStart <- ps.off
                        else
                            //ed.Log.PrintfnAppErrorMsg "Cant find corresponding Start bracket for %A in %s" Brs.[i] (Selection.currentLine ed.AvaEdit)
                            pairStart <- -1


                    pairLen <- 
                        match Brs.[i] with 
                        | ClRound | OpRect | OpCurly | OpRound  | ClRect | ClCurly  -> 1   
                        | OpAnRec | OpArr | ClAnRec | ClArr                         -> 2  

                    //ed.Log.PrintfnDebugMsg "pairStart %d pairEnd %d pairLen %d" pairStart pairEnd pairLen
                    if pairStart >=0 && pairEnd > pairStart then
                        ed.AvaEdit.TextArea.TextView.Redraw() 
    
    /// This gets called for every visible line on any view change
    override this.ColorizeLine(line:Document.DocumentLine) =       
        if Brs.Count > 0 &&  Cols.Count = Offs.Count then 
            let st = line.Offset
            let en = line.EndOffset
            for i = 0 to Offs.Count - 1 do // or binary serach
                if notNull Cols.[i] then // the first one is null ( to keep the coloring from xshd file)
                    let off = Offs.[i]
                    if off >= st && off < en then 
                        //this.Log.Value.PrintfnDebugMsg "Bracket %d to %d on Line %d " off (off+1) line.LineNumber                
                        match Brs.[i] with 
                        | ClRound | OpRect | OpCurly | OpRound  | ClRect | ClCurly  -> base.ChangeLinePart( off, off+1, fun el -> el.TextRunProperties.SetForegroundBrush(Cols.[i]))   
                        | OpAnRec | OpArr | ClAnRec | ClArr                         -> base.ChangeLinePart( off, off+2, fun el -> el.TextRunProperties.SetForegroundBrush(Cols.[i]))                
                
            for i = 0 to Unclosed.Count - 1 do // or binary serach
                let off = Unclosed.[i]
                if off >= st && off < en then               
                    match UnclosedBr.[i] with 
                    | ClRound | OpRect | OpCurly | OpRound  | ClRect | ClCurly  -> base.ChangeLinePart( off, off+1, fun el -> el.TextRunProperties.SetBackgroundBrush(unclosedBg))   
                    | OpAnRec | OpArr | ClAnRec | ClArr                         -> base.ChangeLinePart( off, off+2, fun el -> el.TextRunProperties.SetBackgroundBrush(unclosedBg))  
            
            
            if pairStart >= st && pairStart < en then  base.ChangeLinePart( pairStart, pairStart + pairLen, fun el -> el.TextRunProperties.SetBackgroundBrush(pairBg))   
            if pairEnd   >= st && pairEnd   < en then  base.ChangeLinePart( pairEnd  , pairEnd   + pairLen, fun el -> el.TextRunProperties.SetBackgroundBrush(pairBg))   
                

        //else this.Log.Value.PrintfnAppErrorMsg "Brs %d Offs %d Cols %d,  on Line %d " Brs.Count  Offs.Count Cols.Count  line.LineNumber

    
    
    static member Setup(ed:IEditor, ch:Checker) =   
        if false then // TODO fix bug first !!!
            let brh = BracketHighlighter(ed.AvaEdit)
            //brh.Log <- Some ed.Log
            ed.AvaEdit.TextArea.TextView.LineTransformers.Add(brh)
        
            ch.OnFullCodeAvailabe.Add( fun ched ->
              if ched.Id = ed.Id then 
                  //ed.Log.PrintfnInfoMsg "OnFullCodeAvailabe checking Breackets"
                  brh.FindBrackets(ed) )
        
            ed.AvaEdit.TextArea.Caret.PositionChanged.Add ( fun e -> brh.HighlightPair(ed))


