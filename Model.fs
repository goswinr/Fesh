namespace Seff

open System
open System.IO
open System.Windows.Media // for color brushes
open System.Windows.Input
open ICSharpCode
open FSharp.Compiler.SourceCodeServices



module Style =  
    let dialogCaption = "Seff | Scripting editor for fsharp"   // e.g title of saveAs window
    
    let mutable fontEditor      = FontFamily("Consolas") // used for startup and later for Tooltips. will be set in FontManager.fs
    let mutable fontLog         = FontFamily("Consolas") // defaults to arial if font is missing. will be set from rescources in  FontManager.fs
    let mutable fontToolTip   = FontFamily("Consolas") // use for Tooltips. will be set in FontManager.fs

    let mutable fontSize = 14.0 //will also be used for sizing tooltip text


type ISeffLog = 
    // this interface allows the Config to be declared before the Log
    // the Log is created first with this interface and then Config gets it in the constructor
    abstract member ReadOnlyEditor   : AvalonEdit.TextEditor

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

type CheckResults = {   parseRes:FSharpParseFileResults; checkRes:FSharpCheckFileResults;   code:string; tillOffset:int}

// so that the Editor can be used before declared
type IEditor = 
    abstract member Id        : Guid
    abstract member AvaEdit   : AvalonEdit.TextEditor
    abstract member CheckRes  : CheckResults Option with get , set
    abstract member FileInfo  : FileInfo Option     //with get , set
    abstract member NeedsChecking  : bool with get , set
    abstract member DrawErrors: FSharpErrorInfo[] -> unit
   
type AppRunContext = Standalone  | Hosted of string

type FsiState =  Ready | Evaluating | Initalizing | NotLoaded

type FsiMode  = Sync | Async

type FsiIsCancelingOk = NotEvaluating | YesAsync | Dont | NotPossibleSync // Not-Possible-Sync because during sync eval the ui should be frozen anyway and this request should not be happening
     
type TextChange =  EnteredDot | EnteredOneIdentifierChar | EnteredOneNonIdentifierChar | CompletionWinClosed | OtherChange //| EnteredQuote
    
type CharBeforeQuery = Dot | NotDot
    
type CodeToEval = {code:string; file:FileInfo Option; allOfFile:bool}
    
type PositionInCode = { lineToCaret:string ; row:int; column:int; offset:int }
    
type CommandInfo = {name:string; gesture:string; cmd:ICommand; tip:string }



    