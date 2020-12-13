namespace Seff.Model

open System
open System.IO
open System.Windows.Media // for color brushes
open System.Windows.Input
open ICSharpCode
open FSharp.Compiler.SourceCodeServices


type ISeffLog = 
    // this interface allows the Config to be declared before the Log
    // the Log is created first with this interface and then Config gets it in the constructor
    abstract member ReadOnlyEditor   : AvalonEdit.TextEditor

    abstract member PrintInfoMsg     : Printf.StringFormat<'T,unit> -> 'T 
    abstract member PrintFsiErrorMsg : Printf.StringFormat<'T,unit> -> 'T  
    abstract member PrintAppErrorMsg : Printf.StringFormat<'T,unit> -> 'T  
    abstract member PrintIOErrorMsg  : Printf.StringFormat<'T,unit> -> 'T  
    abstract member PrintDebugMsg    : Printf.StringFormat<'T,unit> -> 'T
    
    /// Print using the Brush or color provided 
    /// at last custom printing call via PrintCustomBrush or PrintCustomColor 
    abstract member PrintCustom     : Printf.StringFormat<'T,unit> -> 'T

        // Change custom color to a new SolidColorBrush (e.g. from System.Windows.Media.Brushes)
        // This will also freeze the Brush.
        // Then print 
        //abstract member PrintCustomBrush : SolidColorBrush -> Printf.StringFormat<'T,unit> -> 'T 

    /// Change custom color to a RGB value ( each between 0 and 255) 
    /// Then print 
    abstract member PrintCustomColor : int ->  int ->  int ->  Printf.StringFormat<'T,unit> -> 'T 
    
    
    
    /// Prints without adding a new line at the end
    abstract member Print_InfoMsg      : Printf.StringFormat<'T,unit> -> 'T  
    /// Prints without adding a new line at the end
    abstract member Print_FsiErrorMsg  : Printf.StringFormat<'T,unit> -> 'T  
    /// Prints without adding a new line at the end
    abstract member Print_AppErrorMsg  : Printf.StringFormat<'T,unit> -> 'T  
    /// Prints without adding a new line at the end
    abstract member Print_IOErrorMsg   : Printf.StringFormat<'T,unit> -> 'T         
    /// Prints without adding a new line at the end
    abstract member Print_DebugMsg     : Printf.StringFormat<'T,unit> -> 'T     
    /// Print using the Brush or color provided 
    /// at last custom printing call via PrintCustomBrush or PrintCustomColor 
    /// without adding a new line at the end
    abstract member Print_Custom : Printf.StringFormat<'T,unit> -> 'T

            // Change custom color to a new SolidColorBrush (e.g. from System.Windows.Media.Brushes)
            // This will also freeze the Brush.
            // Then print without adding a new line at the end
            //abstract member Print_CustomBrush : SolidColorBrush -> Printf.StringFormat<'T,unit> -> 'T 

    /// Change custom color to a RGB value ( each between 0 and 255) 
    /// Then print without adding a new line at the end
    abstract member Print_CustomColor : int ->  int ->  int ->  Printf.StringFormat<'T,unit> -> 'T 

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
    /// returns file name or "*noName*"
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

type CodeToEval = {code:string; file:FilePath; allOfFile:bool; fromLine:int} 

type FsiState =  Ready | Evaluating | Initalizing | NotLoaded

type FsiMode  = Sync | Async

type FsiIsCancelingOk = NotEvaluating | YesAsync | Dont | NotPossibleSync // Not-Possible-Sync because during sync eval the ui should be frozen anyway and this request should not be happening
     
type TextChange =  EnteredDot | EnteredOneIdentifierChar | EnteredOneNonIdentifierChar | CompletionWinClosed | OtherChange //| EnteredQuote
    
type CharBeforeQuery = Dot | NotDot
    
type PositionInCode = { lineToCaret:string ; row:int; column:int; offset:int }

// Menu and commands:

type CommandInfo = {name:string; gesture:string; cmd:ICommand; tip:string }


