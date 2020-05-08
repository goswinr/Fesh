namespace Seff

open System.IO

type ISeffLog =
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