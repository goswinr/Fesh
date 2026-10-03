namespace AvaloniaLog

open AvaloniaLog.Util
open System
open AvaloniaEdit
open Avalonia.Media // for color brushes


/// Describes the position in text where a new color starts
[<Struct>]
[<NoComparison>]
type internal NewColor =
    {
    off   : int
    brush : IImmutableBrush // brush must be frozen to be used async
    }

    /// Does binary search to find an offset that is equal or smaller than currOff
    static member findCurrentInList (cs:ResizeArray<NewColor>) currOff :NewColor =
        //  cs has a least one item that is {off = -1 ; brush=null}, set in AvaloniaLog constructor

        let last = cs.Count-1  //TODO: is it possible that count increases while iterating?
        let rec find lo hi =
            let mid = lo + (hi - lo) / 2          //TODO test edge conditions !!
            if cs.[mid].off <= currOff then
                if mid = last                 then cs.[mid] // exit
                elif cs.[mid+1].off > currOff then cs.[mid] // exit
                else find (mid+1) hi
            else
                find lo (mid-1)
        find 0 last

/// Describes the start and end position of a color with one line
[<Struct>]
[<NoComparison>]
type internal RangeColor =
    {
    start :int
    ende  :int
    brush :IImmutableBrush // brush must be frozen to be used async
    }

    /// Finds all the offset that apply to this line  which is defined by the range of  tOff to enOff
    /// even if the ResizeArray<NewColor> does not contain any offset between stOff and  enOff
    /// it still returns the a list with one item. The closest previous offset
    static member getInRange (cs:ResizeArray<NewColor>) stOff enOff =
        let rec mkList i ls =
            let c = NewColor.findCurrentInList cs i
            if c.off <= stOff  then
                {start = stOff ; ende = enOff ; brush = c.brush} :: ls
            else
                mkList (i-1) ({start = i; ende = enOff ; brush = c.brush}  :: ls)
        mkList enOff []


/// To implement the actual colors from colored printing
type internal ColorizingTransformer(ed:TextEditor, offsetColors: ResizeArray<NewColor>, defaultBrush:IBrush) =
    inherit Rendering.DocumentColorizingTransformer()

    let mutable selStart = -9
    let mutable selEnd   = -9
    let mutable any = false

    member _.SelectionChangedDelegate (_:EventArgs) =
        if ed.SelectionLength = 0 then // no selection
            selStart <- -9
            selEnd   <- -9
        else
            selStart <- ed.SelectionStart
            selEnd   <- selStart + ed.SelectionLength // this is the last selection in case of block selection too ! correct


    /// This gets called for every visible line on any view change
    override this.ColorizeLine(line:Document.DocumentLine) =
        if not line.IsDeleted then
            let stLn = line.Offset
            let enLn = line.EndOffset
            let cs = RangeColor.getInRange offsetColors stLn enLn
            any <- false

            // color non selected lines
            if selStart = selEnd  || selStart > enLn || selEnd < stLn then// no selection in general or on this line
                for c in cs do
                    if c.brush = null && any then //changing the base-foreground is only needed if any other color already exists on this line
                        base.ChangeLinePart(c.start, c.ende, fun element -> element.TextRunProperties.SetForegroundBrush defaultBrush)
                    else
                        if notNull c.brush then // might still happen on first line
                            any <-true
                            base.ChangeLinePart(c.start, c.ende, fun el -> el.TextRunProperties.SetForegroundBrush c.brush)

            // exclude selection from coloring:
            else
                for c in cs do
                    let br = if isNull c.brush then defaultBrush else c.brush
                    let st = c.start
                    let en = c.ende
                    // now consider block or rectangle selection:
                    for seg in ed.TextArea.Selection.Segments do
                        if   seg.EndOffset   < stLn then () // this segment is on another line
                        elif seg.StartOffset > enLn then () // this segment is on another line
                        else
                            if   seg.StartOffset =   seg.EndOffset then base.ChangeLinePart(st,  en, fun el -> el.TextRunProperties.SetForegroundBrush br)  // the selection segment is after the line end, this might happen in block selection
                            elif seg.StartOffset >   en            then base.ChangeLinePart(st,  en, fun el -> el.TextRunProperties.SetForegroundBrush br)  // the selection segment comes after this color section
                            elif seg.EndOffset   <=  st            then base.ChangeLinePart(st,  en, fun el -> el.TextRunProperties.SetForegroundBrush br)  // the selection segment comes before this color section
                            else
                                if st <  seg.StartOffset then base.ChangeLinePart(st           ,  seg.StartOffset, fun el -> el.TextRunProperties.SetForegroundBrush br)
                                if en >  seg.EndOffset   then base.ChangeLinePart(seg.EndOffset,  en             , fun el -> el.TextRunProperties.SetForegroundBrush br)

