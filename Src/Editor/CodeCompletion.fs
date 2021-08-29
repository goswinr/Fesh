namespace Seff.Editor

open System
open System.Windows
open System.Collections.Generic

open AvalonEditB
open AvalonEditB.CodeCompletion
open AvalonEditB.Editing
open AvalonEditB.Document

open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.EditorServices
open FSharp.Compiler.Tokenization // for keywords

open Seff
open Seff.Model
open Seff.Config

type CompletionItemForKeyWord(ed:IEditor,config:Config, text:string, toolTip:string) =
    //let col = Brushes.DarkBlue    // fails on selection, does not get color inverted//check  https://blogs.msdn.microsoft.com/text/2009/08/28/selection-brush/ ??
                
    let style = FontStyles.Normal
    let tb = 
        let mutable tb = Controls.TextBlock()
        tb.Text <- text
        tb.FontFamily <- Style.fontEditor  
        tb.FontSize <-   Style.fontSize 
        //tb.Foreground  <- col, // does not change color when selected anymore
        tb.FontStyle <- style
        tb.Padding <- Thickness(0. , 0. , 8. , 0. ) //left top right bottom / so that it does not aper to be trimmed
        tb
        
    let priority =  1.0 + config.AutoCompleteStatistic.Get(text) 
        
    member this.Content = tb :> obj
    member this.Description = toolTip :> obj 
    member this.Image = null
    member this.Priority = priority
    member this.Text = text
    member this.Complete (textArea:TextArea, completionSegment:ISegment, e:EventArgs ) =       
        if Selection.getSelType textArea = Selection.RectSel then 
            RectangleSelection.complete (ed, completionSegment, text)
        else
            textArea.Document.Replace(completionSegment, text) 
        

    interface ICompletionData with // needed in F#: implementing the interface members as properties too: https://github.com/icsharpcode/AvalonEdit/issues/28
        member this.Complete(t,s,e) = this.Complete(t,s,e)            
        member this.Content         = this.Content 
        member this.Description     = this.Description
        member this.Image           = this.Image           
        member this.Priority        = this.Priority
        member this.Text            = this.Text

type CompletionItem (ed:IEditor,config:Config, getToolTip, it:DeclarationListItem, isDotCompletion:bool) =

    let style =         
        if it.IsOwnMember then FontStyles.Normal 
        else match it.Glyph with    //new Font(FontFamily.GenericSansSerif,12.0F, FontStyle.Bold | FontStyle.Italic) // needs system.drawing
                | FSharpGlyph.Module | FSharpGlyph.NameSpace -> FontStyles.Normal 
                | _                                          -> FontStyles.Italic
    let tb = 
        let mutable tb = Controls.TextBlock()
        tb.Text <- it.Name 
        tb.FontFamily <- Style.fontEditor  
        tb.FontSize <-   Style.fontSize 
        //tb.Foreground  <- col, // does not change color when selected anymore
        tb.FontStyle <- style
        tb.Padding <- Thickness(0. , 0. , 8. , 0. ) //left top right bottom / so that it does not apear to be trimmed
        tb

    let priority = //if it.IsOwnMember then 1. else 1. 
        if isDotCompletion then 1.0// not on Dot completion             
        else                    1.0 + config.AutoCompleteStatistic.Get(it.Name) //if p>1.0 then log.PrintfnDebugMsg "%s %g" it.Name p
    
    let needTicks = 
        [|' '  ; '-' ; '+' ; '*' ; '/' ; '=' ; ',' ; ';' ; '~' ; '%' ; '&' ; '@' ; '#' ; '$' ;
          '\\' ; '|' ; '!' ; '?' ; '(' ; ')' ; '[' ; ']' ; '<' ; '>' ; '{' ; '}' |]
    
    

    member this.Content = tb :> obj
    member this.Description = getToolTip(it) // this gets called on demand only, not when initally filling the list.
    member this.Image = null //TODO
    member this.Priority = priority
    member this.Text = it.Name
    member this.Complete (textArea:TextArea, completionSegment:ISegment, e:EventArgs) = 
        //log.PrintfnDebugMsg "%s is %A and %A" it.Name it.Glyph it.Kind
        //textArea.Document.Replace(completionSegment.Offset + 1, completionSegment.Length, it.Name) //TODO Delete!
        //textArea.Caret.Offset <- completionSegment.Offset + it.Name.Length + 1  //TODO Delete!          
        let compl = 
            //TODO move this logic out here 
            if it.Glyph = FSharpGlyph.Class && it.Name.EndsWith "Attribute" then 
                "[<" + it.Name.Replace("Attribute",">]")
                
            elif it.Name.IndexOfAny needTicks >= 0 then 
                "``" + it.Name + "``"

            elif it.Name = "struct" then 
                "[<Struct>]"
            else 
                it.Name  
                
        //config.Log.PrintfDebugMsg "completionSegment: '%s' : %A" (textArea.Document.GetText(completionSegment)) completionSegment
        if Selection.getSelType textArea = Selection.RectSel then 
            RectangleSelection.complete (ed, completionSegment, compl)
        else
            textArea.Document.Replace(completionSegment, compl) 
        
        if not isDotCompletion then 
            config.AutoCompleteStatistic.Incr(it.Name)
            config.AutoCompleteStatistic.Save()
        // Event sequence on pressing enter in completion window:
        // (1)Close window
        // (2)insert text into editor (triggers completion if one char only)
        // (3)raise InsertionRequested event
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
        member this.Text            = this.Text 
            

