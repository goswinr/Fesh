namespace Seff

open System
open System.IO
open System.Windows.Media // for color brushes
open System.Windows.Input
open ICSharpCode

type ISeffLog =
    abstract member ReadOnlyEditor   : AvalonEdit.TextEditor

    // this interface allows the Config to be declared before the Log
    // the Log is created first with this interface and then Config gets it in the constructor
    abstract member PrintInfoMsg     : Printf.TextWriterFormat<'T> -> 'T 
    abstract member PrintFsiErrorMsg : Printf.TextWriterFormat<'T> -> 'T  
    abstract member PrintAppErrorMsg : Printf.TextWriterFormat<'T> -> 'T  
    abstract member PrintIOErrorMsg  : Printf.TextWriterFormat<'T> -> 'T  
    abstract member PrintDebugMsg    : Printf.TextWriterFormat<'T> -> 'T  
    //used in FSI constructor:
    abstract member TextWriterFsiStdOut    : TextWriter
    abstract member TextWriterFsiErrorOut  : TextWriter
    abstract member TextWriterConsoleOut   : TextWriter
    abstract member TextWriterConsoleError : TextWriter

    
type AppRunContext = 
    |Standalone 
    |Hosted of string

type FsiState =  Ready | Evaluating | Initalizing | NotLoaded

type FsiMode  = Sync | Async

type FsiIsCancelingOk = NotEvaluating | YesAsync | Dont | NotPossibleSync // Not-Possible-Sync because during sync eval the ui should be frozen anyway and this request should not be happening
     
type TextChange =  EnteredDot | EnteredOneIdentifierChar | EnteredOneNonIdentifierChar | CompletionWinClosed | OtherChange //| EnteredQuote
    
type CharBeforeQuery = Dot | NotDot
    
type CodeToEval = {code:string ; file:FileInfo Option ; allOfFile:bool}
    
type PositionInCode = { lineToCaret:string  
                        row: int   
                        column: int 
                        offset: int }
    
type CommandInfo = {name:string; gesture:string; cmd:ICommand; tip:string }


module Appearance =  
    let dialogCaption = "Seff | Scripting editor for fsharp"   // e.g title of saveAs window
    
    let font = FontFamily("Consolas")
    
    let mutable fontSize = 14.0 //will also be used for sizing tooltip text

    