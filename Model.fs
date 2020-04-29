namespace Seff

open System
open System.Windows.Media // for color brushes

module Model =
    
    type LogMessageType = 
        | FsiStdOut 
        | FsiErrorOut 
        | ConsoleOut
        | ConsoleError
        | InfoMsg 
        | FsiErrorMsg 
        | AppErrorMsg 
        | IOErrorMsg 
        | DebugMsg 
        | PrintMsg

        static member getColor = function
            | FsiStdOut     ->Brushes.DarkGray // values printet by fsi iteself like "val it = ...."
            | FsiErrorOut   ->Brushes.Red
            | ConsoleOut    ->Brushes.DarkGreen   // the out from printfn
            | ConsoleError  ->Brushes.LightSalmon // this is never used, only FsiErrorOut is used?
            | InfoMsg       ->Brushes.Blue
            | FsiErrorMsg   ->Brushes.DarkMagenta
            | AppErrorMsg   ->Brushes.DarkOrange
            | IOErrorMsg    ->Brushes.DarkRed
            | DebugMsg      ->Brushes.Green
            | PrintMsg      ->Brushes.DarkCyan // never used ? only ConsoleOut is used?


    type AppRunContext = 
        |Standalone 
        |Hosted of string

    
    type FsiState =  Ready | Evaluating | Initalizing | NotLoaded

    type FsiMode  = Sync | Async

    type FsiIsCancelingOk = NotEvaluating | YesAsync | Dont | NotPossibleSync // Not-Possible-Sync because during sync eval the ui should be frozen anyway and this request should not be happening
     
    
    
    let defaultCodeOnFirstRun =
        [
        "// this is your default code for new files, you can change it by going to the menu: File -> Edit Template File"
        "// or in your local AppData folder: Environment.SpecialFolder.LocalApplicationData/Seff"
        //"tips: // https://panesofglass.github.io/scripting-workshop/#/" 
        //"tips: // http://brandewinder.com/2016/02/06/10-fsharp-scripting-tips/"        
        "open System"
        // "Environment.CurrentDirectory <- __SOURCE_DIRECTORY__"
        ""
        ] 
        |> String.concat Environment.NewLine