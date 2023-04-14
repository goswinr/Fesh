namespace Seff

open System.Windows.Media // for font

module Style =  

    // used for startup only, will be set f in  Fonts.fs :
    let mutable fontEditor      = FontFamily("Consolas") 
    let mutable fontLog         = FontFamily("Consolas") 
    let mutable fontToolTip     = FontFamily("Consolas") 

    let mutable fontSize = 14.0 // will be updated via Fonts.fs

    //TODO try https://fonttools.readthedocs.io/en/latest/subset/index.html to reduce Fira code an others to be without ligatures