namespace Fesh.Editor

open System
open System.Windows
open System.Windows.Threading
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

type TriedShow = DidShow | NoShow

type RestrictedShowList =
    | JustAllFrom of DeclarationListInfo
    | JustDuFrom of DeclarationListInfo
    | JustDeclModifiers
    | JustDirectives
    /// The previously used packages after  #r "nuget:  and the ones found on nuget.org
    | JustNugetPackages of found:NugetSearchResult[]


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

    let setTheme (w:CompletionWindow) =
        ThemeChrome.registerWindow w null Theme.darkChrome.popup
        // the ToolTip for the item descriptions has no parent to inherit the theme from, so it needs to be registered too:
        ThemeChrome.register w.DescriptionToolTip

    let isAtLineStart (completionSegment:ISegment) (textArea:TextArea) =
        let doc = textArea.Document
        let rec loop (off:int) =
            if off = 0 then true // at start of line
            else
                let c = doc.GetCharAt(off)
                if c = ' ' then
                    loop (off - 1)
                elif c = '\n' || c = '\r' then
                    true
                else
                    false
        loop (completionSegment.Offset - 1)

open UtilCompletion

type CompletionInfo =
    | Decl    of decl:DeclarationListItem * getToolTip:(DeclarationListItem -> obj) // getToolTip is a function that returns the tooltip text
    | KeyWord of text:string * toolTip:string

type CompletionItem(state: InteractionState, info:CompletionInfo , isDotCompletion:bool) =

    //let style =
    //    if it.IsOwnMember then FontStyles.Normal
    //    else
    //        match it.Glyph with    //new Font(FontFamily.GenericSansSerif,12.0F, FontStyle.Bold | FontStyle.Italic) // needs system.drawing
    //        | FSharpGlyph.Module | FSharpGlyph.NameSpace -> FontStyles.Normal
    //        | _                                          -> FontStyles.Italic

    let priority = //if it.IsOwnMember then 1. else 1.
        if isDotCompletion then 1.0 // not on Dot completion
        else
            match info with
            | Decl (it,_)      ->  1.0 + state.Config.AutoCompleteStatistic.Get(it.NameInList) //if p>1.0 then log.PrintfnDebugMsg "%s %g" it.Name p
            | KeyWord (text,_) ->  1.0 + state.Config.AutoCompleteStatistic.Get(text) // create once and cache ?

    let textVal, textDisplay=
        match info with
        | Decl (it,_)      -> it.NameInList ,UtilCompletion.mkTexBlock(it.NameInList , FontStyles.Normal)   // create once and cache ?
        | KeyWord (text,_) -> text          ,UtilCompletion.mkTexBlock(text , FontStyles.Normal)   // create once and cache ?

    member this.Content = textDisplay :> obj // the displayed item in the completion window
    member this.Description =
        match info with
        | Decl    (it, getToolTip)  -> getToolTip(it) // this gets called on demand only, not when initially filling the list.
        | KeyWord (_ , toolTip)      -> toolTip :> obj

    member this.Image = null //TODO or part of text box ?
    member this.Priority = priority
    member this.Text = textVal // not used for display, but for priority sorting ?
    member this.Complete (textArea:TextArea, completionSegment:ISegment, e:EventArgs) =
        ignore e // because of warn on unused variables and , needed in F#: implementing the interface members as properties too: https://github.com/icsharpcode/AvalonEdit/issues/28
        //log.PrintfnDebugMsg "%s is %A and %A" it.Name it.Glyph it.Kind
        state.JustCompleted <- true
        let mutable complText =
            match info with
            | KeyWord (txt,_) -> txt
            | Decl (it,_)    ->
                //TODO move this logic out here
                if it.Glyph = FSharpGlyph.Class && it.NameInList.EndsWith "Attribute" then
                    "[<" + it.NameInList.Replace("Attribute",">]")

                // elif UtilCompletion.needsTicks it.Name then "``" + it.Name + "``" // fixed in FSharp.Compiler.Service 42.7.100 by using it.NameInCode

                elif it.NameInList = "struct" then
                    "[<Struct>]"
                else
                    it.NameInCode // may include backticks

        // to maybe add '()' at end of word if this is a function taking unit:
        let taggedTextSig =
            match info with
            | KeyWord _ -> None
            | Decl (it,_)    ->
                match it.Description with
                | ToolTipText.ToolTipText (els) ->
                    match els with
                    |[]  -> None
                    |[el] ->
                            match el with
                            | ToolTipElement.None -> None
                            | ToolTipElement.CompositionError _  -> None
                            | ToolTipElement.Group(tooTipElemDataList) ->
                                match tooTipElemDataList with
                                |[]  -> None
                                |[eld] -> Some eld.MainDescription
                                | _ -> None // there are multiple signatures
                    | _ -> None // there are multiple signatures

        match taggedTextSig with
        |None -> ()
        |Some ts ->
            if ts.Length >= 7
            && ts.Length < 25  // < 25 to skip long list of class definitions
            && ts.[ts.Length-5].Text = "unit"
            && ts.[ts.Length-3].Text = "->"
            && ts.[ts.Length-1].Text = "unit"
            && ts.[ts.Length-7].Text = ":"
            && isAtLineStart completionSegment textArea
            && completionSegment.Length < complText.Length then // the full text is not yet typed in, and the two '()' wher not just deleted
                complText <- complText + "()"

        let doc = textArea.Document

        // IFeshLog.log.PrintfnDebugMsg "completionSegment: '%s' : %A" (textArea.Document.GetText(completionSegment)) completionSegment

        // Extend the completionSegment to the left to replace an existing word
        // if typing stated inside a word. // This deletes characters that should be kept in some cases?
        let extendedSegment = completionSegment
            // let prevChar2 = doc.GetCharAt(max 0 (textArea.Caret.Offset - 2))
            // let prevChar3 = doc.GetCharAt(max 0 (textArea.Caret.Offset - 3))
            // let inline isIdent c  = Char.IsLetterOrDigit c || c = '_' || c = '`' || c = ''' // al characters that can make up a name in F#
            // // IFeshLog.log.PrintfnDebugMsg $"prevChar '{prevChar}'"
            // if isIdent prevChar2 && isIdent prevChar3 then // test if this completion is  started inside a word || prevChar = '.'
            //     // Extend the completionSegment to the left to include the whole word:
            //     let mutable e = completionSegment.EndOffset
            //     let mutable k = 0
            //     while k < 20 && e < doc.TextLength && isIdent(doc.GetCharAt(e)) do // don't extend by more than 20 chars
            //         e <- e + 1
            //         k <- k + 1
            //     ISegment.FormTill(completionSegment.Offset, e)
            // else
            //     completionSegment

        // replace in the document:
        if Selection.getSelType textArea = Selection.RectSel then
            RectangleSelection.complete (textArea, completionSegment, complText) // don't us the expended segment here
        else
            doc.Replace(extendedSegment, complText)

        if not isDotCompletion then
            state.Config.AutoCompleteStatistic.Incr(textVal)
            state.Config.AutoCompleteStatistic.Save()

        // Event sequence on pressing enter in completion window:
        // (1) Close window
        // (2) Insert text into editor (triggers completion window again if it was one char only)
        // (3) Raise InsertionRequested event
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

