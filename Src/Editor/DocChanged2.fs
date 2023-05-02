namespace Seff.Editor

open System
open System.Threading
open System.Windows
open System.Windows.Input

open FSharp.Compiler.Tokenization // for keywords

open AvalonEditB
open AvalonEditB.Document

open Seff.Model
open Seff.Util
open Seff.Util.Str
open Seff


module DocChangeUtil = 
    
    /// returns the total character count change -1 or +1 depending if its a insert or remove
    let isSingleCharChange (a:DocumentChangeEventArgs) =
        match a.InsertionLength, a.RemovalLength with
        | 1, 0 -> ValueSome  1
        | 1, 1 -> ValueSome  0
        | 0, 1 -> ValueSome -1
        | _    -> ValueNone


    /// first: Foldings, ColorBrackets and BadIndentation when full text available async.
    let firstMarkingStep (fullCode, fastColor, state) =
        ()
    
    /// second: Errors and Semantic Highlighting on check result .    
    let secondMarkingStep (fullCode, fastColor, state) =
        ()

        
    let singleCharChange (a:DocumentChangeEventArgs) =
        ()



module DocChanged2 = 
    open DocChangeUtil
    
    let changing (fastColor:FastColorizer) (a:DocumentChangeEventArgs) =             
        match DocChangeUtil.isSingleCharChange a with 
        |ValueSome s -> fastColor.AdjustShift s
        |ValueNone   -> fastColor.ResetShift() //multiCharChange             
    

    let changed (iEd:IEditor)(fastColor:FastColorizer) (state:InteractionState) (a:DocumentChangeEventArgs)  =          
        match state.DocChangedConsequence with 
        | WaitForCompletions -> ()
        | React ->            
            let id = Interlocked.Increment state.DocChangedId 
            let inline isLatest () = id = state.DocChangedId.Value
            let doc = iEd.AvaEdit.Document
            async{                
                match DocChangeUtil.isSingleCharChange a with 
                |ValueSome _ -> 
                    singleCharChange a
                |ValueNone   -> 
                    // NOTE just checking only Partial Code till caret with (doc.CreateSnapshot(0, tillOffset).Text) would make the GetDeclarationsList method miss some declarations !!
                    let fullCode : CodeAsString = doc.CreateSnapshot().Text // the only threadsafe way to access the code string                    
                    if isLatest() then  
                        firstMarkingStep (fullCode,fastColor,state)
                        secondMarkingStep (fullCode,fastColor,state)
            } 
            |> Async.Start
            




            

        