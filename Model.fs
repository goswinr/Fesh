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
     
    
    
    

module Appearance =  

    let mutable font = FontFamily("Consolas")
    
    let mutable fontSize = 14.0 //will also be used for sizing tooltip text

    let dialogCaption = "Seff | Scripting editor for fsharp"   