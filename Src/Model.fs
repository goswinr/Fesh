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

    let mutable fontSize = 14.0 // used for default startup only, will be set in Settings.fs


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
        
        
type FullCodeAndId = 
    | CodeID of string * CheckId
    | NoCode

type CheckResults = { 
    parseRes    :FSharpParseFileResults 
    checkRes    :FSharpCheckFileResults 
    code        :Code 
    checkId     :CheckId     } 

type FilePath = 
    SetTo of FileInfo | NotSet
    member this.File = match this with SetTo fi -> fi.Name |NotSet -> "*noName*"


type FileCheckState = 
    | NotStarted 

    /// getting the code form avalon edit  async
    | GettingCode of CheckId 

    /// got the code form avalon edit async, now running in FCS async
    | Checking of CheckId*Code 

    /// not global but local per file
    | Done of CheckResults 
    | Failed 

    member this.FullCodeAndId  =         
        match this with
        | NotStarted | GettingCode _  | Failed -> NoCode
        | Checking (id, c)  ->  match c        with  FullCode s -> CodeID (s,id          )  | PartialCode _ -> NoCode
        | Done res          ->  match res.code with  FullCode s -> CodeID (s,res.checkId )  | PartialCode _ -> NoCode

    /// to compare FileCheckState with GlobalCheckState
    member this.SameIdAndFullCode (globalChSt:FileCheckState) =  
        match this.FullCodeAndId with
        |NoCode -> NoCode
        |CodeID (id, c)  ->
            match globalChSt.FullCodeAndId with 
            |NoCode -> NoCode
            |CodeID (gid, _) as ci -> if gid=id then ci  else NoCode
        


// so that the Editor can be used before declared
type IEditor = 
    abstract member Id             : Guid
    abstract member AvaEdit        : AvalonEdit.TextEditor
    abstract member FileCheckState : FileCheckState with get , set //None means a check is running
    abstract member FilePath       : FilePath 
    abstract member Log            : ISeffLog 


//---- Fsi types ------------

type CodeToEval = {code:string; file:FilePath; allOfFile:bool} 

type FsiState =  Ready | Evaluating | Initalizing | NotLoaded

type FsiMode  = Sync | Async

type FsiIsCancelingOk = NotEvaluating | YesAsync | Dont | NotPossibleSync // Not-Possible-Sync because during sync eval the ui should be frozen anyway and this request should not be happening
     
type TextChange =  EnteredDot | EnteredOneIdentifierChar | EnteredOneNonIdentifierChar | CompletionWinClosed | OtherChange //| EnteredQuote
    
type CharBeforeQuery = Dot | NotDot
    
type PositionInCode = { lineToCaret:string ; row:int; column:int; offset:int }

// Menu and commands:

type CommandInfo = {name:string; gesture:string; cmd:ICommand; tip:string }


