namespace Fesh.Views

open System
open Avalonia //for FontStyle
open Avalonia.Media // for Fontfamilly
open Fesh.Model
open Fesh
open AvaloniaEdit.Rendering

type Fonts (grid:TabsAndLog) = // will be constructed as part of Commands class
    let log = grid.Log
    let tabs= grid.Tabs
    let sett = grid.Config.Settings
    let comma = "; "

    // let mediaUri =  new Uri("pack://application:,,,/Fesh;component/Media/")
    let mediaUri = new Uri "avares://Fesh/Media/"

    //let fontsUri =  new Uri("pack://application:,,,/Fesh;component/Media/#")
    //let resFontsLazy = lazy (Fonts.GetFontFamilies(fontsUri))

    let defaultFontNames = [|
        "Cascadia Mono" // Cascadia Mono is without ligatures
        "Consolas" // only Consolas renders fast in AvalonEdit
        |]



    /// test if font is installed
    let isInstalled (f:FontFamily) =
        let n = f.FamilyNames|> Seq.head
        if f.Name.Contains(n) then  // source might stat with ./#
            true
        else
            false

    // try get system font and resource font
    let getFontThatIsInstalled (name) =
        try
            let f0 = FontFamily(name)
            if isInstalled f0 then
                Some f0
            else
                let f1 = new FontFamily(mediaUri,"./#"+name)
                if isInstalled f1 then
                    Some f1
                else
                    None
        with e ->
            None

    let tryGetFontOrAlt key =
        match sett.TryGetString key with
        |Some na ->
            match getFontThatIsInstalled(na) with
            |Some f -> Some f
            |None ->
                defaultFontNames
                |> Array.filter ( fun n -> n.ToLower() <> na.ToLower()) // because the desired font might be already in default font names
                |> Array.tryPick getFontThatIsInstalled
                |> Option.orElseWith ( fun () ->
                    IFeshLog.log.PrintfnIOErrorMsg $"Fonts.{key}: failed to load font '{na}' or any of [{defaultFontNames |> String.concat comma}]"
                    None)
        |None ->
            defaultFontNames
            |> Array.tryPick getFontThatIsInstalled
            |> Option.orElseWith ( fun () ->
                    IFeshLog.log.PrintfnIOErrorMsg $"Fonts.{key}: failed to load any font of [{defaultFontNames |> String.concat comma}]"
                    None)

    //let fontExists(f:FontFamily) =
    //    let n = f.FamilyNames.Values |> Seq.head
    //    if f.Source.Contains(n) then  // source might stat with ./#
    //        true
    //    else
    //        log.PrintfnAppErrorMsg "Font '%s' could not be loaded. Loaded '%s' instead." f.Source n
    //        let resFonts = Fonts.GetFontFamilies(fontsUri)
    //        log.PrintfnAppErrorMsg $"{resFonts.Count} Fonts found in Resources in folder Media:"
    //        for fo in Fonts.GetFontFamilies(fontsUri) do
    //            log.PrintfnAppErrorMsg "'%s'" fo.Source
    //        false

    let setLog() =
        match tryGetFontOrAlt "FontLog" with
        |Some f ->
            StyleState.fontLog <- f
            log.AvaloniaLog.FontFamily <- f
            sett.Set ("FontLog", f.Name)
        |None ->  ()

    let setEditor() = // on log and all tabs
        match tryGetFontOrAlt "FontEditor" with
        |Some f ->
            StyleState.fontEditor <- f
            StyleState.italicBoldEditorTf  <-  new Typeface(f, FontStyle.Italic, FontWeight.Bold,    FontStretch.Normal)
            StyleState.italicEditorTf      <-  new Typeface(f, FontStyle.Italic, FontWeight.Normal,  FontStretch.Normal)
            StyleState.boldEditorTf        <-  new Typeface(f, FontStyle.Normal, FontWeight.Bold,    FontStretch.Normal)

            for t in tabs.AllTabs do  t.Editor.AvaEdit.FontFamily  <- f
            sett.Set ("FontEditor", f.Name)

            //match  f.FamilyTypefaces |> Seq.tryFind ( fun tf ->  tf.Style = Windows.FontStyle.Oblique && tf.Weight.ToString() = "Normal" ) with
            //|Some ft ->
            //    let obl = Typeface(ft.)
            //    Editor.SemAction.makeCursive <- fun (el:VisualLineElement) -> el.TextRunProperties.SetTypeface ft.
            //|None -> ()
            //
            //for tf in f.FamilyTypefaces do printfn $"{f.Source} FamilyTypefaces Style Weight: {tf.Style},{tf.Weight}"

        |None -> ()


    let setToolTip() = // on log and all tabs
        match tryGetFontOrAlt "FontToolTip" with
        |Some f ->
            StyleState.fontToolTip <- f
            sett.Set ("FontToolTip", f.Name)
        |None -> ()

    let setSize (newSize:float) = // on log and all tabs
        log.AvaloniaLog.FontSize <- newSize
        for t in tabs.AllTabs do
            t.Editor.AvaEdit.FontSize  <- newSize
        sett.SetFloat ("SizeOfFont", newSize)
        StyleState.fontSize <- newSize
        log.PrintfnInfoMsg "new font size: %.2f" newSize

    //----- init ---------
    do
        setEditor()
        setLog()
        setToolTip()

    // this font size makes block selection delete fail on the last line: 17.0252982466288 happens at 17.5 too

    /// affects Editor and Log
    member this.FontsBigger()=
        let cs = tabs.Current.Editor.AvaEdit.FontSize
        if cs < 250. then setSize(cs * 1.04) // 4% steps

        //let step =
        //    if   cs >= 36. then 4.
        //    elif cs >= 20. then 2.
        //    else                1.
        //if cs < 112. then setSize(cs+step)


    /// affects Editor and Log
    member this.FontsSmaller()=
        let cs = tabs.Current.Editor.AvaEdit.FontSize
        if cs > 3. then setSize(cs / 1.04) // 4% steps

        //let step =
        //    if   cs >= 36. then 4.
        //    elif cs >= 20. then 2.
        //    else                1.
        //if cs > 5. then setSize(cs-step)

