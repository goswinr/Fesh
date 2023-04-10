namespace Seff.Editor

open System
open System.Windows
open System.Collections.Generic

open AvalonEditB
open AvalonEditB.CodeCompletion
open AvalonEditB.Editing
open AvalonEditB.Document

open FSharp.Compiler.EditorServices
open FSharp.Compiler.Tokenization // for keywords

open Seff
open Seff.Model
open Seff.Config
open System.Windows.Controls
open System.Windows.Media
open Seff.XmlParser


module UtilCompletion =     
    
    let mkTexBlock(txt,style) = // the displayed item in the completion window 
        let mutable tb = Controls.TextBlock()
        tb.Text <- txt
        tb.FontFamily <- Style.fontEditor
        tb.FontSize <-   Style.fontSize
        //tb.Foreground  <- col // fails on selection, does not get color inverted//check  https://blogs.msdn.microsoft.com/text/2009/08/28/selection-brush/ ??        
        tb.FontStyle <- style
        tb.Padding <- Thickness(0. , 0. , 8. , 0. ) //left top right bottom / so that it does not appear to be trimmed
        tb
    
    /// To avoid retrigger of completion window on single char completions
    /// the window may just have closed, but for pressing esc, not for completion insertion
    /// this is only true if it just closed for insertion
    let mutable  justCompleted = false 

type CompletionItemForKeyWord(ed:IEditor,config:Config, text:string, toolTip:string) =  
    let priority =  1.0 + config.AutoCompleteStatistic.Get(text)        // create once and cache ?
    let textBlock = UtilCompletion.mkTexBlock(text,FontStyles.Normal)   // create once and cache ?    

    member this.Content = textBlock :> obj
    member this.Description = toolTip :> obj
    member this.Image = null
    member this.Priority = priority
    member this.Text = text
    member this.Complete (textArea:TextArea, completionSegment:ISegment, e:EventArgs ) = 
        UtilCompletion.justCompleted <- true
        if Selection.getSelType textArea = Selection.RectSel then       RectangleSelection.complete (ed.AvaEdit, completionSegment, text)
        else                                                            textArea.Document.Replace(completionSegment, text)

    interface ICompletionData with // needed in F#: implementing the interface members as properties too: https://github.com/icsharpcode/AvalonEdit/issues/28
        member this.Complete(t,s,e) = this.Complete(t,s,e)
        member this.Content         = this.Content
        member this.Description     = this.Description
        member this.Image           = this.Image
        member this.Priority        = this.Priority
        member this.Text            = this.Text

type CompletionItem(ed:IEditor,config:Config, getToolTip, it:DeclarationListItem, isDotCompletion:bool) = 

    let style = 
        if it.IsOwnMember then FontStyles.Normal
        else match it.Glyph with    //new Font(FontFamily.GenericSansSerif,12.0F, FontStyle.Bold | FontStyle.Italic) // needs system.drawing
             | FSharpGlyph.Module | FSharpGlyph.NameSpace -> FontStyles.Normal
             | _                                          -> FontStyles.Italic

    let priority = //if it.IsOwnMember then 1. else 1.
        if isDotCompletion then 1.0 // not on Dot completion
        else                    1.0 + config.AutoCompleteStatistic.Get(it.NameInList) //if p>1.0 then log.PrintfnDebugMsg "%s %g" it.Name p    
    
    let textBlock = UtilCompletion.mkTexBlock(it.NameInList ,FontStyles.Normal)   // create once and cache ?  
        
    member this.Content = textBlock :> obj // the displayed item in the completion window 
    member this.Description = getToolTip(it) // this gets called on demand only, not when initially filling the list.
    member this.Image = null //TODO or part of text box ?
    member this.Priority = priority
    member this.Text = it.NameInList // not used for display, but for priority sorting ? 
    member this.Complete (textArea:TextArea, completionSegment:ISegment, e:EventArgs) = 
        //log.PrintfnDebugMsg "%s is %A and %A" it.Name it.Glyph it.Kind        
        UtilCompletion.justCompleted <- true
        let compl = 
            //TODO move this logic out here
            if it.Glyph = FSharpGlyph.Class && it.NameInList.EndsWith "Attribute" then
                "[<" + it.NameInList.Replace("Attribute",">]")

            // elif UtilCompletion.needsTicks it.Name then "``" + it.Name + "``" // fixed in FSharp.Compiler.Service 42.7.100 by using it.NameInCode 

            elif it.NameInList = "struct" then
                "[<Struct>]"
            else
                it.NameInCode // may include backticks

        //config.Log.PrintfDebugMsg "completionSegment: '%s' : %A" (textArea.Document.GetText(completionSegment)) completionSegment
        if Selection.getSelType textArea = Selection.RectSel then
            RectangleSelection.complete (ed.AvaEdit, completionSegment, compl)
        else
            textArea.Document.Replace(completionSegment, compl)

        if not isDotCompletion then
            config.AutoCompleteStatistic.Incr(it.NameInList)
            config.AutoCompleteStatistic.Save()
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

