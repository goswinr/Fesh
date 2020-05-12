namespace Seff.Editor

open Seff
open Seff.Config
open Seff.Model

//open Seff.Util
open System
open System.IO
open System.Windows
open System.Windows.Controls
open System.Windows.Media
open ICSharpCode.AvalonEdit
open ICSharpCode.AvalonEdit.CodeCompletion
open ICSharpCode.AvalonEdit.Editing
open ICSharpCode.AvalonEdit.Document
open FSharp.Compiler
open FSharp.Compiler.SourceCodeServices
open System.Collections.Generic


type CompletionLine (config:Config, win:CompletionWindow, it:FSharpDeclarationListItem, isDotCompletion:bool, optArgsDict:Dictionary<string,ResizeArray<string>>) =

    let style =         
        if it.IsOwnMember then FontStyles.Normal 
        else match it.Glyph with    //new Font(FontFamily.GenericSansSerif,12.0F, FontStyle.Bold | FontStyle.Italic) // needs system.drawing
                | FSharpGlyph.Module | FSharpGlyph.NameSpace -> FontStyles.Normal 
                | _                                          -> FontStyles.Italic

    let tb = 
        let mutable tb = TextBlock()
        tb.Text <- it.Name 
        tb.FontFamily <- Appearance.font  
        tb.FontSize <-   Appearance.fontSize 
        //tb.Foreground  <- col, // does not change color when selected anymore
        tb.FontStyle <- style
        tb.Padding <- Thickness(0. , 0. , 8. , 0. ) //left top right bottom / so that it does not aper to be trimmed
        tb

    let priority = //if it.IsOwnMember then 1. else 1. 
        if isDotCompletion then 1.0// not on Dot completion             
        else                    1.0 + config.AutoCompleteStatistic.Get(it.Name) //if p>1.0 then log.PrintDebugMsg "%s %g" it.Name p
            
    
    member this.Content = tb :> obj
    member this.Description = // this gets called on demand only, not when initally filling the list.
        async{
            let raw = it.StructuredDescriptionText
            let structured = 
                if optArgsDict.ContainsKey it.FullName then  TypeInfo.GetFormated (raw, optArgsDict.[it.FullName])
                else                                         TypeInfo.GetFormated (raw, ResizeArray(0))
            if win.Visibility = Visibility.Visible then  
                do! Async.SwitchToContext Sync.syncContext
                if win.CompletionList.SelectedItem.Text = it.Name then 
                    win.ToolTipContent <- TypeInfo.GetPanel (Some it, structured )
                else
                    () //TODO add structure to Dict so it does not need recomputing if browsing items in the completion list.
        } |> Async.Start
        TypeInfo.LoadingText :> obj
        
    member this.Image = null
    member this.Priority = priority
    member this.Text = it.Name
    member this.Complete (textArea:TextArea, completionSegment:ISegment, e ) = 
        //log.PrintDebugMsg "%s is %A and %A" it.Name it.Glyph it.Kind
        //textArea.Document.Replace(completionSegment.Offset + 1, completionSegment.Length, it.Name) //TODO Delete!
        //textArea.Caret.Offset <- completionSegment.Offset + it.Name.Length + 1  //TODO Delete!          
        let compl = if it.Glyph = FSharpGlyph.Class && it.Name.EndsWith "Attribute" then "[<" + it.Name.Replace("Attribute",">]") else it.Name     //TODO move this logic out here      
        textArea.Document.Replace(completionSegment, compl) 
        if not isDotCompletion then 
            config.AutoCompleteStatistic.Incr(it.Name)
            config.AutoCompleteStatistic.Save()
        //Editor.current.TriggerCompletionInserted it.Name // to be able to rerun checking

    interface ICompletionData with 
        // Note that the CompletionList uses WPF data binding against the properties in this interface.
        //Thus, your implementation of the interface must use public properties; not explicit interface implementation.
        // needed in F#: implementing the interface members as properties too: https://github.com/icsharpcode/AvalonEdit/issues/28
        member this.Complete(t,s,e) = this.Complete(t,s,e)            
        member this.Content         = this.Content 
        member this.Description     = this.Description //this gets call on demand only, not when filling the completion list.
        member this.Image           = this.Image           
        member this.Priority        = this.Priority
        member this.Text            = this.Text 
            
type CompletionLineKeyWord (config:Config, text:string, toolTip:string) =
    //let col = Brushes.DarkBlue    // fails on selection, does not get color inverted//check  https://blogs.msdn.microsoft.com/text/2009/08/28/selection-brush/ ??
                
    let style = FontStyles.Normal
    let tb = 
        let mutable tb = TextBlock()
        tb.Text <- text
        tb.FontFamily <- Appearance.font  
        tb.FontSize <-   Appearance.fontSize 
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
    member this.Complete (textArea:TextArea, completionSegment:ISegment, e ) =       
        textArea.Document.Replace(completionSegment, text) 
        //Editor.current.TriggerCompletionInserted it.Name // to be able to rerun checking

    interface ICompletionData with // needed in F#: implementing the interface members as properties too: https://github.com/icsharpcode/AvalonEdit/issues/28
        member this.Complete(t,s,e) = this.Complete(t,s,e)            
        member this.Content         = this.Content 
        member this.Description     = this.Description
        member this.Image           = this.Image           
        member this.Priority        = this.Priority
        member this.Text            = this.Text