(*
module Typography =

    open Avalonia
    open Avalonia.Media.TextFormatting

    // from https://github.com/icsharpcode/AvalonEdit/blob/master/ICSharpCode.AvalonEdit/Rendering/DefaultTextRunTypographyProperties.cs
    // avalonedit comment on RunTransformers: For some strange reason, WPF requires that either all or none of the typography properties are set.

    type DefaultTextRunTypographyProperties() =
        inherit TextRunTypographyProperties()
        override this.Variants with get() = FontVariants.Normal
        override this.StylisticSet1 with get() = false
        override this.StylisticSet2 with get() = false
        override this.StylisticSet3 with get() = false
        override this.StylisticSet4 with get() = false
        override this.StylisticSet5 with get() = false
        override this.StylisticSet6 with get() = false
        override this.StylisticSet7 with get() = false
        override this.StylisticSet8 with get() = false
        override this.StylisticSet9 with get() = false
        override this.StylisticSet10 with get() = false
        override this.StylisticSet11 with get() = false
        override this.StylisticSet12 with get() = false
        override this.StylisticSet13 with get() = false
        override this.StylisticSet14 with get() = false
        override this.StylisticSet15 with get() = false
        override this.StylisticSet16 with get() = false
        override this.StylisticSet17 with get() = false
        override this.StylisticSet18 with get() = false
        override this.StylisticSet19 with get() = false
        override this.StylisticSet20 with get() = false
        override this.StylisticAlternates with get() = 0
        override this.StandardSwashes with get() = 0
        override this.StandardLigatures with get() = false
        override this.SlashedZero with get() = false
        override this.NumeralStyle with get() = FontNumeralStyle.Normal
        override this.NumeralAlignment with get() = FontNumeralAlignment.Normal
        override this.MathematicalGreek with get() = false
        override this.Kerning with get() = true
        override this.HistoricalLigatures with get() = false
        override this.HistoricalForms with get() = false
        override this.Fraction with get() = FontFraction.Normal
        override this.EastAsianWidths with get() = FontEastAsianWidths.Normal
        override this.EastAsianLanguage with get() = FontEastAsianLanguage.Normal
        override this.EastAsianExpertForms with get() = false
        override this.DiscretionaryLigatures with get() = false
        override this.ContextualSwashes with get() = 0
        override this.ContextualLigatures with get() = true
        override this.ContextualAlternates with get() = true
        override this.CaseSensitiveForms with get() = false
        override this.CapitalSpacing with get() = false
        override this.Capitals with get() = FontCapitals.Normal
        override this.AnnotationAlternates with get() = 0

        override _.StyleKeyOverride = typeof<Application> // see https://github.com/AvaloniaUI/Avalonia/discussions/18697
*)