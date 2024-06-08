namespace Fesh.Editor

open System
open System.Windows
open System.Collections.Generic

open AvalonEditB
open AvalonEditB.CodeCompletion
open AvalonEditB.Editing
open AvalonEditB.Document
open FSharp.Compiler.EditorServices
open FSharp.Compiler.Tokenization // for keywords
open FSharp.Compiler

open Fesh
open Fesh.Model
open Fesh.Config
open System.Windows.Controls
open System.Windows.Media
open Fesh.XmlParser
open System
open System

type TryShow = DidShow | NoShow

type ShowRestrictions =  JustAll |JustDU | JustKeyWords


module UtilCompletion =

    let mkTexBlock(txt,style) = // the displayed item in the completion window
        let mutable tb = Controls.TextBlock()
        tb.Text <- txt
        tb.FontFamily <- StyleState.fontEditor
        tb.FontSize <-   StyleState.fontSize
        //tb.Foreground  <- col // fails on selection, does not get color inverted//check  https://blogs.msdn.microsoft.com/text/2009/08/28/selection-brush/ ??
        tb.FontStyle <- style
        tb.Padding <- Thickness(0. , 0. , 8. , 0. ) //left top right bottom / so that it does not appear to be trimmed
        tb

type CompletionItemForKeyWord(state: InteractionState, text:string, toolTip:string) =
    let priority =  1.0 + state.Config.AutoCompleteStatistic.Get(text)        // create once and cache ?
    let textBlock = UtilCompletion.mkTexBlock(text,FontStyles.Normal)   // create once and cache ?

    member this.Content    = textBlock :> obj
    member this.Description = toolTip :> obj
    member this.Image = null
    member this.Priority = priority
    member this.Text = text
    member this.Complete (textArea:TextArea, completionSegment:ISegment, e:EventArgs ) =
        state.JustCompleted <- true
        if Selection.getSelType textArea = Selection.RectSel then
            RectangleSelection.complete (textArea, completionSegment, text)
        else
            textArea.Document.Replace(completionSegment, text)
            if text[text.Length-1] = '"' then // to insert cursor between quotes
                textArea.Caret.Offset <- textArea.Caret.Offset - 1

    interface ICompletionData with // needed in F#: implementing the interface members as properties too: https://github.com/icsharpcode/AvalonEdit/issues/28
        member this.Complete(t,s,e) = this.Complete(t,s,e)
        member this.Content         = this.Content
        member this.Description     = this.Description
        member this.Image           = this.Image
        member this.Priority        = this.Priority
        member this.Text            = this.Text

