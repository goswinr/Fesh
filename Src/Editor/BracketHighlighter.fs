namespace Seff.Editor

open System
open System.Windows.Media
open System.Collections.Generic

open AvalonEditB
open AvalonLog.Brush

open Seff.Model
open Seff.Util
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
    idx:int
    }

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

module ParseBrackets = 
    
    [<Struct>]
    type Bracket = {
        kind: BracketKind
        from:int 
        }
    
    type MuliLineState = RegCode | MultiLineComment | SimpleString| RawAtString | RawTripleString
        
    let finAll(lns:CodeLineTools.CodeLines, id) : ResizeArray<ResizeArray<Bracket>> option=
        let code = lns.FullCode

        let brss = ResizeArray<ResizeArray<Bracket>>() 
               
               
        let readLine(brs:ResizeArray<Bracket>, prevState: MuliLineState, firstIdx, lastIdx) : MuliLineState =             
            
            /// for simple strings
            let rec skipString chr i :int  =        // give the character and it's index
                if i=lastIdx then
                    match chr  with 
                    | '"'  -> i+1                   
                    |  _   -> Int32.MaxValue // string flows over to the next line 
                else 
                    let next = code[i+1]
                    match chr ,next with 
                    | '"' ,  _  -> i+1 // first char after string
                    | '\\', '"' -> skipString next (i+1) // jump over escaped quote \"
                    | _         -> skipString next (i+1)

            /// for strings starting with @"
            let rec skipRawAtString chr i :int =  // give the character and it's index
                if i=lastIdx then
                    match chr  with 
                    | '"'  -> i+1 //                   
                    |  _   -> Int32.MaxValue // string flows over to the next line 
                else                     
                    match chr with 
                    | '"'  -> i + 1 // first char after  @string                    
                    | _    -> skipRawAtString code[i+1] (i+1) 

            /// for strings starting with """
            let rec skipRawTrippleString chr next i :int =  // give this character and the next character and this index
                if i+2 <= lastIdx then
                    let next2 = code[i+2]
                    match chr , next, next2 with 
                    | '"' , '"' , '"'   -> i+3 // first char after  multiline string                    
                    | _                 -> skipRawTrippleString next next2 (i+1) 
                else 
                    Int32.MaxValue

            let rec skipMultiLineComment chr  i :int =
                if i+1 < lastIdx then                    
                    let next = code[i+1]
                    match chr , next with 
                    | '*' ,')'    -> i+2 // first char after multiline comment                    
                    | _           -> skipMultiLineComment next (i+1) 
                else 
                    Int32.MaxValue
            
            
            
            let rec charLoop chr i : MuliLineState =                

                let inline pushExit      br = brs.Add {kind=br; from = i} ; RegCode
                let inline pushOne  next br = brs.Add {kind=br; from = i} ; charLoop next      (i+1)
                let inline pushTwo       br = brs.Add {kind=br; from = i} ; if i + 2 <= lastIdx then charLoop code[i+2] (i+2) else RegCode
                
                let flowOnOrOver (state:MuliLineState) ii = 
                    match ii with 
                    | Int32.MaxValue -> state
                    | jj             -> if jj <= lastIdx then charLoop code[jj] jj else  RegCode
                
                
                if i > lastIdx then 
                    //eprintfn $"i{i} > lastIdx{lastIdx} is unexpected: prevState{prevState}"
                    RegCode

                elif i = lastIdx then // i is the last char on this line 
                    match chr with                 
                    | '[' -> pushExit OpRect
                    | '(' -> pushExit OpRound
                    | '{' -> pushExit OpCurly
                    | ']' -> pushExit ClRect
                    | ')' -> pushExit ClRound
                    | '}' -> pushExit ClCurly
                    |  _  -> RegCode // just exit loop
                
                else 
                    let next = code[i+1] 
                    match chr,next with 
                    | '/','/' -> RegCode // a comment starts,  exit loop                    
                    | '(','*' -> skipMultiLineComment next (i+1) |> flowOnOrOver MultiLineComment                                                  
                    
                    | '{','|' -> pushTwo OpAnRec
                    | '[','|' -> pushTwo OpArr
                    | '|','}' -> pushTwo ClAnRec
                    | '|',']' -> pushTwo ClArr                   
                    | '[', _  -> pushOne next OpRect
                    | '(', _  -> pushOne next OpRound
                    | '{', _  -> pushOne next OpCurly
                    | ']', _  -> pushOne next ClRect
                    | ')', _  -> pushOne next ClRound
                    | '}', _  -> pushOne next ClCurly
                    | '"','"' -> if i + 2 <= lastIdx then 
                                    let next2 = code[i+2]
                                    if next2 = '"' then // a multiline string starts,
                                        //skipRawTrippleString next next2 (i+2)  |> flowOnOrOver RawTripleString // TODO does this fail on four quotes ?
                                        if i + 4 <= lastIdx then // check if multiline string can be closed on same line
                                            skipRawTrippleString code[i+3] code[i+4] (i+3)  |> flowOnOrOver RawTripleString // TODO does this fail on four quotes ?
                                        else
                                            RawTripleString 
                                    else                                         
                                        charLoop next2 (i+2) // just an empty string, line continue
                                 else  
                                    RegCode // just an empty string, line ends

                    | '@','"' -> skipRawAtString next (i+1)  |> flowOnOrOver RawAtString  //a @string starts, 
                    | '"', _  -> skipString      next (i+1)  |> flowOnOrOver SimpleString //a  regular string starts,                                      

                    | _       -> charLoop next (i+1)

            if firstIdx > lastIdx then // empty line 
                prevState
            else                
                let flowOnOrOver (state:MuliLineState) ii = 
                    match ii with 
                    | Int32.MaxValue -> state
                    | jj             -> if jj <= lastIdx then charLoop code[jj] jj else  RegCode
                
                match prevState with 
                | RegCode          -> charLoop              code[firstIdx] firstIdx
                | SimpleString     -> skipString            code[firstIdx] firstIdx |> flowOnOrOver SimpleString 
                | RawAtString      -> skipRawAtString       code[firstIdx] firstIdx |> flowOnOrOver RawAtString
                | MultiLineComment     -> skipMultiLineComment  code[firstIdx] firstIdx |> flowOnOrOver MultiLineComment
                | RawTripleString  -> 
                    if firstIdx < lastIdx then skipRawTrippleString code[firstIdx] code[firstIdx+1] firstIdx |> flowOnOrOver RawTripleString
                    else RawTripleString           
           
        
        /// returns true if all lines are looped
        let rec lineLoop (lineState:MuliLineState) lnNo =
            if lnNo > lns.LastLineIdx then 
                Some brss // looped till end
            else                
                match lns.GetLine(lnNo,id) with
                |ValueNone -> None // loop aborted
                |ValueSome l -> 
                    let brs = new ResizeArray<Bracket>()
                    let newLineState = readLine (brs, lineState , l.offStart + l.indent, l.offStart + l.len - 1 )                    
                    brss.Add brs     
                    lineLoop newLineState (lnNo + 1)
                    
        lineLoop RegCode 0 // start at 0 even though the 0 line is always empty
    
    let getBLen = function            
        | OpAnRec -> 2
        | OpArr   -> 2            
        | ClAnRec -> 2
        | ClArr   -> 2            
        | OpRect  -> 1
        | OpCurly -> 1
        | OpRound -> 1  
        | ClRect  -> 1
        | ClCurly -> 1
        | ClRound -> 1 

    type BracketPair = {
        line:int
        from:int
        till:int
        kind:BracketKind
        nestingDepth:int
        mutable other:BracketPair option
        }    

    /// grouped by lines
    let findAllPairs(bss: ResizeArray<ResizeArray<Bracket>>) :BracketPair[][] =
        // find matching brackets and colors via a Stack 
        let stack = Collections.Generic.Stack<BracketPair>()

        let pss = Array.zeroCreate<BracketPair[]> bss.Count
        for lineNo = 0 to bss.Count-1 do 
            let bs = bss[lineNo]
            let ps = Array.zeroCreate<BracketPair> bs.Count
            pss[lineNo] <- ps
            for j=0 to bs.Count-1 do 
                let b = bs[j]                
                match b.kind with
                | OpAnRec | OpArr  | OpRect  | OpCurly | OpRound  ->
                    let p = {line=lineNo; from=b.from; till = b.from + getBLen b.kind; kind=b.kind;  nestingDepth=stack.Count; other=None}
                    ps[j] <- p
                    stack.Push p 
                | ClAnRec | ClArr  | ClRect  | ClCurly | ClRound  ->
                    if stack.Count = 0 then //error this closing was never opened
                        let p = {line=lineNo; from=b.from; till = b.from + getBLen b.kind; kind=b.kind;  nestingDepth=stack.Count; other=None}
                        ps[j] <- p
                    else
                        let prev = stack.Peek()
                        let isCorrectClosing = // is the correct closing bracket
                            match b.kind with
                            | ClAnRec -> prev.kind = OpAnRec
                            | ClArr   -> prev.kind = OpArr
                            | ClRect  -> prev.kind = OpRect
                            | ClCurly -> prev.kind = OpCurly
                            | ClRound -> prev.kind = OpRound
                            | OpAnRec | OpArr  | OpRect  | OpCurly | OpRound  -> false
                        if isCorrectClosing then
                            let other = stack.Pop() 
                            let this = {line=lineNo; from=b.from; till = b.from + getBLen b.kind; kind=b.kind; nestingDepth=stack.Count; other=Some other}                            
                            other.other <- Some this
                            ps[j] <- this
                        else
                            let p = {line=lineNo; from=b.from; till = b.from + getBLen b.kind; kind=b.kind; nestingDepth=stack.Count; other=None}                            
                            ps[j] <- p
        pss

    let getOnePair(pss: BracketPair[][], line:int, offset:int) : (BracketPair*BracketPair) option =
        if line >= pss.Length then // this can happen when writing on last line and the code lines are not yet updated
            //eprintfn $"tried to get line {line} of {pss.Length}items"
            None
        else
            pss[line]
            |> Array.tryFindBack ( fun p -> p.from <= offset && offset <= p.till+1  ) // + 1 to also catch caret right after bracket
            |> Option.bind ( fun t -> 
                    match t.other with 
                    |Some o -> 
                        // first sort them:
                        let a,b = if o.from < t.from then o,t else t,o                    
                        if b.from-a.till <= 1 then None // don't return a pair if only on char between them
                        else                       Some(a,b)
                    |None  ->                      None
                    )
    
      
    let debugPrintBrackets(bss:ResizeArray<ResizeArray<Bracket>>, lns:CodeLineTools.CodeLines, id) :unit =
        
        let getBr = function
            // Opening Brackets:
            | OpAnRec -> "{|"
            | OpArr   -> "[|"            
            | OpRect  -> "["
            | OpCurly -> "{"
            | OpRound -> "("            
            // Closing Brackets:
            | ClAnRec -> "|}"
            | ClArr   -> "|]"            
            | ClRect  -> "]"
            | ClCurly -> "}"
            | ClRound -> ")"   
        
        let mutable k = 0
        for i=1 to bss.Count-1 do             
            let bs = bss[i]
            match lns.GetLine(i,id) with
            |ValueNone   -> () // loop aborted
            |ValueSome l -> 
                let mutable from = l.offStart
                for b in bs do 
                    k <- k+1
                    let gapLen = b.from-from
                    if gapLen<0 then 
                        eprintfn $"debugPrint: b.from{b.from} - from{from} is negative"
                    else
                        let gap = String(' ', gapLen)
                        from <- from + gapLen + getBLen b.kind
                        printf $"{gap}{getBr b.kind}"
                //printfn ""
        printfn $"Total {k} Brackets."

