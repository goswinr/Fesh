namespace Seff

open System
open System.Windows.Media // for color brushes

module Model =
    
    type AppRunContext = 
        |Standalone 
        |Hosted of string

    type FsiState =  Ready | Evaluating | Initalizing | NotLoaded

    type FsiMode  = Sync | Async

    type FsiIsCancelingOk = NotEvaluating | YesAsync | Dont | NotPossibleSync // Not-Possible-Sync because during sync eval the ui should be frozen anyway and this request should not be happening
     
    type TextChange =  EnteredDot | EnteredOneIdentifierChar | EnteredOneNonIdentifierChar | CompletionWinClosed | TabChanged | OtherChange //| EnteredQuote
    
    type CharBeforeQuery = Dot | NotDot
    
    
    type PositionInCode = { lineToCaret:string  
                            row: int   
                            column: int 
                            offset: int }
    

module Appearance =  
    let dialogCaption = "Seff | Scripting editor for fsharp"   // e.g title of saveAs window
    
    let font = FontFamily("Consolas")
    
    let mutable fontSize = 14.0 //will also be used for sizing tooltip text

    