type Completions(avaEdit:TextEditor,config:Config, checker:Checker) = 

    let log = config.Log

    let mutable win : CompletionWindow option = None

    /// for adding question marks to optional arguments:
    let optArgsDict = new Dictionary<string,ResizeArray<OptDefArg>>() 

    let showingEv = Event<unit>()
        
    let selectedCompletionText ()= 
        match win with
        |None -> ""
        |Some w ->
            match w.CompletionList.SelectedItem with
            | null -> ""
            | i -> i.Text

    /// While we are waiting no new checker shall be triggered, 
    /// all typing during waiting for the checker should just become a  prefilter for the completion window
    static member val IsWaitingForTypeChecker = false with get,set
    
    /// To indicate that the stack panel is not showing the loading text but the actual type info 
    static member val HasStackPanelTypeInfo = false with get, set

    member this.IsOpen = win.IsSome

    member this.IsNotOpen = win.IsNone

    /// Returns  win.CompletionList.ListBox.HasItems
    member this.HasItems = win.IsSome && win.Value.CompletionList.ListBox.HasItems

    member this.Close() = 
        if win.IsSome then
            win.Value.Close()
            win <- None
        //Completions.JustClosed              <- true // to not trigger completion again on one letter completions
        Completions.IsWaitingForTypeChecker <- false
        
    member this.RequestInsertion(ev) = if win.IsSome then win.Value.CompletionList.RequestInsertion(ev)
    

    [<CLIEvent>] 
    member this.OnShowing = showingEv.Publish // to close other tooltips that might be open from type info
    member this.ShowingEv = showingEv

    /// Initially returns "loading.." text and triggers async computation to get and update with actual text
    member this.GetToolTip(it:DeclarationListItem)= 
        Completions.HasStackPanelTypeInfo <-false
        async{
            let ttText = it.Description            
            let structured = 
                if optArgsDict.ContainsKey it.FullName then  TypeInfo.makeSeffToolTipDataList (ttText, it.FullName, optArgsDict.[it.FullName])
                else                                         TypeInfo.makeSeffToolTipDataList (ttText, it.FullName, ResizeArray(0))
            if this.IsOpen then
                do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context
                if this.IsOpen then // might get closed during context switch
                    if selectedCompletionText() = it.NameInList then
                        win.Value.ToolTipContent <- TypeInfo.getPanel (structured, {declListItem=Some it; semanticClass=None; declLocation=None; dllLocation=None })
                        Completions.HasStackPanelTypeInfo <-true
                        //TODO add structure to a Dict so it does not need recomputing if browsing up and down items in the completion list.
        } |> Async.Start
        TypeInfo.loadingText :> obj
        
    member this.Log = log
    member this.Checker = checker
    member this.Config = config

    member this.ComplWin 
        with get() : Option<CompletionWindow>  = win
        and set(w  : Option<CompletionWindow>) = win <- w

    /// for a given method name returns a list of optional argument names
    member this.OptArgsDict = optArgsDict

    static member TryShow(iEditor:IEditor, compl:Completions, pos:PositionInCode , lastChar:char, setback:int, dotBefore:DotOrNot, onlyDU:bool) = 
        //a static method so that it can take an IEditor as argument        
        
        let config = compl.Config
        let avaEdit = iEditor.AvaEdit
        let ifDotSetback = if dotBefore = Dot then setback else 0
        //ISeffLog.log.PrintfnDebugMsg "*3.0 TryShow Completion Window for '%s'" pos.lineToCaret
        
        let continueOnUIthread (decls: DeclarationListInfo) =             
            let caret = avaEdit.TextArea.Caret
            let mutable checkingStoppedEarly0 = true
            if AutoFixErrors.isMessageBoxOpen then // because msg box would appear behind completion window and type info
                () //ISeffLog.log.PrintfnDebugMsg "*4.1 AutoFixErrors.isMessageBoxOpen "
            elif caret.Offset < pos.offset then 
                () //ISeffLog.log.PrintfnDebugMsg "*4.2 caret.Offset < pos.offset "
            elif  caret.Line <> pos.row then 
                () //ISeffLog.log.PrintfnDebugMsg "*4.3 caret.Line <> pos.row "
            elif IEditor.current.Value.Id <> iEditor.Id then // safety check just in case the fsharp checker took very long and this has changed in the meantime
                () //ISeffLog.log.PrintfnDebugMsg "*4.4 IEditor.current.Value.Id <> iEditor.Id "
            else    
                let completionLines = ResizeArray<ICompletionData>()
                if not onlyDU && dotBefore = NotDot then
                    completionLines.Add( CompletionItemForKeyWord(iEditor,config,"#if INTERACTIVE",     "Compiler directive to exclude code in compiled format, close with #endif or #else" ) :> ICompletionData)    |>ignore
                    completionLines.Add( CompletionItemForKeyWord(iEditor,config,"#if COMPILED",        "Compiler directive to exclude code in interactive format, close with #endif or #else" ) :> ICompletionData)    |>ignore
                    completionLines.Add( CompletionItemForKeyWord(iEditor,config,"#else",               "else of compiler directives " ) :> ICompletionData)    |>ignore
                    completionLines.Add( CompletionItemForKeyWord(iEditor,config,"#endif",              "End of compiler directive " ) :> ICompletionData)    |>ignore
                
                    completionLines.Add( CompletionItemForKeyWord(iEditor,config,"__SOURCE_DIRECTORY__","Evaluates to the current full path of the source directory" ) :> ICompletionData)    |>ignore
                    completionLines.Add( CompletionItemForKeyWord(iEditor,config,"__SOURCE_FILE__"     ,"Evaluates to the current source file name, without its path") :> ICompletionData)    |>ignore
                    completionLines.Add( CompletionItemForKeyWord(iEditor,config,"__LINE__",            "Evaluates to the current line number") :> ICompletionData)    |>ignore
                    for kw,desc in FSharpKeywords.KeywordsWithDescription  do // add keywords to list
                        completionLines.Add( CompletionItemForKeyWord(iEditor,config,kw,desc) :> ICompletionData) |>ignore

                for it in decls.Items do
                    if onlyDU then 
                        match it.Glyph with
                        |FSharpGlyph.Union |FSharpGlyph.Module |FSharpGlyph.EnumMember -> 
                            completionLines.Add (new CompletionItem(iEditor,config, compl.GetToolTip, it, (lastChar = '.'))) // for DU completion add just some.
                        |_ -> ()
                    else 
                        completionLines.Add (new CompletionItem(iEditor,config, compl.GetToolTip, it, (lastChar = '.'))) // for normal completion add all others too.

                if completionLines.Count = 0 then
                    compl.Checker.CheckThenHighlightAndFold(iEditor)// start new full check, this one was trimmed at offset.
                else                    
                    compl.ShowingEv.Trigger() // to close error and type info tooltip

                    let w =  new CodeCompletion.CompletionWindow(avaEdit.TextArea)
                    UtilCompletion.justCompleted <- false
                    compl.ComplWin <- Some w 

                    w.MaxHeight <- 500 // default 300
                    w.Width <- 250 // default 175
                    //w.CompletionList.Height <- 400.  // has  UI bug  
                    //w.Height <- 400. // does not work               
                    w.BorderThickness <- Thickness(0.0) //https://stackoverflow.com/questions/33149105/how-to-change-the-style-on-avalonedit-codecompletion-window
                    w.ResizeMode      <- ResizeMode.NoResize // needed to have no border!
                    w.WindowStyle     <- WindowStyle.None // = no border                
                    w.SizeToContent   <- SizeToContent.WidthAndHeight // https://github.com/icsharpcode/AvalonEdit/blob/master/ICSharpCode.AvalonEdit/CodeCompletion/CompletionWindow.cs#L47
                    w.MinHeight <- avaEdit.FontSize
                    w.MinWidth  <- avaEdit.FontSize * 8.0
                    w.Closed.Add (fun _  -> 
                            compl.Close()
                            //ISeffLog.log.PrintfnDebugMsg "Completion window just closed with selected item: %A " w.CompletionList.SelectedItem
                            if not UtilCompletion.justCompleted then 
                                compl.Checker.CheckThenHighlightAndFold(iEditor) //because window might close immediately after showing if there are no matches to prefilter
                            )

                    w.CompletionList.SelectionChanged.Add(fun _ -> 
                        if w.CompletionList.ListBox.Items.Count = 0 then w.Close() ) // Close() then triggers CheckThenHighlightAndFold
                           
                    w.CloseAutomatically <- true
                    w.CloseWhenCaretAtBeginning <- false
                                        
                    //ISeffLog.log.PrintfnDebugMsg "*5.1: pos.offset: %d , w.StartOffset %d , setback %d" pos.offset w.StartOffset setback                    
                    let stOff = pos.offset - setback // just using w.StartOffset - setback would sometimes be one too big.( race condition of typing speed)
                    w.StartOffset <- stOff // to replace some previous characters too

                    let complData =  w.CompletionList.CompletionData              
                    for cln in completionLines do
                        complData.Add (cln)                    
                    
                    let prefilter = avaEdit.Document.GetText(stOff, caret.Offset-stOff)// prefilter needs to be calculated here, a few characters might have been added after getCompletions started async.
                    //ISeffLog.log.PrintfnDebugMsg "*5.2: prefilter '%s'" prefilter 
                    if prefilter.Length > 0 then 
                        w.CompletionList.SelectItem(prefilter) //to pre-filter the list by al typed characters
                        //ISeffLog.log.PrintfnDebugMsg "*5.3: count after SelectItem(prefilter): %d" w.CompletionList.ListBox.Items.Count
                            
                    if w.CompletionList.ListBox.Items.Count > 0 then 
                        //ISeffLog.log.PrintfnDebugMsg "*5.4 Show Completion Window with %d items prefilter: '%s' " w.CompletionList.ListBox.Items.Count prefilter
                        try 
                            checkingStoppedEarly0 <- false                            
                            w.Show() 
                            //can be set false now because new checking is prevented by the now open completion window
                            Completions.IsWaitingForTypeChecker <- false
                        with 
                            e -> ISeffLog.log.PrintfnAppErrorMsg "Error in Showing Code Completion Window: %A" e
                    else
                        //ISeffLog.log.PrintfnDebugMsg "*5.5 Skipped showing empty Completion Window"
                        compl.Close() // needed, otherwise it will not show again
                        compl.Checker.CheckThenHighlightAndFold(iEditor)

                    // Event sequence on pressing enter in completion window:
                    // (1)Close window
                    // (2)insert text into editor (triggers completion if one char only)
                    // (3)raise InsertionRequested event
                    // https://github.com/icsharpcode/AvalonEdit/blob/8fca62270d8ed3694810308061ff55c8820c8dfc/AvalonEditB/CodeCompletion/CompletionWindow.cs#L100
            
            // do in any case    
            if checkingStoppedEarly0 then Completions.IsWaitingForTypeChecker <- false
        
        // this here is the only place where IsWaitingForTypeChecker is set true. 
        // Just before the completions show up.
        // it will be set false when the completion window closes or via the checkingStoppedEarly0 variable when the process of finding and opening gets cancelled for other reasons
        // like no completions found or prefilter no matching any.
        Completions.IsWaitingForTypeChecker <- true

        let stopWaiting = ( fun () -> Completions.IsWaitingForTypeChecker <- false)
        
        compl.Checker.GetCompletions(iEditor, pos, ifDotSetback, continueOnUIthread, compl.OptArgsDict, stopWaiting)

