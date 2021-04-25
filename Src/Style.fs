namespace Seff

open System
open System.IO
open System.Windows.Media // for color brushes
open System.Windows.Input
open FSharp.Compiler.SourceCodeServices


module Style =  
    let dialogCaption = "Seff | Scripting editor for fsharp"   // e.g title of saveAs window
    
    let mutable fontEditor      = FontFamily("Consolas") // used for startup only, will be set from rescources in  FontManager.fs
    let mutable fontLog         = FontFamily("Consolas") // used for startup only,
    let mutable fontToolTip     = FontFamily("Consolas") // used for startup only,

    let mutable fontSize = 14.0 // used for default startup only, will be set in Settings.fs
