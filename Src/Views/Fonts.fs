namespace Seff.Views

open System
open System.Windows.Media
open Seff.Model
open Seff
open AvalonEditB.Rendering

type Fonts (grid:TabsAndLog) = // will be constructed as part of Commands class
    let log = grid.Log
    let tabs= grid.Tabs
    let sett = grid.Config.Settings
    let comma = "; "

    let mediaUri =  new Uri("pack://application:,,,/Seff;component/Media/")
    //let fontsUri =  new Uri("pack://application:,,,/Seff;component/Media/#")
    //let resFontsLazy = lazy (Fonts.GetFontFamilies(fontsUri))

    let defaultFontNames = [| 
        "Cascadia Mono" // Cascadia Mono is without ligatures
        "Consolas" // only consolas renders fast
        |]  

    /// test if font is installed
    let isInstalled (f:FontFamily) = 
        let n = f.FamilyNames.Values |> Seq.head
        if f.Source.Contains(n) then  // source might stat with ./#
            true
        else            
            false 
    
    // try get sztem font and rescource font
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

    let tryGetFontOrAlt(key) =
        match sett.Get key with 
        |Some na -> 
            match getFontThatIsInstalled(na) with 
            |Some f -> Some f
            |None -> 
                defaultFontNames
                |> Array.filter ( fun n -> n.ToLower() <> na.ToLower()) // becaus the desired font might be already in default font names
                |> Array.tryPick getFontThatIsInstalled
                |> Option.orElseWith ( fun () ->
                    ISeffLog.log.PrintfnIOErrorMsg $"Fonts.{key}: faild to load font '{na}' or any of [{defaultFontNames |> String.concat comma}]"
                    None)
        |None -> 
            defaultFontNames
            |> Array.tryPick getFontThatIsInstalled
            |> Option.orElseWith ( fun () ->
                    ISeffLog.log.PrintfnIOErrorMsg $"Fonts.{key}: faild to load any font of [{defaultFontNames |> String.concat comma}]"
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
            Style.fontLog <- f
            log.AvalonLog.FontFamily <- f
            sett.Set ("FontLog", f.Source)
            sett.Save()
        |None ->  ()              
        
    let setEditor() = // on log and all tabs
        match tryGetFontOrAlt "FontEditor" with 
        |Some f -> 
            Style.fontEditor <- f
            for t in tabs.AllTabs do  t.Editor.AvaEdit.FontFamily  <- f
            sett.Set ("FontEditor", f.Source)
            sett.Save() 

            //match  f.FamilyTypefaces |> Seq.tryFind ( fun tf ->  tf.Style = Windows.FontStyles.Oblique && tf.Weight.ToString() = "Normal" ) with 
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
            Style.fontToolTip <- f
            sett.Set ("FontToolTip", f.Source)
            sett.Save()
        |None -> ()

    let setSize (newSize:float) = // on log and all tabs
        log.AvalonLog.FontSize <- newSize
        for t in tabs.AllTabs do
            t.Editor.AvaEdit.FontSize  <- newSize
        sett.SetFloat ("FontSize", newSize)
        Style.fontSize <- newSize
        sett.Save ()
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
        if cs < 250. then setSize(cs * 1.02) // 2% steps

        //let step = 
        //    if   cs >= 36. then 4.
        //    elif cs >= 20. then 2.
        //    else                1.
        //if cs < 112. then setSize(cs+step)


    /// affects Editor and Log
    member this.FontsSmaller()= 
        let cs = tabs.Current.Editor.AvaEdit.FontSize
        if cs > 3. then setSize(cs / 1.02) // 2% steps

        //let step = 
        //    if   cs >= 36. then 4.
        //    elif cs >= 20. then 2.
        //    else                1.
        //if cs > 5. then setSize(cs-step)


