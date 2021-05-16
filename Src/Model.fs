namespace Seff.Model

open System
open System.IO
open System.Windows.Media // for color brushes
open System.Windows.Input
open FSharp.Compiler.SourceCodeServices
open System.Text
open AvalonEditB
open AvalonEditB.Folding


type ISeffLog = 
    // this interface allows the Config to be declared before the Log
    // the Log is created first with this interface and then Config gets it in the constructor
    
    /// The log editor is readOnly for users , not for the API
    abstract member ReadOnlyEditor   : TextEditor
    
    /// The log editor and document is readOnly for users , not for the API
    /// use this to call doc.CreateSnapshot().Text  from other threads
    abstract member ReadOnlyDoc   : Document.TextDocument 

    abstract member FsiErrorStream     : StringBuilder
    

    abstract member PrintfnInfoMsg     : Printf.StringFormat<'T,unit> -> 'T 
    abstract member PrintfnFsiErrorMsg : Printf.StringFormat<'T,unit> -> 'T  
    abstract member PrintfnAppErrorMsg : Printf.StringFormat<'T,unit> -> 'T  
    abstract member PrintfnIOErrorMsg  : Printf.StringFormat<'T,unit> -> 'T  
    abstract member PrintfnDebugMsg    : Printf.StringFormat<'T,unit> -> 'T
    
    /// Print using the same color as in last print call
    abstract member PrintfnLastColor     : Printf.StringFormat<'T,unit> -> 'T

    /// Change custom color to a RGB value ( each between 0 and 255) , then print 
    abstract member PrintfnColor : int ->  int ->  int ->  Printf.StringFormat<'T,unit> -> 'T 
    
    //--- without new line: --------------

    /// Prints without adding a new line at the end
    abstract member PrintfInfoMsg      : Printf.StringFormat<'T,unit> -> 'T  
    
    /// Prints without adding a new line at the end
    abstract member PrintfFsiErrorMsg  : Printf.StringFormat<'T,unit> -> 'T  
    
    /// Prints without adding a new line at the end
    abstract member PrintfAppErrorMsg  : Printf.StringFormat<'T,unit> -> 'T  
    
    /// Prints without adding a new line at the end
    abstract member PrintfIOErrorMsg   : Printf.StringFormat<'T,unit> -> 'T         
    
    /// Prints without adding a new line at the end
    abstract member PrintfDebugMsg     : Printf.StringFormat<'T,unit> -> 'T     
    
    /// Print using the same color as in last print call
    /// without adding a new line at the end
    abstract member PrintfLastColor : Printf.StringFormat<'T,unit> -> 'T

    /// Change custom color to a RGB value ( each between 0 and 255) 
    /// Then print without adding a new line at the end
    abstract member PrintfColor : int ->  int ->  int ->  Printf.StringFormat<'T,unit> -> 'T 

    //used in FSI constructor:
    abstract member TextWriterFsiStdOut    : TextWriter
    abstract member TextWriterFsiErrorOut  : TextWriter
    abstract member TextWriterConsoleOut   : TextWriter
    abstract member TextWriterConsoleError : TextWriter

    abstract member Clear : unit -> unit

[<CompiledName("ISeffLogModule")>] //don't rename, it is used via reflection in FsEx.Print
module ISeffLog = 

    /// A refrence to the global single instance of the Log view, will be set immediatly after construction
    /// declared here  in Utils so it can be used in other modules that are declared before Log view
    let mutable log = 
        Unchecked.defaultof<ISeffLog> //set when Log instance is created    

    let mutable printColor : int-> int -> int -> string -> unit = //don't rename!! It's used via reflection in FsEx 
        fun r g b s -> printf "%s" s  //implementation is chanaged  when Log instance is created  
  
    let mutable printnColor : int-> int -> int -> string -> unit = //don't rename!! It's used via reflection in FsEx 
        fun r g b s -> printfn "%s" s //implementation is chanaged  when Log instance is created  

    let mutable clear : unit -> unit =  //don't rename!! It's used via reflection in FsEx
        fun () -> () //implementation is chanaged  when Log instance is created  
        


/// ---- Editor types -----------

/// To give each call to the Fs Checker a unique ID
/// So that at we can check if a local and a global CheckResult are the same
type CheckId = int64

/// The Result when trying to get the current code from the checker 
/// (and not from the editor where the tree would have to be converted to a string)
type FullCodeAndId = 
    | CodeID of string * CheckId
    | NoCode

/// the Code beeing evaluated
type Code = 
    | FullCode of string 
    | PartialCode of string
    member this.Code = match this with  FullCode s -> s  | PartialCode s -> s
        
/// The Results from FSharp.Compiler.Service
type CheckResults = { 
    parseRes    :FSharpParseFileResults 
    checkRes    :FSharpCheckFileResults 
    code        :Code 
    checkId     :CheckId     } 


/// Represents the current sate of the  FSharp.Compiler.Service Checker
/// It is stored globally in the Checker
/// And locally in each Editor instance (they are compared via the CheckId)
type FileCheckState = 
    | NotStarted 

    /// Getting the code form avalon edit text editor asynchronous
    | GettingCode of CheckId 

    /// Got the code form avalon edit async, now running in FCS async
    | Checking of CheckId * Code 

    /// The CheckResults are always local per Editor
    | Done of CheckResults 

    | Failed 

    member this.FullCodeAndId  =         
        match this with
        | NotStarted | GettingCode _  | Failed -> NoCode
        | Checking (id, c)  ->  match c        with  FullCode s -> CodeID (s,id          )  | PartialCode _ -> NoCode
        | Done res          ->  match res.code with  FullCode s -> CodeID (s,res.checkId )  | PartialCode _ -> NoCode

    /// to compare local EditorCheckState with GlobalCheckState
    member this.SameIdAndFullCode (globalChSt:FileCheckState) =  
        match this.FullCodeAndId with
        |NoCode -> NoCode
        |CodeID (id, c)  ->
            match globalChSt.FullCodeAndId with 
            |NoCode -> NoCode
            |CodeID (gid, _) as ci -> if gid=id then ci  else NoCode

type FilePath = 
    | SetTo of FileInfo 
    | NotSet
    /// returns file name or "*noName*"
    member this.File = match this with SetTo fi -> fi.Name |NotSet -> "*noName*"


// so that the Editor can be used before declared
type IEditor = 
    abstract member Id             : Guid
    abstract member AvaEdit        : TextEditor
    abstract member FileCheckState : FileCheckState with get , set 
    abstract member FilePath       : FilePath 
    abstract member Log            : ISeffLog 
    abstract member FoldingManager : FoldingManager 
    abstract member IsComplWinOpen : bool 





        



        




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


