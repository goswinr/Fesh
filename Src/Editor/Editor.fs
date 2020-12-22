namespace Seff.Editor


open Seff
open Seff.Model
open Seff.Util.String
open ICSharpCode
open ICSharpCode.AvalonEdit
open System.Windows.Media
open Seff.Config
open Seff.Util.String
open FSharp.Compiler.SourceCodeServices
open System.Windows
open System.IO
open System
open ICSharpCode.AvalonEdit.Document
open System.Windows.Input



 /// The tab that holds the tab header and the code editor 
type Editor private (code:string, config:Config, filePath:FilePath)  = 
    let avaEdit = new AvalonEdit.TextEditor()
    let id = Guid.NewGuid()
    let log = config.Log

    let checker =           Checker.GetOrCreate(config)  

    let search =            Search.SearchPanel.Install(avaEdit)
    
    let errorHighlighter =  new ErrorHighlighter(avaEdit, config.Log)       
    let compls =            new Completions(avaEdit,config,checker,errorHighlighter)
    let folds =             new Foldings(avaEdit,checker, config, id)
    let rulers =            new ColumnRulers(avaEdit, config.Log) // do foldings first
    //let selText =           SelectedTextTracer.Setup(this,folds,config) // moved to: static member SetUp(..) 
    
    
    let mutable checkState = FileCheckState.NotStarted // local to this editor
    let mutable filePath = filePath    
    
    //let mutable needsChecking = true // so that on a tab chnage a recheck is not triggered if not needed

    do
        avaEdit.BorderThickness <- new Thickness( 0.0)
        avaEdit.Text <- code |> unifyLineEndings |> tabsToSpaces avaEdit.Options.IndentationSize
        avaEdit.ShowLineNumbers <- true // background color is set in ColoumnRulers.cs        
        avaEdit.VerticalScrollBarVisibility <- Controls.ScrollBarVisibility.Auto
        avaEdit.HorizontalScrollBarVisibility <- Controls.ScrollBarVisibility.Auto
        avaEdit.Options.HighlightCurrentLine <- true // http://stackoverflow.com/questions/5072761/avalonedit-highlight-current-line-even-when-not-focused
        avaEdit.Options.EnableHyperlinks <- true
        avaEdit.TextArea.TextView.LinkTextForegroundBrush <- Brushes.DarkGreen
        avaEdit.Options.EnableTextDragDrop <- true //TODO add implementation
        avaEdit.Options.ShowSpaces <- false //true
        avaEdit.Options.ShowTabs <- false // they are always converted to spaces, see above
        avaEdit.Options.ConvertTabsToSpaces <- true
        avaEdit.Options.IndentationSize <- 4
        avaEdit.Options.HideCursorWhileTyping <- false
        avaEdit.TextArea.SelectionCornerRadius <- 0.0 
        avaEdit.TextArea.SelectionBorder <- null
        avaEdit.FontFamily <- Style.fontEditor
        avaEdit.FontSize <- config.Settings.GetFloatRounded "FontSize" Seff.Style.fontSize // raounded! because odd sizes like  17.0252982466288  makes block selection delete fail on the last line
        SyntaxHighlighting.setFSharp(avaEdit,config,false)        
        


    member val IsCurrent = false with get,set //  this is managed in Tabs.selectionChanged event handler 
   
    member val TypeInfoTip = new Controls.ToolTip(IsOpen=false)
    
    // all instances of Editor refer to the same checker instance
    member this.Checker = checker

    member this.ErrorHighlighter = errorHighlighter    
    member this.Completions = compls
    member this.Config = config
    
    member this.Folds = folds
    member this.Search = search
    
    
    
    // IEditor members:

    member this.Id              = id
    member this.AvaEdit         = avaEdit    
    ///This CheckStat is local to the current editor
    member this.FileCheckState  with get() = checkState    and  set(v) = checkState <- v    
    member this.FilePath        with get() = filePath      and  set(v) = filePath <- v // The Tab class containing this editor takes care of updating this 
    member this.Log = log
    
    interface IEditor with
        member this.Id              = id
        member this.AvaEdit         = avaEdit
        member this.FileCheckState  with get() = checkState and  set(v) = checkState <- v
        member this.FilePath        = filePath // interface does not need setter
        member this.Log             = log
        member this.FoldingManager  = folds.Manager
    
    
    // additional text change event:
    //let completionInserted = new Event<string>() // event needed because Text change event is not raised after completion insert    
    //[<CLIEvent>]
    //member this.CompletionInserted = completionInserted.Publish
    //member this.TriggerCompletionInserted x = completionInserted.Trigger x // to raise it after completion inserted ?
    
    /// sets up Text change event handlers
    /// a static method so that an instance if IEditor can be used
    static member SetUp  (code:string, config:Config, filePath:FilePath ) = 
        let ed = Editor(code, config, filePath )
        
        SelectedTextTracer.Setup(ed, ed.Folds, config)
        BracketHighlighter.Setup(ed, ed.Checker)

        let avaEdit = ed.AvaEdit
        let compls = ed.Completions
        let log = ed.Log
        
        /// this line will include the charcater that trigger auto completion(dot or first letter)
        let currentLineBeforeCaret()=
            let doc = avaEdit.Document
            let car = avaEdit.TextArea.Caret
            let caretOffset = car.Offset
            let ln = doc.GetLineByOffset(caretOffset)
            let caretOffsetInThisLine = caretOffset - ln.Offset            
            { lineToCaret = doc.GetText(ln.Offset, caretOffsetInThisLine) 
              row =    car.Line  
              column = caretOffsetInThisLine // equal to amount of characters in lineToCaret
              offset = caretOffset }

        let keywords = Keywords.KeywordsWithDescription |> List.map fst |> Collections.Generic.HashSet // used in analysing text change

        let textChanged (change:TextChange) =        
            //log.PrintfnDebugMsg "*1-textChanged because of %A" change 
            if not compls.IsOpen then 
                if compls.HasItems then 
                    //log.PrintfnDebugMsg "*1.2-textChanged not highlighting because  compls.HasItems"
                    //TODO check text is full mtch and close completion window ?
                    // just keep on tying in completion window, no type checking !
                    ()
                else 
                    //log.PrintfnDebugMsg "*1.1-textChanged: closing empty completion window(change: %A)" change 
                    compls.Close() 

                match change with             
                | OtherChange | CompletionWinClosed  | EnteredOneNonIdentifierChar -> //TODO maybe do less call to error highlighter when typing in string or comment ?
                    //log.PrintfnDebugMsg "*1.2-textChanged highlighting for  %A" change
                    ed.Checker.CkeckHighlightAndFold(ed)
                    //TODO trigger here UpdateFoldings(tab,None) or use event

                | EnteredOneIdentifierChar | EnteredDot -> 
                    let pos = currentLineBeforeCaret() // this line will include the charcater that trigger auto completion(dot or first letter)
                    let line = pos.lineToCaret
                    
                    //possible cases where autocompletion is not desired:
                    //let isNotInString           = (countChar '"' line ) - (countSubString "\\\"" line) |> isEven && not <| line.Contains "print" // "\\\"" to ignore escaped quotes of form \" ; check if formating string
                    let isNotAlreadyInComment   = countSubString "//"  line = 0  ||  lastCharIs '/' line   // to make sure comment was not just typed(then still check)
                    let isNotLetDecl            = let lk = (countSubString "let " line) + (countSubString "let(" line) in lk <= (countSubString "=" line) || lk <= (countSubString ":" line)
                    // TODO add check for "for" declaration
                    let isNotFunDecl            = let fk = (countSubString "fun " line) + (countSubString "fun(" line) in fk <= (countSubString "->" line)|| fk <= (countSubString ":" line)
                    let doCompletionInPattern, onlyDU   =  
                        match stringAfterLast " |" (" "+line) with // add starting step to not fail at start of line with "|" //TODO FIX
                        |None    -> true,false 
                        |Some "" -> log.PrintfnDebugMsg " log.PrintfnDebugMsg: this schould never happen since we get here only with letters, but not typing '|'" ; false, false // most comen case: '|" was just typed, next pattern declaration starts after next car
                        |Some s  -> 
                            let doCompl = 
                                s.Contains "->"             || // name binding already happend 
                                s.Contains " when "         || // name binding already happend now in when clause
                                isOperator s.[0]            || // not in pattern matching 
                                s.[0]=']'                   || // not in pattern matching 
                                (s.Contains " :?" && not <| s.Contains " as ")  // auto complete desired  after '| :?" type check but not after 'as' 
                            if not doCompl && startsWithUppercaseAfterWhitespace s then // do autocomplete on DU types when starting with uppercase Letter
                               if s.Contains "(" || s.Contains " " then   false,false //no completion binding a new name inside a DU
                               else                                       true ,true //upper case only, show DU and Enum in completion list, if all others are false
                            else
                               doCompl,false //not upper case, other 3 decide if anything is shown

                    //log.PrintfnDebugMsg "isNotAlreadyInComment:%b; isNotFunDeclaration:%b; isNotLetDeclaration:%b; doCompletionInPattern:%b(, onlyDU:%b)" isNotAlreadyInComment isNotFunDecl isNotLetDecl doCompletionInPattern onlyDU
                
                    if (*isNotInString &&*) isNotAlreadyInComment && isNotFunDecl && isNotLetDecl && doCompletionInPattern then
                        let setback     = lastNonFSharpNameCharPosition line                
                        let query       = line.Substring(line.Length - setback)
                        let isKeyword   = keywords.Contains query
                        //log.PrintfnDebugMsg "pos:%A setback='%d'" pos setback                
                                           
                        let charBeforeQueryDU = 
                            let i = pos.column - setback - 1
                            if i >= 0 && i < line.Length then 
                                if line.[i] = '.' then Dot else NotDot
                            else
                                NotDot

                        if charBeforeQueryDU = NotDot && isKeyword then
                            //log.PrintfnDebugMsg "*2.1-textChanged highlighting with: query='%s', charBefore='%A', isKey=%b, setback='%d', line='%s' " query charBeforeQueryDU isKeyword setback line
                            ed.Checker.CkeckHighlightAndFold(ed)

                        else 
                           
                           
                           //log.PrintfnDebugMsg "*2.2-textChanged Completion window opening with: query='%s', charBefore='%A', isKey=%b, setback='%d', line='%s' change=%A" query charBeforeQueryDU isKeyword setback line change
                           Completions.TryShow(ed, compls, pos, change, setback, query, charBeforeQueryDU, onlyDU)
                    else
                        //log.PrintfnDebugMsg "*2.3-textChanged didn't trigger of checker not needed? isNotAlreadyInComment = %b;isNotFunDecl = %b; isNotLetDecl = %b; doCompletionInPattern = %b" isNotAlreadyInComment  isNotFunDecl  isNotLetDecl  doCompletionInPattern
                        ed.Checker.CkeckHighlightAndFold(ed)
                        ()
        

        let docChanged (e:DocumentChangeEventArgs) = 
                    //log.PrintfnDebugMsg "*Document.Changed Event: deleted %d '%s', inserted %d '%s', completion hasItems: %b, isOpen: %b , Just closed: %b" e.RemovalLength e.RemovedText.Text e.InsertionLength e.InsertedText.Text ed.Completions.HasItems ed.Completions.IsOpen compls.JustClosed
                   
                   //DELETE: //if e.RemovalLength > 0 && e.RemovedText.Text <> e.InsertedText.Text then  compls.JustClosed<-false // in this case open window again?

                   if compls.IsOpen then   // just keep on tying in completion window, no type checking !                
                       if compls.HasItems then // TODO, this code is duplicated in textChanged function
                           ()
                           //let currentText = getField(typeof<CodeCompletion.CompletionList>,w.CompletionList,"currentText") :?> string //this property schould be public !
                           //TODO close Window if w.CompletionList.SelectedItem.Text = currentText
                           //TODO ther is a bug in current text when deliting chars
                           //log.PrintfnDebugMsg "currentText: '%s'" currentText
                           //log.PrintfnDebugMsg "w.CompletionList.CompletionData.Count:%d" w.CompletionList.ListBox.VisibleItemCount
                       else 
                           compls.Close() 
                   
                   else //no completion window open , do type check..                
                       match e.InsertedText.Text with 
                       |"."  ->                                             textChanged (EnteredDot         )//complete
                       | txt when txt.Length = 1 ->                                     
                           if compls.JustClosed then                        textChanged (CompletionWinClosed)//check to avoid retrigger of window on single char completions
                           else                                                         
                               let c = txt.[0]                                          
                               if Char.IsLetter(c) || c='_' || c='`' || c='#'  then   textChanged (EnteredOneIdentifierChar  ) //complete (# for #if directives)
                               else                                         textChanged (EnteredOneNonIdentifierChar)//check
                                                                                        
                       | _  ->                                              textChanged (OtherChange               )//several charcters(paste) ,delete or completion window insert         
                       
                       compls.JustClosed<-false
        
        /// for closing and inserting from completion window
        let textEntering (ev:TextCompositionEventArgs) =          
            if compls.IsOpen then 
                match ev.Text with              //this is not needed  for  general insertion,  insertion with Tab or Enter is built in !!
                |" " -> compls.Close()
                |"." -> compls.RequestInsertion(ev) // insert on dot too? 
                |"(" -> compls.RequestInsertion(ev) // insert on open Bracket too? 
                | _  -> () // other triggers https://github.com/icsharpcode/AvalonEdit/blob/28b887f78c821c7fede1d4fc461bde64f5f21bd1/ICSharpCode.AvalonEdit/CodeCompletion/CompletionList.cs#L171            
            //else
            //    compls.JustClosed<-false
               
        avaEdit.AllowDrop <- true  
        avaEdit.Drop.Add( fun e -> CursorBehaviour.dragAndDrop(ed,log,e)) 
        
        avaEdit.PreviewKeyDown.Add           ( fun e -> CursorBehaviour.previewKeyDown(avaEdit,log,e))   //to indent and dedent
        avaEdit.TextArea.PreviewTextInput.Add (fun a -> CursorBehaviour.previewTextInput(avaEdit,a))
        
        // setup and tracking folding status, (needs a ref to file path:  )
        ed.Folds.SetState( ed )              
        ed.Folds.Margin.MouseUp.Add (fun e -> config.FoldingStatus.Set(ed) )

        //----------------------------------
        //--FS Checker and Code completion--
        //---------------------------------- 

        avaEdit.Document.Changed.Add(docChanged)
        avaEdit.TextArea.TextEntering.Add (textEntering)

        ed.Checker.OnChecked.Add(fun iEditor -> ed.ErrorHighlighter.Draw(ed)) // this then trigger folding too, stusbar update is added in statusbar
        
        compls.OnShowing.Add(fun _ -> ed.ErrorHighlighter.ToolTip.IsOpen <- false)
        compls.OnShowing.Add(fun _ -> ed.TypeInfoTip.IsOpen        <- false)


        // Mouse Hover:

        avaEdit.TextArea.TextView.MouseHover.Add(fun e -> TypeInfo.mouseHover(e, ed, log, ed.TypeInfoTip))        
        avaEdit.TextArea.TextView.MouseHoverStopped.Add(fun _ -> ed.TypeInfoTip.IsOpen <- false )
                

        ed


    ///additional constructor using default code 
    static member New (config:Config) =  Editor.SetUp( config.DefaultCode.Get() , config, NotSet)