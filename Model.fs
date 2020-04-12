namespace Seff

open System

module Model =
    
    type LogMessageType = 
        | StdOut 
        | ErrorOut 
        | InfoMsg 
        | AppError 
        | IOError 
        | DebugMsg 


    type RunContext = 
        |Standalone 
        |Hosted of string

    let defaultCodeOnFirstRun =
        [
        "// this is your default code for new files, you can change by going to the menu: File -> Edit Template File"
        "// or in your local AppData folder: Environment.SpecialFolder.LocalApplicationData/Seff"
        //"tips: // https://panesofglass.github.io/scripting-workshop/#/" 
        //"tips: // http://brandewinder.com/2016/02/06/10-fsharp-scripting-tips/"        
        //"#load @\"" + General.installFolder() + "\\SeffLib.fsx\""
        "open System"
        // "Environment.CurrentDirectory <- __SOURCE_DIRECTORY__"
        ""
        ] 
        |> String.concat Environment.NewLine