type CompletionWindow(avaEdit:TextEditor,config:Config, checker:Checker) =
    let win = new CodeCompletion.CompletionWindow(avaEdit.TextArea)
    
    let showingEv = Event<unit>()

    let mutable justClosed = false //TODO neded ?

    let keywordsComletionLines = [| 
        for kw,desc in Keywords.KeywordsWithDescription  do 
            yield CompletionLineKeyWord(config,kw,desc) :> ICompletionData
            yield CompletionLineKeyWord(config,"__SOURCE_DIRECTORY__","Evaluates to the current full path of the source directory" ) :> ICompletionData    
            yield CompletionLineKeyWord(config,"__SOURCE_FILE__"     ,"Evaluates to the current source file name, without its path") :> ICompletionData    
            yield CompletionLineKeyWord(config,"__LINE__",            "Evaluates to the current line number") :> ICompletionData    
            |]
    
    do
        win.BorderThickness <- Thickness(0.0)
        win.ResizeMode      <- ResizeMode.NoResize // needed to have no border!
        win.WindowStyle     <- WindowStyle.None // = no border
        win.SizeToContent   <- SizeToContent.WidthAndHeight //https://github.com/icsharpcode/AvalonEdit/blob/master/ICSharpCode.AvalonEdit/CodeCompletion/CompletionWindow.cs#L47
        win.MinHeight <- avaEdit.FontSize
        win.MinWidth  <- avaEdit.FontSize * 8.0
        win.Closed.Add (fun _  -> 
             config.Log.PrintDebugMsg "Completion window just closed with selected item: %A " win.CompletionList.SelectedItem
             justClosed <- true // to not trigger completion again
            )
        
        win.CompletionList.SelectionChanged.Add(fun _ -> if win.CompletionList.ListBox.Items.Count=0 then win.Close()) // otherwise empty box might be shown and only get closed on second character
        win.Loaded.Add(                         fun _ -> if win.CompletionList.ListBox.Items.Count=0 then win.Close()) // close immediatly if completion list is empty
         
        win.CloseAutomatically <-true
        win.CloseWhenCaretAtBeginning <- true
        
        //w.CompletionList.InsertionRequested.Add (fun _ -> avaEdit.LastTextChangeWasFromCompletionWindow <- true) // BAD !! triggers after text inested and textchnaged is triggerd on single letter 

        //w.CompletionList.ListBox.SelectionChanged.Add (fun e -> //TODO this is not the correct event to hook up to
        //    try if not w.CompletionList.ListBox.HasItems then w.Close() 
        //    with  _ -> log.PrintDebugMsg "Null ref HasItems")// because sometime empty completion window stays open

    
    member this.IsVisible = win.IsVisible
    
    /// returns  win.CompletionList.ListBox.HasItems
    member this.HasItems = win.CompletionList.ListBox.HasItems

    member this.Close() = win.Close()

    member this.Window = win

    member this.JustClosed 
                with get() = justClosed
                and set(v) = justClosed <- v

    member this.OnShowing = showingEv.Publish

    member this.TryShow(fi: FileInfo Option, pos:PositionInCode , changetype:Model.TextChange, setback:int, query:string, charBefore:CharBeforeQuery, onlyDU:bool) = 
        //log.PrintDebugMsg "*prepareAndShowComplWin..."
        let ifDotSetback = if charBefore = Dot then setback else 0

        //let prevCursor = avaEdit.Editor.Cursor
        //avaEdit.Editor.Cursor <- Cursors.Wait //TODO does this get stuck on folding column ?

        let contOnUI (decls: FSharpDeclarationListInfo,declSymbs: FSharpSymbolUse list list) =
            
            /// for adding question m arks to optional arguments:
            let optArgDict = Dictionary()
            for symbs in declSymbs do 
                for symb in symbs do 
                    let opts = TypeInfo.NamesOfOptionalArgs symb
                    if opts.Count>0 then 
                        optArgDict.[symb.Symbol.FullName]<- opts

            let completionLines = ResizeArray<ICompletionData>()                                
            if not onlyDU && charBefore = NotDot then   completionLines.AddRange keywordsComletionLines  // add keywords to list
            for it in decls.Items do                    
                match it.Glyph with 
                |FSharpGlyph.Union|FSharpGlyph.Module | FSharpGlyph.EnumMember -> completionLines.Add (new CompletionLine(config, win, it, (changetype = EnteredDot), optArgDict)) 
                | _ -> if not onlyDU then                                         completionLines.Add (new CompletionLine(config, win, it, (changetype = EnteredDot), optArgDict))
              
            if completionLines.Count > 0 then 
                //( xs: ICompletionData seq, setback:int , query:string) 
                showingEv.Trigger() // to close error and type info tooltip
                win.CompletionList.CompletionData.Clear()
                win.StartOffset <- win.StartOffset - setback // to maybe replace some previous characters too           
       
                for cln in completionLines do 
                    win.CompletionList.CompletionData.Add (cln) // if window is slow: https://stackoverflow.com/questions/487661/how-do-i-suspend-painting-for-a-control-and-its-children 

                if query.Length > 0 then 
                    win.CompletionList.SelectItem(query) //to prefilter the list if query present
            
                //try
                win.Show()
                //with e -> log.PrintDebugMsg "Error in Showing Code Completion Window: %A" e

                // Event sequence on pressing enter in completion window:
                // (1)Close window
                // (2)insert text into editor (triggers completion if one char only)
                // (3)raise InsertionRequested event
                // https://github.com/icsharpcode/AvalonEdit/blob/8fca62270d8ed3694810308061ff55c8820c8dfc/ICSharpCode.AvalonEdit/CodeCompletion/CompletionWindow.cs#L100
            
            else
                ()// TODO highlight errors instead

        checker.GetCompletions(avaEdit, fi, pos, ifDotSetback, contOnUI)