type CompletionItem(state: InteractionState, getToolTip, it:DeclarationListItem, isDotCompletion:bool) =

    //let style =
    //    if it.IsOwnMember then FontStyles.Normal
    //    else
    //        match it.Glyph with    //new Font(FontFamily.GenericSansSerif,12.0F, FontStyle.Bold | FontStyle.Italic) // needs system.drawing
    //        | FSharpGlyph.Module | FSharpGlyph.NameSpace -> FontStyles.Normal
    //        | _                                          -> FontStyles.Italic

    let priority = //if it.IsOwnMember then 1. else 1.
        if isDotCompletion then 1.0 // not on Dot completion
        else                    1.0 + state.Config.AutoCompleteStatistic.Get(it.NameInList) //if p>1.0 then log.PrintfnDebugMsg "%s %g" it.Name p

    let textBlock = UtilCompletion.mkTexBlock(it.NameInList ,FontStyles.Normal)   // create once and cache ?

    member this.Content = textBlock :> obj // the displayed item in the completion window
    member this.Description = getToolTip(it) // this gets called on demand only, not when initially filling the list.
    member this.Image = null //TODO or part of text box ?
    member this.Priority = priority
    member this.Text = it.NameInList // not used for display, but for priority sorting ?
    member this.Complete (textArea:TextArea, completionSegment:ISegment, e:EventArgs) =
        //log.PrintfnDebugMsg "%s is %A and %A" it.Name it.Glyph it.Kind
        state.JustCompleted <- true
        let mutable complText =
            //TODO move this logic out here
            if it.Glyph = FSharpGlyph.Class && it.NameInList.EndsWith "Attribute" then
                "[<" + it.NameInList.Replace("Attribute",">]")

            // elif UtilCompletion.needsTicks it.Name then "``" + it.Name + "``" // fixed in FSharp.Compiler.Service 42.7.100 by using it.NameInCode

            elif it.NameInList = "struct" then
                "[<Struct>]"
            else
                it.NameInCode // may include backticks

        // add '()' at end of word if this is a function taking unit:
        let taggedTextSig =
            match it.Description with
            | ToolTipText.ToolTipText (els) ->
                match els with
                |[]  -> None
                |[el] ->
                        match el with
                        | ToolTipElement.None -> None
                        | ToolTipElement.CompositionError(text) -> None
                        | ToolTipElement.Group(tooTipElemDataList) ->
                            match tooTipElemDataList with
                            |[]  -> None
                            |[eld] -> Some eld.MainDescription
                            | _ -> None // there are multiple signatures
                | _ -> None // there are multiple signatures

        match taggedTextSig with
        |None -> ()
        |Some ts ->
            if ts.Length >= 7 && ts.Length < 25 then // < 25 to skip long list of class definitions
                if ts.[ts.Length-5].Text = "unit"
                && ts.[ts.Length-3].Text = "->"
                && ts.[ts.Length-1].Text = "unit"
                && ts.[ts.Length-7].Text = ":" then
                    complText <- complText + "()"

        //config.Log.PrintfDebugMsg "completionSegment: '%s' : %A" (textArea.Document.GetText(completionSegment)) completionSegment
        if Selection.getSelType textArea = Selection.RectSel then
            RectangleSelection.complete (textArea, completionSegment, complText)
        else
            textArea.Document.Replace(completionSegment, complText)

        if not isDotCompletion then
            state.Config.AutoCompleteStatistic.Incr(it.NameInList)
            state.Config.AutoCompleteStatistic.Save()
        // Event sequence on pressing enter in completion window:
        // (1)Close window
        // (2)Insert text into editor (triggers completion if one char only)
        // (3)Raise InsertionRequested event
        // https://github.com/icsharpcode/AvalonEdit/blob/8fca62270d8ed3694810308061ff55c8820c8dfc/AvalonEditB/CodeCompletion/CompletionWindow.cs#L100

    interface ICompletionData with
        // Note that the CompletionList uses WPF data binding against the properties in this interface.
        // Thus, your implementation of the interface must use public properties; not explicit interface implementation.
        // needed in F#: implementing the interface members as properties too: https://github.com/icsharpcode/AvalonEdit/issues/28
        member this.Complete(t,s,e) = this.Complete(t,s,e)
        member this.Content         = this.Content
        member this.Description     = this.Description //this gets call on demand only, not when filling the completion list.
        member this.Image           = this.Image
        member this.Priority        = this.Priority
        member this.Text            = this.Text // not used for display, but for priority sorting ?

