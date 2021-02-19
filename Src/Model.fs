namespace Seff.Model

open System
open System.IO
open System.Windows.Media // for color brushes
open System.Windows.Input
open ICSharpCode
open FSharp.Compiler.SourceCodeServices
open System.Text
open ICSharpCode.AvalonEdit.Folding


type ISeffLog = 
    // this interface allows the Config to be declared before the Log
    // the Log is created first with this interface and then Config gets it in the constructor
    
    /// The log editor is readOnly for users , not for the API
    abstract member ReadOnlyEditor   : AvalonEdit.TextEditor
    
    /// The log editor and document is readOnly for users , not for the API
    /// use this to call doc.CreateSnapshot().Text  from other threads
    abstract member ReadOnlyDoc   : AvalonEdit.Document.TextDocument 

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

[<CompiledName("ISeffLogModule")>] //don't rename used via reflection in FsEx.Print
module ISeffLog = 

    /// a refrence to the global single instance of the Log view, will be set immediatly after construction
    /// declared here  in Utils so it can be used in othet moduled that are declared before Log view
    let mutable log = 
        Unchecked.defaultof<ISeffLog> //set when Log instance is created
    
    //let mutable printfCustomColor  : int ->  int ->  int ->  Printf.StringFormat<'T,unit> -> 'T  = fun red green blue msg ->  Printf.kprintf (fun s -> printf "%s" s)  msg 

    //let mutable PrintfnColor : int ->  int ->  int ->  Printf.StringFormat<'T,unit> -> 'T =  fun red green blue msg ->  Printf.kprintf (fun s -> printfn "%s" s)  msg 

    let mutable printColor : int-> int -> int -> string -> unit = //don't rename used via reflection in FsEx //reset when Log instance is created
        fun r g b s -> printf "%s" s    
  
    let mutable printnColor : int-> int -> int -> string -> unit = //don't rename used via reflection in FsEx //reset when Log instance is created
        fun r g b s -> printfn "%s" s

    let mutable clear : unit -> unit = fun () -> () //don't rename used via reflection in FsEx //reset when Log instance is created
        


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


