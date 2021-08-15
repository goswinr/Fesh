namespace Seff

open System.Windows.Media // for font

module Style =  
    let dialogCaption = "Seff | Scripting editor for fsharp"   // e.g title of saveAs window
    
    let mutable fontEditor      = FontFamily("Consolas") // used for startup only, will be set from rescources in  Fonts.fs
    let mutable fontLog         = FontFamily("Consolas") // used for startup only,
    let mutable fontToolTip     = FontFamily("Consolas") // used for startup only, will be set to Verdana in Fonts.fs

    let mutable fontSize = 14.0 // used for default startup only, will be set in Settings.fs