type Completions(state: InteractionState) =
    let avEd = state.Editor

    let mutable win : CompletionWindow option = None

    let mutable willInsert = false // to track if window closed without inserting ( e.g pressing esc)

    let empty = ResizeArray<string>()
    let selectedCompletionText ()=
        match win with
        |None -> ""
        |Some w ->
            match w.CompletionList.SelectedItem with
            | null -> ""
            | i -> i.Text

    // to close other tooltips that might be open from type info
    let showingEv = Event<unit>()

    [<CLIEvent>]
    member _.OnShowing = showingEv.Publish // to close other tooltips that might be open from type info


    member _.IsOpen = win.IsSome

    /// to set state.DocChangedConsequence <- React,
    /// close the completion window if present,
    /// and set win <- None
    member _.CloseAndEnableReacting() =
        state.DocChangedConsequence <- React
        if win.IsSome then
            win.Value.Close()
            win <- None


    /// Initially returns "loading.." text and triggers async computation to get and update with actual text
    member this.GetToolTip(it:DeclarationListItem)=
        async{
            let ttText = it.Description

            let structured =
                if Checker.OptArgsDict.ContainsKey it.FullName then  TypeInfo.makeFeshToolTipDataList (ttText, it.FullName, Checker.OptArgsDict.[it.FullName])
                else                                                 TypeInfo.makeFeshToolTipDataList (ttText, it.FullName, empty)
            if this.IsOpen then
                do! Async.SwitchToContext Fittings.SyncWpf.context
                if this.IsOpen then // might get closed during context switch
                    if selectedCompletionText() = it.NameInList then
                        win.Value.ToolTipContent <- TypeInfo.getPanel (structured, {declListItem=Some it; semanticClass=None; declLocation=None; dllLocation=None })
                        //TODO add structure to a Dict so it does not need to be recomputed if browsing up and down items in the completion list?
        } |> Async.Start
        TypeInfo.loadingText // :> obj


    member _.ComplWin
        with get() : Option<CompletionWindow>  = win
        and set(w  : Option<CompletionWindow>) = win <- w


    member val justKeyWords  =
        let lines = ResizeArray<ICompletionData>()
        for kw,desc in FSharpKeywords.KeywordsWithDescription  do // add keywords to list
            if kw.StartsWith "pri" || kw.StartsWith "mut" || kw.StartsWith "inl" || kw.StartsWith "int" then
                lines.Add( CompletionItemForKeyWord(state,kw,desc) :> ICompletionData) |>ignore
        lines

    member val justAll =
        let lines = ResizeArray<ICompletionData>()
        lines.Add( CompletionItemForKeyWord(state,"#if INTERACTIVE",     "Compiler directive to exclude code in compiled format, close with #endif or #else" ) :> ICompletionData)    |>ignore
        lines.Add( CompletionItemForKeyWord(state,"#if COMPILED",        "Compiler directive to exclude code in interactive format, close with #endif or #else" ) :> ICompletionData)    |>ignore
        lines.Add( CompletionItemForKeyWord(state,"#else",               "else of compiler directives " ) :> ICompletionData)    |>ignore
        lines.Add( CompletionItemForKeyWord(state,"#endif",              "End of compiler directive " ) :> ICompletionData)    |>ignore
        lines.Add( CompletionItemForKeyWord(state,"#r \"nuget: \"",      "Nuget package reference " ) :> ICompletionData)    |>ignore
        lines.Add( CompletionItemForKeyWord(state,"#r \"\"",              "Dll file reference " ) :> ICompletionData)    |>ignore
        lines.Add( CompletionItemForKeyWord(state,"#I \"\"",              "Dll folder reference " ) :> ICompletionData)    |>ignore
        lines.Add( CompletionItemForKeyWord(state,"#load \"\"",           "fsx file reference " ) :> ICompletionData)    |>ignore

        lines.Add( CompletionItemForKeyWord(state,"__SOURCE_DIRECTORY__","Evaluates to the current full path of the source directory" ) :> ICompletionData)    |>ignore
        lines.Add( CompletionItemForKeyWord(state,"__SOURCE_FILE__"     ,"Evaluates to the current source file name, without its path") :> ICompletionData)    |>ignore
        lines.Add( CompletionItemForKeyWord(state,"__LINE__",            "Evaluates to the current line number") :> ICompletionData)    |>ignore
        for kw,desc in FSharpKeywords.KeywordsWithDescription  do // add keywords to list
            lines.Add( CompletionItemForKeyWord(state,kw,desc) :> ICompletionData) |>ignore
        lines

    member this.CompletionLines ( decls: DeclarationListInfo, pos:PositionInCodeEx, showRestrictions:ShowRestrictions )=
        match showRestrictions with
        |JustKeyWords ->
            this.justKeyWords
        |JustDU ->
            let lines = ResizeArray<ICompletionData>(decls.Items.Length)
            for it in decls.Items do
                match it.Glyph with
                |FSharpGlyph.Union |FSharpGlyph.Module |FSharpGlyph.EnumMember ->
                    lines.Add (new CompletionItem(state, this.GetToolTip, it, pos.dotBefore)) // for DU completion add just some.
                |_ -> ()
            lines
        |JustAll ->
            let kws = this.justAll
            let lines = ResizeArray<ICompletionData>(decls.Items.Length + kws.Count)
            if not pos.dotBefore then
                lines.AddRange kws
            for it in decls.Items do
                lines.Add (new CompletionItem(state, this.GetToolTip, it, pos.dotBefore)) // for normal completion add all others too.
            lines

    /// must be called from UI thread
    member this.TryShow( decls: DeclarationListInfo, pos:PositionInCodeEx, showRestrictions:ShowRestrictions, checkAndMark:unit->unit) : TryShow =
        willInsert <- false

        //IFeshLog.log.PrintfnDebugMsg $"*3.0 TryShow Completion Window , {decls.Items.Length} items, onlyDU:{showRestrictions}:\r\n{pos}"
        if AutoFixErrors.isMessageBoxOpen then // because msg box would appear behind completion window and type info
            NoShow
        else
            let completionLines = this.CompletionLines(decls, pos, showRestrictions)
            if completionLines.Count = 0 then
                NoShow
            else
                let ta = avEd.TextArea
                let w =  new CodeCompletion.CompletionWindow(ta)
                let complList = w.CompletionList
                let complData =  complList.CompletionData
                for cln in completionLines do
                    complData.Add (cln)

                let caret = avEd.TextArea.Caret
                let stOff = pos.offset - pos.setback // just using w.StartOffset - setback would sometimes be one too big.( race condition of typing speed)
                // prefilter needs to be calculated here, a few characters might have been added after getCompletions started async.
                let prefilterLength = caret.Offset-stOff
                if prefilterLength < 0 then // caret moved back before start of completion window
                    this.CloseAndEnableReacting()
                    NoShow
                else
                    let prefilter = avEd.Document.GetText(stOff, prefilterLength )
                    //IFeshLog.log.PrintfnDebugMsg "*5.2: prefilter '%s'" prefilter

                    if prefilter.Length > 0 then
                        complList.SelectItem(prefilter) //to pre-filter the list by al typed characters
                        //IFeshLog.log.PrintfnDebugMsg "*5.3: count after SelectItem(prefilter): %d" complList.ListBox.Items.Count

                    if complList.ListBox.Items.Count > 0 // list is empty if prefilter does not match any item
                        && not AutoFixErrors.isMessageBoxOpen  // because msg box would appear behind completion window and type info
                        && IEditor.isCurrent avEd // switched to other editor
                        && caret.Line = pos.row // moved cursor to other line
                        && caret.Offset >= pos.offset then // moved cursor back before completion ( e.g. via deleting)

                            // Only now actually show the window:

                            w.MaxHeight <- 500 // default 300
                            w.Width <- 250 // default 175
                            //complList.Height <- 400.  // has  UI bug
                            //w.Height <- 400. // does not work
                            w.BorderThickness <- Thickness(0.0) //https://stackoverflow.com/questions/33149105/how-to-change-the-style-on-avalonedit-codecompletion-window
                            w.ResizeMode      <- ResizeMode.NoResize // needed to have no border!
                            w.WindowStyle     <- WindowStyle.None // = no border
                            w.SizeToContent   <- SizeToContent.WidthAndHeight // https://github.com/icsharpcode/AvalonEdit/blob/master/ICSharpCode.AvalonEdit/CodeCompletion/CompletionWindow.cs#L47
                            w.MinHeight       <- StyleState.fontSize
                            w.MinWidth        <- StyleState.fontSize * 8.0
                            w.CloseAutomatically <- true
                            w.CloseWhenCaretAtBeginning <- not pos.dotBefore
                            w.StartOffset <- stOff // to replace some previous characters too

                            let taCaretChanged  = new EventHandler(fun _ _ ->
                                match complList.ListBox.Items.Count with
                                | 0 -> w.Close()  //  close when list is empty. willInsert is still false so checkAndMark will be called in closing event handler
                                | 1 -> match complList.SelectedItem with // insert and close if there is an exact match and no other possible match available
                                        | null -> ()
                                        | it ->
                                                let textInWin = it.Text
                                                let len = textInWin.Length
                                                if avEd.Document.TextLength >= w.StartOffset + len then
                                                    let textInDoc = avEd.Document.GetText(w.StartOffset, textInWin.Length)
                                                    if textInWin = textInDoc then
                                                        complList.RequestInsertion(new EventArgs()) // this triggers a doc-changed event
                                | _ -> () // else keep window open
                                )

                            w.Closed.Add (fun _  -> // this gets called even if the window never shows up
                                    ta.Caret.PositionChanged.RemoveHandler taCaretChanged

                                    // Event sequence on pressing enter in completion window:
                                    // (1)raise InsertionRequested event
                                    // (2)in one of the event handlers first Closes window
                                    // (3)then on the item line this.Complete (TextArea, ISegment, EventArgs) is called and change the Document
                                    // https://github.com/goswinr/AvalonEditB/blob/main/AvalonEditB/CodeCompletion/CompletionWindow.cs#L110
                                    this.CloseAndEnableReacting()
                                    //IFeshLog.log.PrintfnDebugMsg "Completion window just closed with selected item: %A " complList.SelectedItem

                                    if not willInsert then // else -on inserting- a DocChanged event is triggered anyway that will do the checkAndMark
                                        checkAndMark()
                                    )
                            ta.Caret.PositionChanged.AddHandler taCaretChanged
                            complList.InsertionRequested.Add(fun _ -> willInsert <- true)


                            //IFeshLog.log.PrintfnDebugMsg "*5.4 Show Completion Window with %d items prefilter: '%s' " complList.ListBox.Items.Count prefilter
                            showingEv.Trigger() // to close error and type info tooltip
                            this.ComplWin <- Some w
                            w.Show()
                            DidShow
                    else
                        //IFeshLog.log.PrintfnDebugMsg "*5.5 Skipped showing empty Completion Window"
                        this.CloseAndEnableReacting() // needed, otherwise it will not show again
                        NoShow

    /// For closing and inserting from completion window
    member this.MaybeInsertOrClose  (ev:Input.TextCompositionEventArgs) =
        if this.IsOpen then
            // checking for Tab or Enter key is not needed  here for  insertion,
            // insertion with Tab or Enter key is built into Avalonedit!!

            match ev.Text with
            |" " -> this.CloseAndEnableReacting()

            // insert on dot too? //TODO only when more than one char is typed in completion window??
            |"." -> win.Value.CompletionList.RequestInsertion(ev)

            | _  -> () // other triggers like tab, enter and return are covered in   https://github.com/goswinr/AvalonEditB/blob/main/AvalonEditB/CodeCompletion/CompletionList.cs#L170

            // insert on open Bracket too?
            //|"(" -> compls.RequestInsertion(ev)
