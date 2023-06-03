namespace Seff.Model

open System
open System.IO
open System.Windows.Input

open AvalonEditB
open AvalonEditB.Folding

open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler


type ISeffLog = 

    // this interface allows the Config to be declared before the Log
    // the Log is created first with this interface and then Config gets it in the constructor   
    abstract member PrintfnRuntimeErr  : Printf.StringFormat<'T,unit> -> 'T
    abstract member PrintfnInfoMsg     : Printf.StringFormat<'T,unit> -> 'T
    abstract member PrintfnFsiErrorMsg : Printf.StringFormat<'T,unit> -> 'T
    abstract member PrintfnAppErrorMsg : Printf.StringFormat<'T,unit> -> 'T
    abstract member PrintfnIOErrorMsg  : Printf.StringFormat<'T,unit> -> 'T
    abstract member PrintfnDebugMsg    : Printf.StringFormat<'T,unit> -> 'T

    /// Prints without adding a new line at the end
    abstract member PrintfFsiErrorMsg  : Printf.StringFormat<'T,unit> -> 'T

    /// Change custom color to a RGB value ( each between 0 and 255) , then print
    abstract member PrintfnColor : int ->  int ->  int ->  Printf.StringFormat<'T,unit> -> 'T

    /// Change custom color to a RGB value ( each between 0 and 255)
    /// Then print without adding a new line at the end
    abstract member PrintfColor : int ->  int ->  int ->  Printf.StringFormat<'T,unit> -> 'T

    //used in FSI constructor:
    abstract member TextWriterFsiStdOut    : TextWriter
    abstract member TextWriterFsiErrorOut  : TextWriter
    abstract member TextWriterConsoleOut   : TextWriter
    abstract member TextWriterConsoleError : TextWriter

    /// An additional textwriter to also write Info, AppError, IOError, Debug and FsiError messages to.
    /// But not any other text printed with any custom color. 
    abstract member AdditionalLogger : option<TextWriter> with get,set

    abstract member Clear : unit -> unit

[<RequireQualifiedAccess>]
[<CompiledName("ISeffLogModule")>] // DON'T RENAME !! It is used via reflection in https://github.com/goswinr/FsEx 
module ISeffLog = 

    /// A reference to the global single instance of the Log view, will be set immediately after construction
    /// declared here in Utils so it can be used in other modules that are declared before Log view.
    let mutable log = 
        Unchecked.defaultof<ISeffLog> //set immediately when Log instance is created in function Initialize.everything()

    /// A simple error logging function using PrintfnAppErrorMsg.
    let printError s = 
        if Object.ReferenceEquals(log,null) then printfn "%s" s
        else log.PrintfnAppErrorMsg "%s" s

    let mutable printColor : int-> int -> int -> string -> unit = //don't rename!! It's used via reflection in FsEx.
        fun r g b s -> printf "%s" s  //this implementation is changed  when Log instance is created.

    let mutable printnColor : int-> int -> int -> string -> unit = //don't rename!! It's used via reflection in FsEx.
        fun r g b s -> printfn "%s" s //this implementation is changed  when Log instance is created.

    let mutable clear : unit -> unit =  //don't rename!! It's used via reflection in FsEx
        fun () -> () //implementation is changed  when Log instance is created


// ---- Editor types -----------

/// To give each doc chnage a unique ID
/// Only chanages while not waiting for completion window
/// So that at we can check if a CheckResult still corresponds to the latest changes
type ChangeId = int64

/// just an alias for a string
type CodeAsString = string

/// for offsets into the fullCode
[<Measure>] type off 

/// The errors sorted into groups
type ErrorsBySeverity = {
    errors             : ResizeArray<Diagnostics.FSharpDiagnostic>
    warnings           : ResizeArray<Diagnostics.FSharpDiagnostic>
    infos              : ResizeArray<Diagnostics.FSharpDiagnostic>
    hiddens            : ResizeArray<Diagnostics.FSharpDiagnostic> 
    errorsAndWarnings  : ResizeArray<Diagnostics.FSharpDiagnostic> 
    }

/// The Results from FSharp.Compiler.Service
type FullCheckResults = {
    parseRes    :FSharpParseFileResults
    checkRes    :FSharpCheckFileResults
    errors      :ErrorsBySeverity
    changeId    :ChangeId 
    editor      :TextEditor
    }

type FileCheckState =     
    | Checking 
    | Done     of FullCheckResults


type FilePath = 
    | SetTo   of FileInfo
    | Deleted of FileInfo
    | NotSet  of string // the dummy name to display in the tab header

    member this.DoesNotExistsAsFile = match this with  SetTo _ ->  false | Deleted _ |NotSet _ -> true
    member this.ExistsAsFile        = match this with  SetTo _ ->  true  | Deleted _ |NotSet _ -> false
    member this.FileName            = match this with  SetTo f | Deleted f ->  f.Name |NotSet f -> f

  

// so that the Editor can be used before declared
type IEditor = 
    abstract member AvaEdit        : TextEditor    
    abstract member FileCheckState : FileCheckState with get , set
    abstract member FilePath       : FilePath // saving settings in config , like fold status
    abstract member IsComplWinOpen : bool   // for  checking when modifying keyboard events
    abstract member FoldingManager : FoldingManager // so that fsi can go to error location and unfold    /
    abstract member EvaluateFrom   : int option // the line number to start evaluating from if EvaluationTracker is active

[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module IEditor = 
    /// A global reference to the current Editor
    let mutable current :option<IEditor> = None

    /// A global reference to the current main window
    let mutable mainWindow = Unchecked.defaultof<FsEx.Wpf.PositionedWindow>

    let isCurrent(e:TextEditor)= 
        match current with
        |None   -> false
        |Some o -> o.AvaEdit = e 



//---- Fsi types ------------
type CodeSegment = {
    text:CodeAsString
    startLine:int
    startOffset:int
    length:int
    }

type FsiCodeAmount = 
    | All
    | ContinueFromChanges 
    | FsiSegment of  CodeSegment

type CodeToEval = {
    editor:IEditor
    amount:FsiCodeAmount
    logger:option<TextWriter>
    scriptName:string // the file name for reporting in errors
    }


type PositionInCode = { 
    lineToCaret:string  
    row:int
    column:int 
    offset:int 
    }

/// UI Menu and commands:
type CommandInfo = {
    name:string
    gesture:string 
    cmd:ICommand 
    tip:string 
    }

