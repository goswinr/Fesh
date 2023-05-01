namespace Seff



open System.Windows // for FontStyles
open System.Windows.Media // for FontFamily

module StyleState =  

    // used for startup only, will be set f in  Fonts.fs :
    let mutable fontEditor      = FontFamily("Consolas") 
    let mutable fontLog         = FontFamily("Consolas") 
    let mutable fontToolTip     = FontFamily("Consolas") 

    let mutable fontSize = 14.0 // will be updated via Fonts.fs

    // will be updated via Fonts.fs:
    let mutable italicBoldEditorTf  =  new Typeface(fontEditor, FontStyles.Italic, FontWeights.Bold,  FontStretches.Normal) 
    let mutable italicEditorTf      =  new Typeface(fontEditor, FontStyles.Italic, FontWeights.Bold,  FontStretches.Normal) 
    let mutable boldEditorTf        =  new Typeface(fontEditor, FontStyles.Italic, FontWeights.Normal, FontStretches.Normal)



    //TODO try https://fonttools.readthedocs.io/en/latest/subset/index.html to reduce Fira code an others to be without ligatures ?