/// An item in the completion list after  #r "nuget:
/// usedCount: how often the package was used before, 0 if it was only found on nuget.org.
type NugetCompletionItem(state: InteractionState, name:string, usedCount:int, found:NugetSearchResult option) =

    let textDisplay = UtilCompletion.mkTexBlock(name, FontStyles.Normal)

    let description = lazy (
        let lines = ResizeArray<string>()
        match usedCount with
        | 0 -> ()
        | 1 -> lines.Add "Used once before."
        | n -> lines.Add $"Used {n} times before."
        match found with
        | None -> ()
        | Some r ->
            lines.Add $"On nuget.org: latest version {r.version}, {r.downloads:N0} downloads."
            if r.description <> "" then
                lines.Add ""
                lines.Add (if r.description.Length > 400 then r.description.Substring(0, 400) + " …" else r.description)
        TextBlock(Text = String.Join("\r\n", lines), TextWrapping = TextWrapping.Wrap, MaxWidth = 500.0) :> obj
        )

    member this.Content = textDisplay :> obj
    member this.Description = description.Value
    member this.Image = null
    member this.Priority = 1.0 + float usedCount
    member this.Text = name
    member this.Complete (textArea:TextArea, completionSegment:ISegment, e:EventArgs) =
        ignore e
        state.JustCompleted <- false // so that the next typed character, like a backspace, can show the packages again
        let doc = textArea.Document
        let start = completionSegment.Offset // before the Replace, the anchored segment might move with the insertion
        // also replace the rest of the package name after the caret:
        let lineEnd = doc.GetLineByOffset(completionSegment.EndOffset).EndOffset
        let mutable en = completionSegment.EndOffset
        while en < lineEnd && NugetDirective.isPackageIdChar (doc.GetCharAt en) do
            en <- en + 1
        doc.Replace(start, en - start, name)
        textArea.Caret.Offset <- start + name.Length
        state.Config.NugetStatistic.Incr name
        state.Config.NugetStatistic.Save()

    override this.ToString() = name // for UI Automation, e.g. screen readers

    interface ICompletionData with
        member this.Complete(t,s,e) = this.Complete(t,s,e)
        member this.Content         = this.Content
        member this.Description     = this.Description
        member this.Image           = this.Image
        member this.Priority        = this.Priority
        member this.Text            = this.Text