type Completions(avaEdit:TextEditor,config:Config, checker:Checker) = 
    
    let log = config.Log

    let mutable win : CompletionWindow option = None
    
    /// for adding question marks to optional arguments:
    let optArgsDict = Dictionary() //TODO should this be global ?

    let showingEv = Event<unit>()

    let mutable justClosed = false // to avoid retrigger of window on single char completions

    let mutable hasStackPanelTypeInfo = false // to avoid retrigger of window on single char completions

    let selectedText ()= 
        match win with 
        |None -> ""
        |Some w ->
            match w.CompletionList.SelectedItem with
            | null -> ""
            | i -> i.Text
    
    member this.IsOpen = win.IsSome

    member this.IsNotOpen = win.IsNone
    
    /// returns  win.CompletionList.ListBox.HasItems
    member this.HasItems = win.IsSome && win.Value.CompletionList.ListBox.HasItems
    
    member this.Close() = 
        if win.IsSome then 
            win.Value.Close()
            win <- None
        justClosed <- true // to not trigger completion again on one letter completions
   
    member this.RequestInsertion(ev) = if win.IsSome then win.Value.CompletionList.RequestInsertion(ev)
    
    member this.JustClosed 
                with get() = justClosed
                and set(v) = justClosed <- v

    [<CLIEvent>] member this.OnShowing = showingEv.Publish
    member this.ShowingEv = showingEv

    /// Returns "loading" text and triggers async computation to get and update with actual text 
    member this.GetToolTip(it:DeclarationListItem)= 
        hasStackPanelTypeInfo <-false
        async{            
            let raw = it.Description            
            let structured = 
                if optArgsDict.ContainsKey it.FullName then  TypeInfo.getToolTipDataList (raw, optArgsDict.[it.FullName])
                else                                         TypeInfo.getToolTipDataList (raw, ResizeArray(0))
            if this.IsOpen then
                do! Async.SwitchToContext FsEx.Wpf.SyncWpf.context
                if this.IsOpen then // might get closed during context switch
                    if selectedText() = it.Name then 
                        win.Value.ToolTipContent <- TypeInfo.getPanel (Some it, structured )
                        hasStackPanelTypeInfo <-true
                        //TODO add structure to Dict so it does not need recomputing if browsing items in the completion list.
        } |> Async.Start
        TypeInfo.loadingText :> obj    

    member this.HasStackPanelTypeInfo = hasStackPanelTypeInfo

    member this.Log = log
    member this.Checker = checker
    member this.Config = config
    
    member this.ComplWin 
        with get() = win
        and set(w) = win<-w  
    
    /// for a given method name retunes a list of optional argument names
    member this.OptArgsDict = optArgsDict

    static member TryShow(iEditor:IEditor,compl:Completions, pos:PositionInCode , changetype:TextChange, setback:int, query:string, charBefore:CharBeforeQuery, onlyDU:bool) = 
        //a static method so that i can take an IEditor as argument
        let log = compl.Log
        let config = compl.Config
        let avaEdit = iEditor.AvaEdit
        //log.PrintfnDebugMsg "TryShow Completion Window for '%s'" pos.lineToCaret
        let ifDotSetback = if charBefore = Dot then setback else 0


        let contOnUI (decls: DeclarationListInfo, declSymbs: FSharpSymbolUse list list) =
            
            // TODO move out of UI thread
            /// for adding question marks to optional arguments:
            compl.OptArgsDict.Clear() //TODO make persistent on class for cashing
            for symbs in declSymbs do 
                for symb in symbs do 
                    let opts = TypeInfo.namesOfOptionalArgs( symb)
                    if opts.Count>0 then 
                        compl.OptArgsDict.[symb.Symbol.FullName]<- opts
           

            let completionLines = ResizeArray<ICompletionData>()                                
            if not onlyDU && charBefore = NotDot then
                completionLines.Add( CompletionItemForKeyWord(iEditor,config,"#if INTERACTIVE",     "Compiler directive to exclude code in compiled form, close with #endif" ) :> ICompletionData)    |>ignore
                completionLines.Add( CompletionItemForKeyWord(iEditor,config,"#else",               "else of compiler directive " ) :> ICompletionData)    |>ignore
                completionLines.Add( CompletionItemForKeyWord(iEditor,config,"#endif",              "End of compiler directive " ) :> ICompletionData)    |>ignore
                completionLines.Add( CompletionItemForKeyWord(iEditor,config,"__SOURCE_DIRECTORY__","Evaluates to the current full path of the source directory" ) :> ICompletionData)    |>ignore
                completionLines.Add( CompletionItemForKeyWord(iEditor,config,"__SOURCE_FILE__"     ,"Evaluates to the current source file name, without its path") :> ICompletionData)    |>ignore
                completionLines.Add( CompletionItemForKeyWord(iEditor,config,"__LINE__",            "Evaluates to the current line number") :> ICompletionData)    |>ignore
                for kw,desc in FSharpKeywords.KeywordsWithDescription  do // add keywords to list
                    completionLines.Add( CompletionItemForKeyWord(iEditor,config,kw,desc) :> ICompletionData) |>ignore
            
              
            for it in decls.Items do                    
                match it.Glyph with 
                |FSharpGlyph.Union
                |FSharpGlyph.Module 
                |FSharpGlyph.EnumMember -> completionLines.Add (new CompletionItem(iEditor,config, compl.GetToolTip, it, (changetype = EnteredDot))) // for DU completion add just some.
                | _ -> if not onlyDU then  completionLines.Add (new CompletionItem(iEditor,config, compl.GetToolTip, it, (changetype = EnteredDot))) // for normal completion add all others too.
              
            if completionLines.Count > 0 then 
                compl.ShowingEv.Trigger() // to close error and type info tooltip

                let w =  new CodeCompletion.CompletionWindow(avaEdit.TextArea)
                compl.ComplWin <- Some w
                w.BorderThickness <- Thickness(0.0)
                w.ResizeMode      <- ResizeMode.NoResize // needed to have no border!
                w.WindowStyle     <- WindowStyle.None // = no border
                w.SizeToContent   <- SizeToContent.WidthAndHeight //https://github.com/icsharpcode/AvalonEdit/blob/master/AvalonEditB/CodeCompletion/CompletionWindow.cs#L47
                w.MinHeight <- avaEdit.FontSize
                w.MinWidth  <- avaEdit.FontSize * 8.0
                w.Closed.Add (fun _  -> 
                        compl.Close()
                        compl.Checker.CkeckHighlightAndFold(iEditor) //needed ! because window might close immedeatly after showing if ther are no matches
                        //config.Log.PrintfnDebugMsg "Completion window just closed with selected item: %A " w.CompletionList.SelectedItem
                        )
                    
                w.CompletionList.SelectionChanged.Add(fun _ -> if w.CompletionList.ListBox.Items.Count=0 then w.Close()) // otherwise empty box might be shown and only get closed on second character
                w.Loaded.Add(                         fun _ -> if w.CompletionList.ListBox.Items.Count=0 then w.Close()) // close immediatly if completion list is empty
                   
                w.CloseAutomatically <-true
                w.CloseWhenCaretAtBeginning <- true
                    
                //w.CompletionList.InsertionRequested.Add (fun _ -> avaEdit.LastTextChangeWasFromCompletionWindow <- true) // BAD !! triggers after text inested and textchnaged is triggerd on single letter 

                //w.CompletionList.ListBox.SelectionChanged.Add (fun e -> //TODO this is not the correct event to hook up to
                //    try if not w.CompletionList.ListBox.HasItems then w.Close() 
                //    with  _ -> log.PrintfnDebugMsg "Null ref HasItems")// because sometime empty completion window stays open

                
                w.StartOffset <- w.StartOffset - setback // to maybe replace some previous characters too           
       
                for cln in completionLines do 
                    w.CompletionList.CompletionData.Add (cln) 

                if query.Length > 0 then 
                    w.CompletionList.SelectItem(query) //to prefilter the list if query present
            
                try
                    //log.PrintfnDebugMsg "Show Completion Window with %d items" w.CompletionList.CompletionData.Count
                    w.Show()
                with e -> 
                    log.PrintfnAppErrorMsg "Error in Showing Code Completion Window: %A" e

                // Event sequence on pressing enter in completion window:
                // (1)Close window
                // (2)insert text into editor (triggers completion if one char only)
                // (3)raise InsertionRequested event
                // https://github.com/icsharpcode/AvalonEdit/blob/8fca62270d8ed3694810308061ff55c8820c8dfc/AvalonEditB/CodeCompletion/CompletionWindow.cs#L100
            
            else
                compl.Checker.CkeckHighlightAndFold(iEditor)// start new full check, this on was trimmed at offset.

        compl.Checker.GetCompletions(iEditor, pos, ifDotSetback, contOnUI)

