namespace Seff

open System.Windows.Media // for font

module Style = 

    let mutable fontEditor      = FontFamily("Consolas") // used for startup only, will be set from resources in  Fonts.fs
    let mutable fontLog         = FontFamily("Consolas") // used for startup only,
    let mutable fontToolTip     = FontFamily("Consolas") // used for startup only, will be set to Verdana in Fonts.fs

    let mutable fontSize = 14.0 // will be updated via Fonts.fs

    //TODO try https://fonttools.readthedocs.io/en/latest/subset/index.html to reduce Fira code an others to be without ligatures