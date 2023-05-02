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

    let multiCharChange (a:DocumentChangeEventArgs) =
        ()

    let singleCharChange (a:DocumentChangeEventArgs) =
        ()



module DocChanged2 = 
    open DocChangeUtil
    
    let changing (fastColor:FastColorizer) (a:DocumentChangeEventArgs) =             
        match DocChangeUtil.isSingleCharChange a with 
        |ValueSome s -> fastColor.AdjustShift s
        |ValueNone   -> fastColor.ResetShift() 
            
    

    let changed (fastColor:FastColorizer) (state:InteractionState) (a:DocumentChangeEventArgs)  =  
        Interlocked.Increment state.DocChangedId  |> ignore 
        match state.DocChangedConsequence with 
        | WaitForCompletions -> ()
        | React ->            
            match DocChangeUtil.isSingleCharChange a with 
            |ValueSome _ -> singleCharChange a
            |ValueNone   -> multiCharChange a
        