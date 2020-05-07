namespace Seff

open System
open System.Windows
open System.Windows.Controls
open Seff.Util.WPF
open Seff.Util


module ModifyUI = 
   
    let toggleLogLineWrap()=
        if Config.Settings.getBool "logHasLineWrap" true then 
            Log.ReadOnlyEditor.WordWrap         <- false 
            Log.ReadOnlyEditor.HorizontalScrollBarVisibility <- ScrollBarVisibility.Auto
            Config.Settings.setBool "logHasLineWrap" false
        else
            Log.ReadOnlyEditor.WordWrap         <- true  
            Log.ReadOnlyEditor.HorizontalScrollBarVisibility <- ScrollBarVisibility.Disabled 
            Config.Settings.setBool "logHasLineWrap" true
        Config.Settings.Save ()

    //----------------------
    //-------- Fontsize-----
    //----------------------

    let setFontSize newSize = 
        Log.ReadOnlyEditor.FontSize    <- newSize
        for t in Tabs.AllTabs do                
            t.Editor.FontSize  <- newSize
        Appearance.fontSize <- newSize // use for UI completion line too
        Config.Settings.setFloat "FontSize" newSize    
        Config.Settings.Save ()
        Log.Print "new Fontsize: %.1f" newSize

    /// affects Editor and Log
    let fontBigger()= 
        let cs = Tabs.Current.Editor.FontSize
        //let step = 
        //    if   cs >= 36. then 4. 
        //    elif cs >= 20. then 2. 
        //    else                1.
        //if cs < 112. then setFontSize(cs+step)
        if cs < 250. then setFontSize(cs* 1.03) // 3% steps
    
    /// affects Editor and Log
    let fontSmaller()=
        let cs = Tabs.Current.Editor.FontSize
        //let step = 
        //    if   cs >= 36. then 4. 
        //    elif cs >= 20. then 2. 
        //    else                1.
        //if cs > 5. then setFontSize(cs-step)
        if cs > 3. then setFontSize(cs * 0.97) // 3% steps


    //--------------------------------------
    //-------- Text Selection---------------
    //--------------------------------------

    let expandSelectionToFullLines(tab:Tab) =
        let doc = tab.Editor.Document
        let st = doc.GetLineByOffset(tab.Editor.SelectionStart)
        let en = doc.GetLineByOffset(tab.Editor.SelectionStart + tab.Editor.SelectionLength)
        let stoff = st.Offset
        tab.Editor.Select(stoff,en.EndOffset-stoff)
        tab.Editor.SelectedText

    let selectAll(tab: Tab) =
        let doc = tab.Editor.Document
        tab.Editor.Select(0,doc.TextLength)




