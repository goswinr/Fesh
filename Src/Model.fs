namespace Seff

open System
open System.IO
open System.Windows.Media // for color brushes
open System.Windows.Input
open ICSharpCode
open FSharp.Compiler.SourceCodeServices



module Style =  
    let dialogCaption = "Seff | Scripting editor for fsharp"   // e.g title of saveAs window
    
    let mutable fontEditor      = FontFamily("Consolas") // used for startup only, will be set from rescources in  FontManager.fs
    let mutable fontLog         = FontFamily("Consolas") // used for startup only,
    let mutable fontToolTip     = FontFamily("Consolas") // used for startup only,

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

/// ---- Editor types -----------

type CheckId = int64

type Code = 
    FullCode of string | PartialCode of string
    member this.Code = match this with  FullCode s -> s  | PartialCode s -> s
       
        

type CheckResults = { parseRes:FSharpParseFileResults; checkRes:FSharpCheckFileResults;  code:Code ; checkId:CheckId} // to do remove till offset , not needed?

type FilePath = SetTo of FileInfo | NotSet

type FileCheckState = 
    | NotStarted 
    | Running of CheckId
    | Done of CheckResults // not global but local per file
    | Failed 


// so that the Editor can be used before declared
type IEditor = 
    abstract member Id           : Guid
    abstract member AvaEdit      : AvalonEdit.TextEditor
    abstract member CheckState   : FileCheckState //with get , set //None means a check is running
    abstract member FilePath     : FilePath     


//---- Fsi types ------------

type CodeToEval = {code:string; file:FilePath; allOfFile:bool}   

type HostingMode = Standalone  | Hosted of string

type FsiState =  Ready | Evaluating | Initalizing | NotLoaded

type FsiMode  = Sync | Async

type FsiIsCancelingOk = NotEvaluating | YesAsync | Dont | NotPossibleSync // Not-Possible-Sync because during sync eval the ui should be frozen anyway and this request should not be happening
     
type TextChange =  EnteredDot | EnteredOneIdentifierChar | EnteredOneNonIdentifierChar | CompletionWinClosed | OtherChange //| EnteredQuote
    
type CharBeforeQuery = Dot | NotDot
    
type PositionInCode = { lineToCaret:string ; row:int; column:int; offset:int }

// Menu and commands:

type CommandInfo = {name:string; gesture:string; cmd:ICommand; tip:string }


