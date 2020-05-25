namespace Seff.Views

open Seff
open System
open System.Windows.Input
open System.Windows.Controls
open Seff.Views.Util
open Seff.Config
open Seff.Util.General
open System.Collections.Generic
open System.Windows.Media

type Fonts (grid:TabsAndLog) = // will be contructed as part of Commands class
    let log = grid.Log
    let tabs= grid.Tabs
    let config = grid.Config
    
    let fontsUri =  new Uri("pack://application:,,,/Seff;component/Media/#")
    let mediaUri =  new Uri("pack://application:,,,/Seff;component/Media/")

    let setSize (newSize) = // on log and all tabs
        log.ReadOnlyEditor.FontSize <- newSize
        for t in tabs.AllTabs do                
            t.Editor.AvaEdit.FontSize  <- newSize        
        config.Settings.SetFloat "FontSize" newSize 
        Style.fontSize <- newSize
        config.Settings.Save ()
        log.PrintInfoMsg "new Fontsize: %.1f" newSize
    

    let verifyFont(f:FontFamily) =
        let n = f.FamilyNames.Values |> Seq.head
        if f.Source.Contains(n) then  // scource migth stat with ./#
            true
        else
            log.PrintAppErrorMsg "Font '%s' could not be loaded. Loaded '%s' instead." f.Source n
            log.PrintAppErrorMsg "Fonts found in Rescources in folder Media:"
            for fo in Fonts.GetFontFamilies(fontsUri) do
                log.PrintAppErrorMsg "'%s'" fo.Source    
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
            log.PrintAppErrorMsg "Fonts.load(\"%s\",\"%s\") failed : %A" name alternative e
            new FontFamily(alternative) |>> verifyFont 
    
    //----- init ---------
    do        
        setEditor(fromRescources("Fira Code", "Consolas"))
        setLog(     FontFamily ("Consolas")|>> verifyFont)  
        setToolTip( FontFamily ("Verdana") |>> verifyFont) // or FontFamily("Andale Mono")
      
          
    /// affects Editor and Log    
    member this.FontsBigger()= 
        let cs = tabs.Current.Editor.AvaEdit.FontSize
        //let step = 
        //    if   cs >= 36. then 4. 
        //    elif cs >= 20. then 2. 
        //    else                1.
        //if cs < 112. then setFontSize(cs+step)
        if cs < 250. then setSize(cs* 1.03) // 3% steps
          
    /// affects Editor and Log
    member this.FontsSmaller()=
        let cs = tabs.Current.Editor.AvaEdit.FontSize
        //let step = 
        //    if   cs >= 36. then 4. 
        //    elif cs >= 20. then 2. 
        //    else                1.
        //if cs > 5. then setFontSize(cs-step)
        if cs > 3. then setSize(cs * 0.97) // 3% steps 

