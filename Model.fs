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
            | FsiStdOut     ->Brushes.DarkGray
            | FsiErrorOut   ->Brushes.DarkMagenta
            | ConsoleOut    ->Brushes.DarkGreen  // printfn in script
            | ConsoleError  ->Brushes.Magenta
            | InfoMsg       ->Brushes.Blue
            | FsiErrorMsg   ->Brushes.Red
            | AppErrorMsg   ->Brushes.DarkOrange
            | IOErrorMsg    ->Brushes.DarkRed
            | DebugMsg      ->Brushes.Green
            | PrintMsg      ->Brushes.DarkCyan


    type RunContext = 
        |Standalone 
        |Hosted of string

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