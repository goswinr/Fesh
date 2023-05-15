﻿namespace Seff.Editor

open System
open System.Windows.Media
open System.Collections.Generic

open AvalonEditB
open AvalonLog.Brush

open Seff.Model
open Seff.Util.General
open AvalonEditB.Document
open AvalonEditB.Rendering


type BracketKind = 
    // Opening Brackets:
    | OpAnRec // {|
    | OpArr   // [|

    | OpRect  // [
    | OpCurly // {
    | OpRound // (

    // Closing Brackets:
    | ClAnRec // |}
    | ClArr   // |]

    | ClRect
    | ClCurly
    | ClRound

[<Struct>]
type BracketInfo = {
    bracket: BracketKind
    off:int  
    lnNo:int  
    color:Action<VisualLineElement>
    idx:int}

type RedrawSegment(startOffset,  endOffset)  = 
    member s.Offset      = startOffset
    member s.EndOffset   = endOffset
    member s.Length      = endOffset - startOffset
    
    override s.ToString() = $"RedrawSegment form: {s.Offset}, len:{s.Length}"

    interface ISegment with 
        member s.Offset      = startOffset
        member s.EndOffset   = endOffset
        member s.Length      = endOffset - startOffset  
        
    member t.Merge (o:RedrawSegment) = 
        new RedrawSegment(
            min t.Offset o.Offset, 
            max t.EndOffset o.EndOffset )

module Render = 
    
    let inline setTextColor (b:SolidColorBrush) (el:Rendering.VisualLineElement) = 
        el.TextRunProperties.SetForegroundBrush(b) 
        

    let inline setBgColor (b:SolidColorBrush) (el:Rendering.VisualLineElement) = 
        el.TextRunProperties.SetBackgroundBrush(b)


