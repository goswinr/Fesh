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
        if Selection.getSelType textArea = Selection.RectSel then       RectangleSelection.complete (textArea, completionSegment, text)
        else                                                            textArea.Document.Replace(completionSegment, text)

    interface ICompletionData with // needed in F#: implementing the interface members as properties too: https://github.com/icsharpcode/AvalonEdit/issues/28
        member this.Complete(t,s,e) = this.Complete(t,s,e)
        member this.Content         = this.Content
        member this.Description     = this.Description
        member this.Image           = this.Image
        member this.Priority        = this.Priority
        member this.Text            = this.Text

type CompletionItem(state: InteractionState, getToolTip, it:DeclarationListItem, isDotCompletion:bool) = 

    let style = 
        if it.IsOwnMember then FontStyles.Normal
        else match it.Glyph with    //new Font(FontFamily.GenericSansSerif,12.0F, FontStyle.Bold | FontStyle.Italic) // needs system.drawing
             | FSharpGlyph.Module | FSharpGlyph.NameSpace -> FontStyles.Normal
             | _                                          -> FontStyles.Italic

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
            RectangleSelection.complete (textArea, completionSegment, compl)
        else
            textArea.Document.Replace(completionSegment, compl)

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

type Completions(avaEdit:TextEditor, state: InteractionState) = 

    let mutable win : CompletionWindow option = None   
            
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
    member this.OnShowing = showingEv.Publish // to close other tooltips that might be open from type info
    member this.ShowingEv = showingEv // to trigger this event from the TryShow static member.
    
    /// To indicate that the stack panel is not showing the loading text but the actual type info 
    member val HasStackPanelTypeInfo = false with get, set

    member this.IsOpen = win.IsSome

    member this.IsNotOpen = win.IsNone

    /// Returns  win.CompletionList.ListBox.HasItems
    member this.HasItems = win.IsSome && win.Value.CompletionList.ListBox.HasItems

    member this.Close() = 
        if win.IsSome then
            win.Value.Close()
            win <- None        
        
    member this.RequestInsertion(ev:EventArgs) = if win.IsSome then win.Value.CompletionList.RequestInsertion(ev)    

    /// Initially returns "loading.." text and triggers async computation to get and update with actual text
    member this.GetToolTip(it:DeclarationListItem)= 
        this.HasStackPanelTypeInfo <-false
        async{
            let ttText = it.Description            
            let structured = 
                if Checker.OptArgsDict.ContainsKey it.FullName then  TypeInfo.makeSeffToolTipDataList (ttText, it.FullName, Checker.OptArgsDict.[it.FullName])
                else                                                 TypeInfo.makeSeffToolTipDataList (ttText, it.FullName, ResizeArray(0))
            if this.IsOpen then
                do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context
                if this.IsOpen then // might get closed during context switch
                    if selectedCompletionText() = it.NameInList then
                        win.Value.ToolTipContent <- TypeInfo.getPanel (structured, {declListItem=Some it; semanticClass=None; declLocation=None; dllLocation=None })
                        this.HasStackPanelTypeInfo <-true
                        //TODO add structure to a Dict so it does not need recomputing if browsing up and down items in the completion list.
        } |> Async.Start
        TypeInfo.loadingText :> obj
        

    member this.ComplWin  
        with get() : Option<CompletionWindow>  = win
        and set(w  : Option<CompletionWindow>) = win <- w

    
    member _.Editor = avaEdit
    
    
    //-----------------------------------------------------------------
    //---------------static members------------------------------------
    //-----------------------------------------------------------------

    static member TryShow( state: InteractionState, decls: DeclarationListInfo, compl:Completions, pos:PositionInCodeEx, onlyDU:bool) = 
              
        
        //let avaEdit = iEditor.AvaEdit
        
        //ISeffLog.log.PrintfnDebugMsg "*3.0 TryShow Completion Window for '%s'" pos.lineToCaret       
                   
        //let caret = avaEdit.TextArea.Caret
        
        if AutoFixErrors.isMessageBoxOpen then // because msg box would appear behind completion window and type info
            None 
        
        //elif caret.Offset < pos.offset then // DELETE
        //    ISeffLog.log.PrintfnDebugMsg "*4.2 caret.Offset < pos.offset "
        //    None
        //elif  caret.Line <> pos.row then 
        //    None //ISeffLog.log.PrintfnDebugMsg "*4.3 caret.Line <> pos.row "
        //elif IEditor.current.Value.Id <> iEditor.Id then // safety check just in case the fsharp checker took very long and this has changed in the meantime
        //    None //ISeffLog.log.PrintfnDebugMsg "*4.4 IEditor.current.Value.Id <> iEditor.Id "
        else    
            let completionLines = ResizeArray<ICompletionData>()
            if not onlyDU && not pos.dotBefore  then
                completionLines.Add( CompletionItemForKeyWord(state,"#if INTERACTIVE",     "Compiler directive to exclude code in compiled format, close with #endif or #else" ) :> ICompletionData)    |>ignore
                completionLines.Add( CompletionItemForKeyWord(state,"#if COMPILED",        "Compiler directive to exclude code in interactive format, close with #endif or #else" ) :> ICompletionData)    |>ignore
                completionLines.Add( CompletionItemForKeyWord(state,"#else",               "else of compiler directives " ) :> ICompletionData)    |>ignore
                completionLines.Add( CompletionItemForKeyWord(state,"#endif",              "End of compiler directive " ) :> ICompletionData)    |>ignore
                                                              
                completionLines.Add( CompletionItemForKeyWord(state,"__SOURCE_DIRECTORY__","Evaluates to the current full path of the source directory" ) :> ICompletionData)    |>ignore
                completionLines.Add( CompletionItemForKeyWord(state,"__SOURCE_FILE__"     ,"Evaluates to the current source file name, without its path") :> ICompletionData)    |>ignore
                completionLines.Add( CompletionItemForKeyWord(state,"__LINE__",            "Evaluates to the current line number") :> ICompletionData)    |>ignore
                for kw,desc in FSharpKeywords.KeywordsWithDescription  do // add keywords to list
                    completionLines.Add( CompletionItemForKeyWord(state,kw,desc) :> ICompletionData) |>ignore

            for it in decls.Items do
                if onlyDU then 
                    match it.Glyph with
                    |FSharpGlyph.Union |FSharpGlyph.Module |FSharpGlyph.EnumMember -> 
                        completionLines.Add (new CompletionItem(state, compl.GetToolTip, it, pos.dotBefore)) // for DU completion add just some.
                    |_ -> ()
                else 
                    completionLines.Add (new CompletionItem(state, compl.GetToolTip, it, pos.dotBefore)) // for normal completion add all others too.

            if completionLines.Count = 0 then
                //compl.Checker.CheckThenHighlightAndFold(iEditor)// start new full check, this one was trimmed at offset.
                None
            else                    
                
                let w =  new CodeCompletion.CompletionWindow(compl.Editor.TextArea)
                compl.ComplWin <- Some w 

                w.MaxHeight <- 500 // default 300
                w.Width <- 250 // default 175
                //w.CompletionList.Height <- 400.  // has  UI bug  
                //w.Height <- 400. // does not work               
                w.BorderThickness <- Thickness(0.0) //https://stackoverflow.com/questions/33149105/how-to-change-the-style-on-avalonedit-codecompletion-window
                w.ResizeMode      <- ResizeMode.NoResize // needed to have no border!
                w.WindowStyle     <- WindowStyle.None // = no border                
                w.SizeToContent   <- SizeToContent.WidthAndHeight // https://github.com/icsharpcode/AvalonEdit/blob/master/ICSharpCode.AvalonEdit/CodeCompletion/CompletionWindow.cs#L47
                w.MinHeight       <- StyleState.fontSize
                w.MinWidth        <- StyleState.fontSize * 8.0
                w.Closed.Add (fun _  -> 
                        compl.Close()
                        //ISeffLog.log.PrintfnDebugMsg "Completion window just closed with selected item: %A " w.CompletionList.SelectedItem
                        //if not UtilCompletion.justCompleted then // DELETE
                        //    compl.Checker.CheckThenHighlightAndFold(iEditor) //because window might close immediately after showing if there are no matches to prefilter
                        )

                w.CompletionList.SelectionChanged.Add(fun _ -> 
                    if w.CompletionList.ListBox.Items.Count = 0 then w.Close() ) // Close() then triggers CheckThenHighlightAndFold
                           
                w.CloseAutomatically <- true
                w.CloseWhenCaretAtBeginning <- false
                                        
                //ISeffLog.log.PrintfnDebugMsg "*5.1: pos.offset: %d , w.StartOffset %d , setback %d" pos.offset w.StartOffset setback                    
                let stOff = pos.offset - pos.setback // just using w.StartOffset - setback would sometimes be one too big.( race condition of typing speed)
                w.StartOffset <- stOff // to replace some previous characters too

                let complData =  w.CompletionList.CompletionData              
                for cln in completionLines do
                    complData.Add (cln)                    
                    
                let prefilter = compl.Editor.Document.GetText(stOff, compl.Editor.TextArea.Caret.Offset-stOff)// prefilter needs to be calculated here, a few characters might have been added after getCompletions started async.
                //ISeffLog.log.PrintfnDebugMsg "*5.2: prefilter '%s'" prefilter 
                if prefilter.Length > 0 then 
                    w.CompletionList.SelectItem(prefilter) //to pre-filter the list by al typed characters
                    //ISeffLog.log.PrintfnDebugMsg "*5.3: count after SelectItem(prefilter): %d" w.CompletionList.ListBox.Items.Count
                            
                if w.CompletionList.ListBox.Items.Count > 0 then 
                    //ISeffLog.log.PrintfnDebugMsg "*5.4 Show Completion Window with %d items prefilter: '%s' " w.CompletionList.ListBox.Items.Count prefilter
                     
                    compl.ShowingEv.Trigger() // to close error and type info tooltip                           
                    w.Show()
                    Some compl
                   
                else
                    //ISeffLog.log.PrintfnDebugMsg "*5.5 Skipped showing empty Completion Window"
                    compl.Close() // needed, otherwise it will not show again
                    //compl.Checker.CheckThenHighlightAndFold(iEditor) /// DELETE all CheckThenHighlightAndFold
                    None

                // Event sequence on pressing enter in completion window:
                // (1)Close window
                // (2)insert text into editor (triggers completion if one char only)
                // (3)raise InsertionRequested event
                // https://github.com/icsharpcode/AvalonEdit/blob/8fca62270d8ed3694810308061ff55c8820c8dfc/AvalonEditB/CodeCompletion/CompletionWindow.cs#L100
            


