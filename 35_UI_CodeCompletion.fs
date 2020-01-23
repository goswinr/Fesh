﻿namespace Seff

open Seff.Util
open System
open System.Windows
open System.Windows.Controls
open System.Windows.Media

open ICSharpCode.AvalonEdit.CodeCompletion
open ICSharpCode.AvalonEdit.Editing
open ICSharpCode.AvalonEdit.Document
open FSharp.Compiler
open FSharp.Compiler.SourceCodeServices

module CompletionUI =
    
    type CompletionLine (it:FSharpDeclarationListItem) =
        let col = 
            match it.Glyph with  // does not change coler when selected anymore
            | FSharpGlyph.Class
            | FSharpGlyph.Typedef
            | FSharpGlyph.Type
            | FSharpGlyph.Exception         -> Brushes.DarkBlue

            | FSharpGlyph.Union
            | FSharpGlyph.Enum              -> Brushes.DarkGray

            | FSharpGlyph.EnumMember
            | FSharpGlyph.Variable
            | FSharpGlyph.Field             -> Brushes.Black

            | FSharpGlyph.Constant          -> Brushes.DarkCyan
            | FSharpGlyph.Event             -> Brushes.DarkRed
            | FSharpGlyph.Delegate          -> Brushes.DarkMagenta
            | FSharpGlyph.Interface         -> Brushes.DarkCyan
            | FSharpGlyph.Method            -> Brushes.Black
            | FSharpGlyph.OverridenMethod   -> Brushes.DarkKhaki
            | FSharpGlyph.Module            -> Brushes.Black
            | FSharpGlyph.NameSpace         -> Brushes.Black
            | FSharpGlyph.Property          -> Brushes.DarkGreen
            | FSharpGlyph.Struct            -> Brushes.Blue
            | FSharpGlyph.ExtensionMethod   -> Brushes.DarkKhaki
            | FSharpGlyph.Error             -> Brushes.Red
    
        let prio = if it.IsOwnMember then 1. else 1. 
        let style =         
            if it.IsOwnMember then FontStyles.Normal 
            else match it.Glyph with    //new Font(FontFamily.GenericSansSerif,12.0F, FontStyle.Bold | FontStyle.Italic) // needs system.drawing
                 | FSharpGlyph.Module | FSharpGlyph.NameSpace -> FontStyles.Normal 
                 | _                                          -> FontStyles.Italic

        let tb = 
            let mutable tb = TextBlock()
            tb.Text <- it.Name //+ " " // TODO add padding instaead of space character?
            tb.FontFamily <- Appearance.defaultFont  
            tb.FontSize <-   Appearance.fontSize 
            //tb.Foreground  <- col, // does not change color when selected anymore
            tb.FontStyle <- style
            tb.Padding <- Thickness(0. , 0. , 8. , 0. ) //left top right bottom / so that it does not aper to be trimmed
            tb
    
        member this.Content = tb :> obj
        member this.Description = //this gets call on demand only, not when filling the list.
            it.StructuredDescriptionText |> Tooltips.formated |> Tooltips.stackPanel (Some it) :> obj
            //async{
            //    let! stt = it.StructuredDescriptionTextAsync
            //    let ttds = Tooltips.formated stt
            //    do! Async.SwitchToContext Sync.syncContext
            //    return Tooltips.stackPanel ttds} |> Async.RunSynchronously :> obj // this will cause the UI to hang

        member this.Image = null
        member this.Priority = prio
        member this.Text = it.Name
        member this.Complete (textArea:TextArea, completionSegment:ISegment, e ) = 
            //Log.printf "%s is %A and %A" it.Name it.Glyph it.Kind
            //textArea.Document.Replace(completionSegment.Offset + 1, completionSegment.Length, it.Name) //Delete!
            //textArea.Caret.Offset <- completionSegment.Offset + it.Name.Length + 1  //Delete!          
            let compl = if it.Glyph = FSharpGlyph.Class && it.Name.EndsWith "Attribute" then "[<" + it.Name.Replace("Attribute",">]") else it.Name     //TODO move this logic out here      
            textArea.Document.Replace(completionSegment, compl) 
            //Editor.current.TriggerCompletionInserted it.Name // to be able to rerun checking

        interface ICompletionData with // needed in F#: implementing the interface members as properties too: https://github.com/icsharpcode/AvalonEdit/issues/28
            member this.Complete(t,s,e) = this.Complete(t,s,e)            
            member this.Content         = this.Content 
            member this.Description     = this.Description //this gets call on demand only, not when filling the completion list.
            member this.Image           = this.Image           
            member this.Priority        = this.Priority
            member this.Text            = this.Text 
            
    type CompletionLineKeyWord (text:string, toolTip:string) =

        //let col = Brushes.DarkBlue    // fails on selection, does not get color inverted
        let prio = 99.9
        let style = FontStyles.Normal
        let tb = 
            new TextBlock(
                    Text = text + " ", // add padding instaead of space character
                    FontFamily = Appearance.defaultFont  ,
                    FontSize =   Appearance.fontSize ,
                    //Foreground  = col, // does not change color when selected anymore //check  https://blogs.msdn.microsoft.com/text/2009/08/28/selection-brush/ ??
                    FontStyle = style
                    )
        member this.Content = tb :> obj
        member this.Description = toolTip :> obj // it.DescriptionText :> obj // xml ?
        member this.Image = null
        member this.Priority = prio
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

    let showCompletionWindow( tab:FsxTab, xs: ICompletionData seq, setback, query:string) =
        tab.ErrorToolTip.IsOpen    <- false
        tab.TypeInfoToolTip.IsOpen <- false
            
        let w = new CompletionWindow(tab.Editor.TextArea)
        tab.CompletionWin <- Some w
        w.StartOffset <- w.StartOffset - setback // to maybe replace some previous characters too           
        w.BorderThickness <- Thickness(0.0)
        w.ResizeMode      <- ResizeMode.NoResize // needed to have no border!
        w.WindowStyle     <- WindowStyle.None // = no border
        w.SizeToContent   <- SizeToContent.WidthAndHeight //https://github.com/icsharpcode/AvalonEdit/blob/master/ICSharpCode.AvalonEdit/CodeCompletion/CompletionWindow.cs#L47
        w.MinHeight <- tab.Editor.FontSize
        w.MinWidth <- tab.Editor.FontSize * 8.0
        w.Closed.Add (fun _  -> 
            //Log.printf "Completion window closed with selected item %s " tab.CompletionWin.Value.CompletionList.SelectedItem.Text
            tab.CompletionWin <- None  
            tab.CompletionWindowClosed()
            tab.ErrorToolTip.IsOpen    <- false
            tab.TypeInfoToolTip.IsOpen <- false
            tab.CompletionWindowJustClosed <- true // to not trigger completion again
            )
        
        w.CompletionList.SelectionChanged.Add(fun _ -> if w.CompletionList.ListBox.Items.Count=0 then w.Close()) // otherwise empty box might be shown and only get closed on second character
        w.Loaded.Add(fun _ -> if w.CompletionList.ListBox.Items.Count=0 then w.Close()) // close immediatly if completion list is empty
        
        w.CloseAutomatically <-true
        w.CloseWhenCaretAtBeginning <- true
        //w.CompletionList.InsertionRequested.Add (fun _ -> tab.LastTextChangeWasFromCompletionWindow <- true) // BAD !! triggers after text inested and textchnaged is triggerd on single letter 

        //w.CompletionList.ListBox.SelectionChanged.Add (fun e -> //TODO this is not the correct event to hook up to
        //    try if not w.CompletionList.ListBox.HasItems then w.Close() 
        //    with  _ -> Log.print "Null ref HasItems")// because sometime empty completion window stays open

        for x in xs do w.CompletionList.CompletionData.Add (x) // if window is slow: https://stackoverflow.com/questions/487661/how-do-i-suspend-painting-for-a-control-and-its-children 

        if query.Length > 0 then 
            w.CompletionList.SelectItem(query) //to prefilter the list if query present
            
        //try
        w.Show()
        //with e -> Log.printf "Error in Showing Code Completion Window: %A" e


        //Event sequence on pressing enter in completion window:// https://github.com/icsharpcode/AvalonEdit/blob/8fca62270d8ed3694810308061ff55c8820c8dfc/ICSharpCode.AvalonEdit/CodeCompletion/CompletionWindow.cs#L100
        // Close window
        // insert text into editor (triggers completion if one char only)
        // raise InsertionRequested event

        