type Completions(state: InteractionState) =
    let avEd = state.Editor

    let mutable win : CompletionWindow option = None

    let mutable willInsert = false // to track if window closed without inserting ( e.g pressing esc)

    //let empty = ResizeArray<string>()

    let selectedCompletionText ()=
        match win with
        |None -> ""
        |Some w ->
            match w.CompletionList.SelectedItem with
            | null -> ""
            | i -> i.Text

    // to close other tooltips that might be open from type info
    let showingEv = Event<unit>()

    let hashDirectives =
        let lines = ResizeArray<ICompletionData>()
        lines.Add( CompletionItem(state,KeyWord("#if INTERACTIVE",     "Compiler directive to exclude code in compiled format, close with #endif or #else" ), false)     :> ICompletionData)
        lines.Add( CompletionItem(state,KeyWord("#if COMPILED",        "Compiler directive to exclude code in interactive format, close with #endif or #else" ), false)  :> ICompletionData)
        lines.Add( CompletionItem(state,KeyWord("#else",               "else of compiler directives " ), false)                                                          :> ICompletionData)
        lines.Add( CompletionItem(state,KeyWord("#endif",              "End of compiler directive " ), false)                                                            :> ICompletionData)
        lines.Add( CompletionItem(state,KeyWord("#r \"nuget: \"",      "Nuget package reference " ), false)                                                              :> ICompletionData)
        lines.Add( CompletionItem(state,KeyWord("#r \"\"",             "Dll file reference " ), false)                                                                   :> ICompletionData)
        lines.Add( CompletionItem(state,KeyWord("#I \"\"",             "Dll folder reference " ), false)                                                                 :> ICompletionData)
        lines.Add( CompletionItem(state,KeyWord("#load \"\"",          "fsx file reference " ), false)                                                                   :> ICompletionData)
        lines

    let underscore =
        let lines = ResizeArray<ICompletionData>()
        lines.Add( CompletionItem(state,KeyWord("__SOURCE_DIRECTORY__","Evaluates to the current full path of the source directory" ), false) :> ICompletionData)
        lines.Add( CompletionItem(state,KeyWord("__SOURCE_FILE__"     ,"Evaluates to the current source file name, without its path"), false) :> ICompletionData)
        lines.Add( CompletionItem(state,KeyWord("__LINE__",            "Evaluates to the current line number"), false)                        :> ICompletionData)
        lines

    /// Initially returns "loading.." text and triggers async computation to get and update with actual text
    let getToolTip (it:DeclarationListItem) : obj =
        async{
            let ttText = it.Description
            let structured = TypeInfo.makeFeshToolTipDataList (ttText, it.FullName)
                // if Checker.OptArgsDict.ContainsKey it.FullName then  TypeInfo.makeFeshToolTipDataList (ttText, it.FullName, Checker.OptArgsDict.[it.FullName])
                // else                                                 TypeInfo.makeFeshToolTipDataList (ttText, it.FullName, empty)
            do! Async.SwitchToContext Fittings.SyncWpf.context
            if win.IsSome then // might get closed during context switch
                if selectedCompletionText() = it.NameInList then
                    win.Value.ToolTipContent <- TypeInfo.getPanel (structured, {declListItem=Some it; semanticClass=None; declLocation=None; dllLocation=None })
                    //TODO add structure to a Dict so it does not need to be recomputed if browsing up and down items in the completion list?
            //     else
            //         IFeshLog.log.PrintfnDebugMsg $"getToolTip: '{selectedCompletionText()}' <> '{it.NameInList}'"
            // else
            //     IFeshLog.log.PrintfnDebugMsg $"win.IsSome2: '{win.IsSome}'"
        } |> Async.Start
        TypeInfo.loadingText :> obj

    /// Is the current completion window the one for the NuGet packages after  #r "nuget:
    let mutable isNugetWin = false

    /// The text typed so far since the completion window started.
    let typedText (w:CompletionWindow) =
        let off = avEd.TextArea.Caret.Offset
        if off > w.StartOffset then avEd.Document.GetText(w.StartOffset, off - w.StartOffset) else ""

    /// Long enough to search for it on nuget.org, and could be (the start of) a package id.
    let isNugetQuery (txt:string) =
        txt.Length >= NugetSearch.MinQueryLength && Seq.forall NugetDirective.isPackageIdChar txt

    /// For the completion window of the NuGet packages:
    /// When typing pauses, search nuget.org for the typed text and add the found packages to the list.
    let searchNugetWhileOpen (w:CompletionWindow) =
        let complList = w.CompletionList
        let searched = HashSet<string>(StringComparer.OrdinalIgnoreCase)

        let addFound (found:NugetSearchResult[]) =
            let have = HashSet<string>(complList.CompletionData |> Seq.map (fun d -> d.Text), StringComparer.OrdinalIgnoreCase)
            let mutable added = false
            for r in found do
                if have.Add r.id then
                    complList.CompletionData.Add(NugetCompletionItem(state, r.id, 0, Some r) :> ICompletionData)
                    added <- true
            if added then
                // SelectItem only filters again if the text changed, a space matches no package id:
                complList.SelectItem " "
                complList.SelectItem (typedText w)

        let search () =
            let query = typedText w
            if isNugetQuery query && searched.Add query then
                async {
                    let! found = NugetSearch.Search query |> Async.AwaitTask
                    do! Async.SwitchToContext Fittings.SyncWpf.context
                    match win with
                    | Some cw when Object.ReferenceEquals(cw, w) -> addFound found
                    | _ -> () // closed meanwhile
                } |> Async.Start

        let timer = DispatcherTimer(DispatcherPriority.Background, Interval = TimeSpan.FromMilliseconds 300.0)
        timer.Tick.Add(fun _ -> timer.Stop(); search())
        let restartTimer = EventHandler(fun _ _ -> timer.Stop(); timer.Start())
        avEd.TextArea.Caret.PositionChanged.AddHandler restartTimer
        w.Closed.Add(fun _ ->
            timer.Stop()
            avEd.TextArea.Caret.PositionChanged.RemoveHandler restartTimer)
        search() // for the text typed while waiting for the window to show

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

    member _.ComplWin
        with get() : Option<CompletionWindow>  = win
        and set(w:Option<CompletionWindow>) = win <- w


    member val justKeyWords  =
        let lines = ResizeArray<ICompletionData>()
        for kw,desc in FSharpKeywords.KeywordsWithDescription  do // add keywords to list
            if kw.StartsWith "pri" || kw.StartsWith "mut" || kw.StartsWith "inl" || kw.StartsWith "int" then
                lines.Add( CompletionItem(state, KeyWord(kw,desc), false) :> ICompletionData )
        lines

    member val directives =
        hashDirectives

    member val justAll =
        let lines = ResizeArray<ICompletionData>()
        for kw,desc in FSharpKeywords.KeywordsWithDescription  do // add keywords to list
            lines.Add( CompletionItem(state, KeyWord(kw,desc), false) :> ICompletionData )
        lines.AddRange hashDirectives
        lines.AddRange underscore
        lines

    member this.MakeCompletionLines (  pos:PositionInCodeEx, rShowList:RestrictedShowList ) : ResizeArray<ICompletionData> =
        match rShowList with
        |JustDeclModifiers ->
            this.justKeyWords
        |JustDirectives ->
            this.directives
        |JustDuFrom decls ->
            let lines = ResizeArray<ICompletionData>(decls.Items.Length)
            for it in decls.Items do
                match it.Glyph with
                |FSharpGlyph.Union |FSharpGlyph.Module |FSharpGlyph.EnumMember ->
                    lines.Add (new CompletionItem(state, Decl(it, getToolTip ), pos.dotBefore)) // for DU completion add just some.
                |_ -> ()
            lines
        |JustAllFrom decls ->
            let kws = this.justAll
            let lines = ResizeArray<ICompletionData>(decls.Items.Length + kws.Count)
            if not pos.dotBefore then
                lines.AddRange kws
            for it in decls.Items do
                lines.Add (new CompletionItem(state, Decl(it, getToolTip ), pos.dotBefore)) // for normal completion add all others too.
            lines
        |JustNugetPackages found ->
            let foundById = Dictionary<string, NugetSearchResult>(StringComparer.OrdinalIgnoreCase)
            for r in found do
                if not (foundById.ContainsKey r.id) then foundById[r.id] <- r
            let lines = ResizeArray<ICompletionData>()
            for struct(name, count) in state.Config.NugetStatistic.Packages do // the most used first
                match foundById.TryGetValue name with
                | true, r ->
                    foundById.Remove name |> ignore
                    lines.Add (NugetCompletionItem(state, name, count, Some r))
                | _ ->
                    lines.Add (NugetCompletionItem(state, name, count, None))
            for r in found do // in the order of relevance from nuget.org
                if foundById.Remove r.id then
                    lines.Add (NugetCompletionItem(state, r.id, 0, Some r))
            lines

    /// must be called from UI thread
    /// searchAgain is called for the NuGet packages, when the window closes because the typed text matches none of them,
    /// so that nuget.org gets searched for the typed text.
    member this.TryShow( posX:PositionInCodeEx, rShowList:RestrictedShowList, checkAndMark:unit->unit, searchAgain:unit->unit) : TriedShow =
        willInsert <- false
        let isNuget = match rShowList with JustNugetPackages _ -> true | _ -> false
        // IFeshLog.log.PrintfnDebugMsg $"*5.0 TryShow Completion Window , {rShowList} items, onlyDU:{rShowList}:\r\n{pos}"
        if AutoFixErrors.isMessageBoxOpen then // because msg box would appear behind completion window and type info
            NoShow
        else
            let completionLines = this.MakeCompletionLines( posX, rShowList)
            // IFeshLog.log.PrintfnDebugMsg $"*5.1: CompletionLines with {completionLines.Count} items"
            // for l in completionLines do IFeshLog.log.PrintfnDebugMsg "*5.2 CompletionLine: %s" l.Text
            if completionLines.Count = 0 then
                NoShow
            else
                let ta = avEd.TextArea
                let w =  new CodeCompletion.CompletionWindow(ta)
                setTheme w
                let complList = w.CompletionList
                let complData =  complList.CompletionData
                for cln in completionLines do
                    complData.Add (cln)

                let caret = avEd.TextArea.Caret
                let stOff = posX.offset - posX.setback // just using w.StartOffset - setback would sometimes be one too big.( race condition of typing speed)
                // prefilter needs to be calculated here, a few characters might have been added after getCompletions started async.
                let prefilterLength = caret.Offset-stOff
                if prefilterLength < 0 then // caret moved back before start of completion window
                    this.CloseAndEnableReacting()
                    NoShow
                else
                    let prefilter = avEd.Document.GetText(stOff, prefilterLength )
                    //IFeshLog.log.PrintfnDebugMsg "*5.2.1: prefilter '%s'" prefilter

                    if prefilter.Length > 0 then
                        complList.SelectItem prefilter //to pre-filter the list by al typed characters
                        //IFeshLog.log.PrintfnDebugMsg "*5.3: count after SelectItem(prefilter): %d" complList.ListBox.Items.Count

                    if complList.ListBox.Items.Count > 0 // list is empty if prefilter does not match any item
                        && not AutoFixErrors.isMessageBoxOpen  // because msg box would appear behind completion window and type info
                        && IEditor.isCurrent avEd // switched to other editor
                        && caret.Line = posX.lineIdx // moved cursor to other line
                        && caret.Offset >= posX.offset then // moved cursor back before completion ( e.g. via deleting)

                            // Only now actually show the window:
                            w.MaxHeight <- 500 // default 300
                            w.Width <- 250 // default 175
                            //complList.Height <- 400.  // has  UI bug
                            //w.Height <- 400. // does not work
                            w.BorderThickness <- Thickness 0.0 //https://stackoverflow.com/questions/33149105/how-to-change-the-style-on-avalonedit-codecompletion-window
                            w.ResizeMode      <- ResizeMode.NoResize // needed to have no border!
                            w.WindowStyle     <- WindowStyle.None // = no border
                            w.SizeToContent   <- SizeToContent.WidthAndHeight // https://github.com/icsharpcode/AvalonEdit/blob/master/ICSharpCode.AvalonEdit/CodeCompletion/CompletionWindow.cs#L47
                            w.MinHeight       <- StyleState.fontSize
                            w.MinWidth        <- StyleState.fontSize * 8.0
                            w.CloseAutomatically <- true
                            w.CloseWhenCaretAtBeginning <- not posX.dotBefore && not isNuget // for NuGet packages show all again when deleting back to the start of the name
                            w.StartOffset <- stOff // to replace some previous characters too

                            let searchAgainOnClose = ref false

                            let checkIfOnlyOneMatch  = fun() ->
                                match complList.ListBox.Items.Count with
                                | 0 ->
                                    if isNuget && isNugetQuery (typedText w) then
                                        searchAgainOnClose.Value <- true // nuget.org might have other packages for the longer text
                                    w.Close()  //  close when list is empty. 'willInsert' is still false so checkAndMark (or searchAgain) will be called in closing event handler
                                | 1 when isNuget -> () // keep it open, a package name is often the start of other ones, like Euclid and Euclid.Rhino
                                | 1 ->  match complList.SelectedItem with // insert and close if there is an exact match and no other possible match available
                                        | null -> ()
                                        | it ->
                                            let textInWin = it.Text
                                            let len = textInWin.Length
                                            if avEd.Document.TextLength >= w.StartOffset + len then
                                                let textInDoc = avEd.Document.GetText(w.StartOffset, textInWin.Length)
                                                if textInWin = textInDoc then
                                                    complList.RequestInsertion(new EventArgs()) // this triggers a doc-changed event
                                | _ -> () // else keep window open

                            let autoCloseOnCaretChanged = EventHandler(fun _ _ -> checkIfOnlyOneMatch())
                            ta.Caret.PositionChanged.AddHandler autoCloseOnCaretChanged
                            w.Closed.Add (fun _  -> // this gets called even if the window never shows up
                                ta.Caret.PositionChanged.RemoveHandler autoCloseOnCaretChanged

                                // Event sequence on pressing enter in completion window:
                                // (1) raise InsertionRequested event
                                // (2) in one of the event handlers first Closes window
                                // (3) then on the item line this.Complete (TextArea, ISegment, EventArgs) is called and change the Document
                                // https://github.com/goswinr/AvalonEditB/blob/main/AvalonEditB/CodeCompletion/CompletionWindow.cs#L110
                                this.CloseAndEnableReacting()
                                //IFeshLog.log.PrintfnDebugMsg "Completion window just closed with selected item: %A " complList.SelectedItem

                                if not willInsert then // else -on inserting- a DocChanged event is triggered anyway that will do the checkAndMark
                                    if searchAgainOnClose.Value then
                                        searchAgain() // also does the checkAndMark if nothing is found
                                    else
                                        checkAndMark()
                                )

                            complList.InsertionRequested.Add(fun _ -> willInsert <- true)


                            if complList.ListBox.Items.Count = 1 then
                                complList.SelectedItem <- unbox complList.ListBox.Items.[0] //complData.[0] // select the only item if there is just one


                            //IFeshLog.log.PrintfnDebugMsg "*5.4 Show Completion Window with %d items prefilter: '%s' " complList.ListBox.Items.Count prefilter
                            showingEv.Trigger() // to close error and type info tooltip
                            this.ComplWin <- Some w
                            isNugetWin <- isNuget
                            if isNuget then
                                searchNugetWhileOpen w // before Show, it hooks up to the Closed event
                            w.Show()
                            checkIfOnlyOneMatch()
                            DidShow
                    else
                        //IFeshLog.log.PrintfnDebugMsg "*5.5 Skipped showing empty Completion Window"
                        this.CloseAndEnableReacting() // needed, otherwise it will not show again
                        NoShow

    /// For closing and inserting from completion window
    member this.MaybeInsertOrClose (ev:Input.TextCompositionEventArgs) =
        if this.IsOpen then
            // checking for Tab or Enter key is not needed  here for  insertion,
            // insertion with Tab or Enter key is built into Avalonedit!!

            match ev.Text with
            // |"\t" -> built in by AvalonEdit
            |" " -> this.CloseAndEnableReacting()

            // insert on dot too? //TODO only when more than one char is typed in completion window??
            |"." ->
                match this.ComplWin.Value.CompletionList.SelectedItem with
                | null -> () // don't insert __LINE__ when doing an underscore and a dot in the new shorthand meber acces like: xs |> Array.map _.Length
                | _ when isNugetWin -> () // the dot is part of the package name
                | i -> if not <|  i.Text.StartsWith "_" then win.Value.CompletionList.RequestInsertion(ev)

            | _  -> () // other triggers like tab, enter and return are covered in   https://github.com/goswinr/AvalonEditB/blob/main/AvalonEditB/CodeCompletion/CompletionList.cs#L170

            // insert on open Bracket too?
            //|"(" -> compls.RequestInsertion(ev)
