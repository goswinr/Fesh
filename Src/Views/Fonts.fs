namespace Seff.Views

open Seff
open System
open Seff.Config
open Seff.Util.General
open System.Windows.Media

type Fonts (grid:TabsAndLog) = // will be contructed as part of Commands class
    let log = grid.Log
    let tabs= grid.Tabs
    let config = grid.Config
    
    let fontsUri =  new Uri("pack://application:,,,/Seff;component/Media/#")
    let mediaUri =  new Uri("pack://application:,,,/Seff;component/Media/")


    let setSize (newSize:float) = // on log and all tabs
        
        // 17.0252982466288 this fonsize makes block selection delete fail on the last line: 17.0252982466288
        //let newSize = grid.Config.Settings.roundToOneDigitBehindComa(newSizeUnRounded)
        
        log.ReadOnlyEditor.FontSize <- newSize
        for t in tabs.AllTabs do                
            t.Editor.AvaEdit.FontSize  <- newSize        
        config.Settings.SetFloat "FontSize" newSize 
        Style.fontSize <- newSize
        config.Settings.Save ()
        log.PrintfnInfoMsg "new Fontsize: %.2f" newSize
    

    let verifyFont(f:FontFamily) =
        let n = f.FamilyNames.Values |> Seq.head
        if f.Source.Contains(n) then  // scource migth stat with ./#
            true
        else
            log.PrintfnAppErrorMsg "Font '%s' could not be loaded. Loaded '%s' instead." f.Source n
            log.PrintfnAppErrorMsg "Fonts found in Rescources in folder Media:"
            for fo in Fonts.GetFontFamilies(fontsUri) do
                log.PrintfnAppErrorMsg "'%s'" fo.Source    
            false

    let setLog(font:FontFamily) = // on log and all tabs
        Style.fontLog <- font
        log.ReadOnlyEditor.FontFamily<- font        
        config.Settings.Set "FontLog" font.Source
        config.Settings.Save ()
           
    let setEditor(font:FontFamily) = // on log and all tabs        
        Style.fontEditor<- font
        for t in tabs.AllTabs do                
            t.Editor.AvaEdit.FontFamily  <- font 
        config.Settings.Set "FontEditor" font.Source
        config.Settings.Save ()

    let setToolTip(font:FontFamily) = // on log and all tabs
        Style.fontToolTip<- font        
        config.Settings.Set "FontToolTip" font.Source
        config.Settings.Save ()
    

    let fromRescources(name, alternative) =
        try
            let f = new FontFamily(mediaUri,"./#"+name) 
            // TODO set ligatures?
            if not (verifyFont  f) then 
                new FontFamily(alternative) |>> verifyFont 
            else
                f
        with e -> 
            log.PrintfnAppErrorMsg "Fonts.load(\"%s\",\"%s\") failed : %A" name alternative e
            new FontFamily(alternative) |>> verifyFont 
    
    //----- init ---------
    do   
        

        //setEditor(fromRescources("Fira Code", "Consolas")) // too slow on big files, Cascadia Mono is just as bad
        setEditor(  FontFamily ("Consolas")|>> verifyFont)  // only consolas renders fast      
        //setEditor(  FontFamily ("JetBrains Mono")|>> verifyFont)  // only consolas renders fast      
        setLog(     FontFamily ("Consolas")|>> verifyFont)  
        setToolTip( FontFamily ("Verdana") |>> verifyFont) // or FontFamily("Andale Mono")?
    
    
    // this fonsize makes block selection delete fail on the last line: 17.0252982466288 happens at 17.5 too
          
    /// affects Editor and Log    
    member this.FontsBigger()= 
        let cs = tabs.Current.Editor.AvaEdit.FontSize
        
        //let step =
        //    if   cs >= 36. then 4. 
        //    elif cs >= 20. then 2. 
        //    else                1.
        //if cs < 112. then setSize(cs+step)
        
        if cs < 250. then setSize(cs * 1.03) // 3% steps
          
    /// affects Editor and Log
    member this.FontsSmaller()=
        let cs = tabs.Current.Editor.AvaEdit.FontSize
        //let step = 
        //    if   cs >= 36. then 4. 
        //    elif cs >= 20. then 2. 
        //    else                1.
        //if cs > 5. then setSize(cs-step)

        if cs > 3. then setSize(cs / 1.03) // 3% steps 