type BracketHighlighter (state:InteractionState) =     

    let colErr   = Brushes.Red                  |> freeze
    let colErrBg = Brushes.Pink |> brighter 25  |> freeze
    let colPair  = Brushes.Gray |> brighter 70  |> freeze  

    let colors = [|
        null // the first one is null ( to keep the coloring from xshd file)        
        Brushes.Yellow     |> darker 80    |> freeze
        Brushes.Green      |> darker 20    |> freeze
        Brushes.Blue       |> brighter 40  |> freeze
        Brushes.Magenta    |> darker 70    |> freeze
        |]

    let actErr      = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(colErr); el.TextRunProperties.SetBackgroundBrush(colErrBg))    
    let actPair     = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(colPair))

    let acts = colors|> Array.map ( fun c -> if isNull c then null else new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c)))

    let nextAction i = acts.[i % acts.Length]
    //let nextColor i = colors.[i % colors.Length]

    let Brs        = ResizeArray<BracketKind>()
    let Offs       = ResizeArray<int>() // doc offsets
    let LineNos    = ResizeArray<int>() // line numbers
    let Acts       = ResizeArray<Action<VisualLineElement>>()
    let Unclosed   = ResizeArray<BracketInfo>() 
    let PairStarts = Dictionary<int,BracketInfo>()
    let PairEnds   = Dictionary<int,BracketInfo>() 
    let mutable idAtListupdate = state.DocChangedId.Value
    let mutable listsAreClean = false
    
    // for highlighting matching brackets at cursor:
    let mutable pairStart = -1
    let mutable pairStartLn = -1
    let mutable pairEnd = -1
    let mutable pairEndLn = -1
    let mutable pairLen = -1         
    
    let findAllBrackets (tx:CodeAsString, id, cur:Int64 ref ) =         
        listsAreClean <-false
        Brs.Clear()
        Offs.Clear()
        LineNos.Clear()
        Acts.Clear()
        Unclosed.Clear()
        PairStarts.Clear()
        PairEnds.Clear()
        pairStart <- -1
        pairStartLn <- -1
        pairEnd   <- -1
        pairEndLn <- -1
        pairLen   <- -1

        let mutable inComment      = false
        let mutable inBlockComment = false
        let mutable inString       = false
        let mutable inAtString     = false // with @
        let mutable inRawString    = false // with @
        let mutable ln             = 1 // line Number 
        
        let inline push (br, i, lnNo) =  
            Brs.Add br
            Offs.Add i
            LineNos.Add lnNo
        
        let len2 = tx.Length - 1

        let rec find i = 
            if i < len2 && cur.Value = id then
                let t0 = tx.[i]
                let t1 = tx.[i+1]

                if t0='\n' then ln <- ln + 1

                if inComment then
                    if  t0='\n' then inComment <- false   ; find(i+1)
                    else find(i+1)

                elif inBlockComment then
                    if  t0='*' && t1 = ')' then inBlockComment <- false  ; find(i+2)
                    else find(i+1)

                elif inString then
                    if   t0='\\' && t1 = '"'  then  find(i+2) //an escaped quote in a string
                    elif t0='\\' && t1 = '\\' then  find(i+2) //an escaped backslash in a string
                    elif t0= '"'              then  inString <- false;  find(i+1)
                    else find(i+1)

                elif inAtString then
                    if    t0= '"' then  inAtString <- false;  find(i+1)
                    else find(i+1)

                elif inRawString then
                    if  t0='"' && t1 = '"' && i+1 < len2 && tx.[i+2] = '"' then  inRawString <- false;  find(i+3)
                    else find(i+1)

                else // in Code
                    // opening brackets
                    if  t0='{' then
                        if  t1 = '|' then push(OpAnRec, i, ln) ; find(i+2)
                        else              push(OpCurly, i, ln) ; find(i+1)
                    elif  t0='[' then
                        if   t1 = '|' then push(OpArr , i, ln)  ; find(i+2)
                        else               push(OpRect, i, ln)  ; find(i+1)
                    elif
                        t0='(' then
                            if    t1 = ')' then                             find(i+2) // skip '(' followed by ')' directly
                            elif  t1 = '*' then   inBlockComment <- true ;  find(i+2) 

                            else                     push(OpRound, i, ln) ; find(i+1)

                    // closing brackets
                    elif t0 = '|' then
                        if   t1 = ']' then push(ClArr , i, ln)  ; find(i+2)
                        elif t1 = '}' then push(ClRect, i, ln)  ; find(i+2)
                        else                                      find(i+1)

                    elif  t0='}' then push(ClCurly, i, ln) ; find(i+1)
                    elif  t0=']' then push(ClRect , i, ln) ; find(i+1)
                    elif  t0=')' then push(ClRound, i, ln) ; find(i+1)

                    // escape cases:

                    elif  t0='@' && t1 = '"'                                 then inAtString    <- true; find(i+2)
                    elif  t0='"' && t1 = '"' && i+1 < len2 && tx.[i+2] = '"' then inRawString   <- true; find(i+3)
                    elif  t0='"'                                             then inString      <- true; find(i+1)
                    elif  t0='/'  && t1 = '/'                                then inComment     <- true; find(i+2)
                    // if char just jump over it
                    elif  t0='\'' then
                        if    i+1  < len2 && tx.[i+2] = '\''                                 then   find(i+3)  // a regular  character, including quote "
                        elif  i+2  < len2 && t1 = '\\' && tx.[i+3]  = '\''                   then   find(i+4)  // a simple escaped character
                        elif  i+6  < len2 && t1 = '\\' && tx.[i+2] = 'u' && tx.[i+7]  = '\'' then   find(i+8)  // a 16 bit unicode character
                        elif  i+10 < len2 && t1 = '\\' && tx.[i+2] = 'U' && tx.[i+11] = '\'' then   find(i+12) // a 32 bit unicode character
                        else find(i+1)
                    else
                        find(i+1)

            // check last character of text
            elif i>2 && i = tx.Length - 1  && not inComment && not inString && cur.Value = id then
                let t0 = tx.[i]
                if tx.[i-1] = '|' then
                    if   t0 = ']' then push(ClArr , i, ln)
                    elif t0 = '}' then push(ClRect, i, ln)
                elif t0='{' then push(OpCurly, i, ln)
                elif t0='[' then push(OpRect , i, ln)
                elif t0='(' then push(OpRound, i, ln)
                elif t0='}' then push(ClCurly, i, ln)
                elif t0=']' then push(ClRect , i, ln)
                elif t0=')' then push(ClRound, i, ln)

        find 0

        // find matching brackets and colors via a Stack
        let stack = Collections.Generic.Stack<BracketInfo>()
        for i=0 to  Brs.Count - 1 do
            if cur.Value = id then 
                match Brs.[i] with
                | OpAnRec | OpArr  | OpRect  | OpCurly | OpRound  ->
                    let col = nextAction (stack.Count)
                    stack.Push {bracket = Brs.[i]; off= Offs.[i]; lnNo = LineNos.[i]; color = col; idx=i}
                    Acts.Add col
                | ClAnRec | ClArr  | ClRect  | ClCurly | ClRound  ->
                    if stack.Count = 0 then
                        Acts.Add actErr
                    else
                        let bc = stack.Peek()
                        let ok = 
                            match Brs.[i] with
                            | ClAnRec -> bc.bracket = OpAnRec
                            | ClArr   -> bc.bracket = OpArr
                            | ClRect  -> bc.bracket = OpRect
                            | ClCurly -> bc.bracket = OpCurly
                            | ClRound -> bc.bracket = OpRound
                            | OpAnRec | OpArr  | OpRect  | OpCurly | OpRound  -> false
                        if ok then
                            stack.Pop ()  |> ignore
                            Acts.Add bc.color
                            PairEnds.[bc.off]     <- {bracket = Brs.[i]; off= Offs.[i]; lnNo = LineNos.[i]; color = bc.color; idx=i}
                            PairStarts.[Offs.[i]] <- bc
                        else
                            Acts.Add actErr

        //for k in PairEnds.Keys do   ed.Log.PrintfnDebugMsg   "start %d end  %d" k PairEnds.[k]
        //for k in PairStarts.Keys do   ed.Log.PrintfnDebugMsg "end  %d start  %d" k PairStarts.[k]

        //for i=0 to Cols.Count-1 do ed.Log.PrintfnDebugMsg "%A in %A at %d" Brs.[i] Cols.[i] Offs.[i]
        //ISeffLog.log.PrintfnDebugMsg "%d Brackets found " Brs.Count
        //if Brs.Count = 0 then ed.Log.PrintfnDebugMsg "inComment   inBlockComment  inString  %b %b %b" inComment   inBlockComment  inString

        // find error in remaining stack items:
        for e in stack do
            Unclosed.Add e  
        
        if cur.Value = id then 
            listsAreClean <-true
    
    // for previous highlighting matching brackets at cursor:
    let mutable prevStartLn = -1
    let mutable prevEndLn = -1
    
    /// this needs the offsets precomputed
    /// finds if offset is before or after the bracket, or in between for two char brackets
    let findHighlightPairAtCursor(caretOff) = 
    
        //if ed.TextArea.Selection.Length = 0 then // or always do ?
        
        let oMin = caretOff - 2
        let oMax = caretOff 
        
        /// returns the index where found
        let rec binSearch lo hi =
            if lo > hi || not listsAreClean then 
                None
            else
                let mid = lo + (hi - lo) / 2
                let o = Offs.[mid]
                if   o < oMin then binSearch lo (mid - 1)
                elif o > oMax then binSearch (mid + 1) hi
                else Some mid 

        match binSearch 0 (Offs.Count - 1) with 
        | None -> () // do not clear previous highlighting pairs
        | Some i -> 
            let off = Offs.[i]
            let ln = LineNos.[i]
            if off = caretOff || off = caretOff - 1  then
                //ISeffLog.log.PrintfnDebugMsg "Bracket %d to %d on Line %d " off (off+1) line.LineNumber
                prevStartLn <- pairStartLn 
                prevEndLn   <- pairEndLn 
                match Brs.[i] with
                | OpAnRec | OpArr | OpRect | OpCurly | OpRound   ->
                    pairStart   <- off
                    pairStartLn <- ln
                    let ok,pe = PairEnds.TryGetValue(pairStart)
                    if ok then 
                        pairEnd   <- pe.off
                        pairEndLn <- pe.lnNo
                    else
                        //ISeffLog.log.PrintfnAppErrorMsg "Cant find corresponding End bracket for %A in %s" Brs.[i] (Selection.currentLine ed.AvaEdit)
                        pairEnd <- -1

                | ClAnRec | ClArr | ClRect | ClCurly | ClRound     ->
                    pairEnd   <- off
                    pairEndLn <- ln
                    let ok,ps = PairStarts.TryGetValue(pairEnd)
                    if ok then 
                        pairStart   <- ps.off
                        pairStartLn <- ps.lnNo
                    else
                        //ISeffLog.log.PrintfnAppErrorMsg "Cant find corresponding Start bracket for %A in %s" Brs.[i] (Selection.currentLine ed.AvaEdit)
                        pairStart <- -1

                pairLen <-
                    match Brs.[i] with
                    | ClRound | OpRect | OpCurly | OpRound  | ClRect | ClCurly  -> 1
                    | OpAnRec | OpArr | ClAnRec | ClArr                         -> 2

                //ed.Log.PrintfnDebugMsg "pairStart %d pairEnd %d pairLen %d" pairStart pairEnd pairLen                
                
            // just for making to work right after two char bracket like |] too
            elif off = caretOff - 2  then 
                //ISeffLog.log.PrintfnDebugMsg "Bracket %d to %d on Line %d " off (off+1) line.LineNumber
                prevStartLn <- pairStartLn 
                prevEndLn   <- pairEndLn 

                let mutable isTwoChars = true
                match Brs.[i] with
                | OpRect | OpCurly | OpRound   -> isTwoChars<- false // skip single char
                | OpAnRec | OpArr ->
                    pairStart <- off
                    pairStartLn <- ln
                    let ok,pe = PairEnds.TryGetValue(pairStart)
                    if ok then 
                        pairEnd   <- pe.off
                        pairEndLn <- pe.lnNo
                    else
                        //ISeffLog.log.PrintfnAppErrorMsg "Cant find corresponding End bracket for %A in %s" Brs.[i] (Selection.currentLine ed.AvaEdit)
                        pairEnd <- -1

                | ClRect| ClCurly| ClRound  -> isTwoChars<- false// skip single char
                | ClAnRec | ClArr-> 
                    pairEnd <- off
                    pairEndLn <- ln
                    let ok,ps = PairStarts.TryGetValue(pairEnd)
                    if ok then 
                        pairStart   <- ps.off
                        pairStartLn <- ps.lnNo
                    else
                        //ISeffLog.log.PrintfnAppErrorMsg "Cant find corresponding Start bracket for %A in %s" Brs.[i] (Selection.currentLine ed.AvaEdit)
                        pairStart <- -1
                    
                if isTwoChars then 
                    pairLen <- 2                    
    
    let transMatch = state.TransformersMatchingBrackets
    
    let updatePairTransformers() =
        if pairStart > 0 && pairEnd > 0  then // must check both
            // first remove previous transformers
            transMatch.ClearAllLines()           
            transMatch.Insert(pairStartLn, {from=pairStart; till=pairStart + pairLen; act=actPair})
            transMatch.Insert(pairEndLn  , {from=pairEnd;   till=pairEnd   + pairLen; act=actPair})
    
    let mutable prevPairSeg: RedrawSegment option = None

    let redrawSegment() =
        let seg = RedrawSegment(pairStart,pairEnd)            
        match prevPairSeg with 
        |Some prev -> 
            let m = seg.Merge(prev)                    
            //ISeffLog.printnColor 150 222 50 $"HighlightPair merged {m}"
            state.Editor.TextArea.TextView.Redraw(m)            
        |None ->
            //ISeffLog.printnColor 150 222 50 $"HighlightPair {seg}"
            state.Editor.TextArea.TextView.Redraw(seg)
        prevPairSeg <- Some seg     
    
    
    let caretPositionChanged(e:EventArgs) = 
        let id = state.DocChangedId.Value
        let caretOff = state.Editor.CaretOffset
        state.Caret <- caretOff
        async{ 
            do! Async.Sleep 50 // wait for update the offset list Offs lists
            if state.DocChangedId.Value = id then 
                findHighlightPairAtCursor(caretOff) 
                updatePairTransformers()
                if state.DocChangedId.Value = id then 
                    do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context
                    redrawSegment()            
        } |> Async.Start    
    
    let foundBracketsEv = new Event<unit>()

    let transAll = state.TransformersAllBrackets
    
    do
        state.Editor.TextArea.Caret.PositionChanged.Add (caretPositionChanged)
    
    [<CLIEvent>] 
    member _.FoundBrackets = foundBracketsEv.Publish
    
    /// This gets called for every visible line on any view change
    member _.UpdateAllBrackets(tx:CodeAsString, id) = 
        let cur = state.DocChangedId        
        findAllBrackets(tx,id,cur)
        findHighlightPairAtCursor(state.Caret)
        if Brs.Count > 0 &&  Acts.Count = Offs.Count && cur.Value = id then                            
            for i = 0 to Offs.Count - 1 do 
                if notNull Acts.[i] then // the first one is null ( to keep the coloring from xshd file)
                    let off = Offs.[i] 
                    let lineNo = LineNos.[i]
                    //ISeffLog.log.PrintfnDebugMsg "Bracket %d to %d on Line %d " off (off+1) line.LineNumber
                    match Brs.[i] with
                    | ClRound | OpRect | OpCurly | OpRound  | ClRect | ClCurly  -> transAll.Insert(lineNo, {from=off; till=off+1; act= Acts.[i] })
                    | OpAnRec | OpArr | ClAnRec | ClArr                         -> transAll.Insert(lineNo, {from=off; till=off+2; act= Acts.[i] })

            for i = 0 to Unclosed.Count - 1 do 
                let u = Unclosed[i]
                let off = u.off
                match u.bracket with
                | ClRound | OpRect | OpCurly | OpRound  | ClRect | ClCurly  -> transAll.Insert(u.lnNo, {from=off; till=off+1; act=actErr}) 
                | OpAnRec | OpArr | ClAnRec | ClArr                         -> transAll.Insert(u.lnNo, {from=off; till=off+2; act=actErr}) 

            updatePairTransformers()
            foundBracketsEv.Trigger()
        


   