open ParseBrackets

type BracketHighlighter (state:InteractionState) =     

    let colPair  = Brushes.Gray |> brighter 80  |> freeze  
    let colErr   = Brushes.Red                  |> freeze
    //let colErrBg = Brushes.Pink |> brighter 25  |> freeze
    //let colErrBg = SolidColorBrush(Color.FromArgb(15uy,255uy,0uy,0uy))|> freeze // a=0 : fully transparent, a=255 opaque
    let colErrBg = SolidColorBrush(Color.FromArgb(90uy,255uy,150uy,0uy))|> freeze // a=0 : fully transparent, a=255 opaque
    
    let colors = [|
        null // the first one is null ( to keep the coloring from xshd file)        
        Brushes.Purple     |> brighter 40  |> freeze
        Brushes.Orange     |> darker 30    |> freeze
        Brushes.Green      |> brighter 30  |> freeze
        Brushes.Cyan       |> darker 40    |> freeze
        |]

    let actErr      = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(colErr); el.TextRunProperties.SetBackgroundBrush(colErrBg))    
    let actPair     = new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(colPair))

    let acts = colors|> Array.map ( fun c -> if isNull c then null else new Action<VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush(c)))

    // ----------coloring on pair of matching brackets at cursor:---------------
    
    let transMatch = state.TransformersMatchingBrackets
    
    let mutable prevPairSeg: RedrawSegment option = None
    
    let mutable allPairs : option<BracketPair[][]> = None
        
    let caretPositionChanged(e:EventArgs) =               
        let id = state.DocChangedId.Value
        let caret = state.Editor.TextArea.Caret
        let caretOff = caret.Offset
        let caretLine= caret.Line
              
        async{
            do! Async.Sleep 50 // wait for update the offset list Offs lists            
            while allPairs.IsNone || state.CodeLines.IsNotFromId id do
                do! Async.Sleep 50 // wait for update the offset list Offs lists
            if state.DocChangedId.Value = id then 
               
               match ParseBrackets.getOnePair(allPairs.Value, caretLine, caretOff) with 
               |None -> 
                    //transMatch.ClearAllLines() // or keep showing the bracket highlighting when cursor moves away??
                    if state.DocChangedId.Value = id then 
                        //redrawSegment:
                        do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context
                        match prevPairSeg with 
                        |Some prev -> 
                            state.Editor.TextArea.TextView.Redraw(prev)            
                            prevPairSeg <- None
                        |None ->()

               |Some (f,t) -> 
                    let newTrans = ResizeArray<ResizeArray<LinePartChange>>(t.line+1) 
                    LineTransformers.Insert(newTrans, f.line, {from=f.from; till=f.till; act = actPair})
                    LineTransformers.Insert(newTrans, t.line, {from=t.from; till=t.till; act = actPair})
                    transMatch.Update(newTrans)
                    if state.DocChangedId.Value = id then 
                        //redrawSegment:
                        do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context
                        //ISeffLog.log.PrintfnDebugMsg $"redraw for caretPositionChanged , id:{id}"
                        let seg = RedrawSegment(f.from,t.till)            
                        match prevPairSeg with 
                        |Some prev -> 
                            let m = seg.Merge(prev) 
                            state.Editor.TextArea.TextView.Redraw(m)            
                        |None ->
                            state.Editor.TextArea.TextView.Redraw(seg)
                        prevPairSeg <- Some seg  
        } |> Async.Start    
    
    let foundBracketsEv = new Event<int64>()

    let transAll = state.TransformersAllBrackets

    let nextAction i = acts.[i % acts.Length]

    // do state.Editor.TextArea.Caret.PositionChanged.Add (caretPositionChanged)        // TODO reenable when fixed, also in list of fast colorizers in InteractionState.fs
    
    [<CLIEvent>] 
    member _.FoundBrackets = foundBracketsEv.Publish
        
    member _.UpdateAllBrackets(id) =        
        allPairs <- None
        match ParseBrackets.finAll(state.CodeLines, id) with
        |None -> ()
        |Some bss ->            
            //ParseBrackets.debugPrintBrackets(bss, state.CodeLines, id)
            let pss =  ParseBrackets.findAllPairs(bss)
            if state.IsLatest id then 
                let newTrans = ResizeArray<ResizeArray<LinePartChange>>(transAll.LineCount+4)
                allPairs <-Some pss
                for lnNo = 0 to pss.Length - 1 do 
                    let ps = pss[lnNo]
                    for i = 0 to ps.Length - 1 do   
                        let p = ps[i]
                        let act = match p.other with |None -> actErr |Some _ -> nextAction p.nestingDepth
                        LineTransformers.Insert(newTrans, lnNo, {from=p.from; till=p.till; act= act })
                transAll.Update(newTrans)
                foundBracketsEv.Trigger(id)        

   
    
    
   