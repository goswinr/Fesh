namespace Seff

open System
open System.Windows
open System.Windows.Controls
open Seff.UtilWPF
open Seff.Util
open Seff.Logging

module ModifyUI = 
   
    let toggleLogLineWrap()=
        if Config.Settings.getBool "logHasLineWrap" true then 
            UI.log.WordWrap         <- false 
            UI.log.HorizontalScrollBarVisibility <- ScrollBarVisibility.Auto
            Config.Settings.setBool "logHasLineWrap" false
        else
            UI.log.WordWrap         <- true  
            UI.log.HorizontalScrollBarVisibility <- ScrollBarVisibility.Disabled 
            Config.Settings.setBool "logHasLineWrap" true
        Config.Settings.Save ()

    //----------------------
    //-------- Fontsize-----
    //----------------------

    let setFontSize newSize = 
        UI.log.FontSize    <- newSize
        for t in Tab.allTabs do                
            t.Editor.FontSize  <- newSize
        Appearance.fontSize <- newSize // use for UI completion line too
        Config.Settings.setFloat "FontSize" newSize    
        Config.Settings.Save ()
        Log.print "new Fontsize: %.1f" newSize

    /// affects Editor and Log
    let fontBigger()= 
        let cs = if Tab.current.IsSome then Tab.currEditor.FontSize else Config.Settings.getFloat "FontSize" 14.
        //let step = 
        //    if   cs >= 36. then 4. 
        //    elif cs >= 20. then 2. 
        //    else                1.
        //if cs < 112. then setFontSize(cs+step)
        if cs < 250. then setFontSize(cs* 1.03) // 3% steps
    
    /// affects Editor and Log
    let fontSmaller()=
        let cs = if Tab.current.IsSome then Tab.currEditor.FontSize else Config.Settings.getFloat "FontSize" 14.
        //let step = 
        //    if   cs >= 36. then 4. 
        //    elif cs >= 20. then 2. 
        //    else                1.
        //if cs > 5. then setFontSize(cs-step)
        if cs > 3. then setFontSize(cs * 0.97) // 3% steps

    //--------------------------------------
    //-------- Marking unsaved files Tab-----
    //--------------------------------------

    let markTabUnSaved(tab:FsxTab) = 
        if tab.ChangesAreProbalySaved then 
            if notNull tab.HeaderTextBlock then 
                tab.ChangesAreProbalySaved <- false
                if tab.FileInfo.IsSome then
                    tab.HeaderTextBlock.Text <- tab.FileInfo.Value.Name+" *"                 
                    tab.HeaderTextBlock.ToolTip <- "file with unsaved changes from:\r\n" + tab.FileInfo.Value.FullName

    let markTabSaved(tab:FsxTab)   = 
        if not tab.ChangesAreProbalySaved then 
            if notNull tab.HeaderTextBlock then 
                tab.ChangesAreProbalySaved <- true
                tab.HeaderTextBlock.Text <- tab.FileInfo.Value.Name
                tab.HeaderTextBlock.ToolTip <- "file saved at:\r\n"  + tab.FileInfo.Value.FullName

    //--------------------------------------
    //-------- Text Selection---------------
    //--------------------------------------

    let expandSelectionToFullLines(tab: FsxTab) =
        let doc = tab.Editor.Document
        let st = doc.GetLineByOffset(tab.Editor.SelectionStart)
        let en = doc.GetLineByOffset(tab.Editor.SelectionStart + tab.Editor.SelectionLength)
        let stoff = st.Offset
        tab.Editor.Select(stoff,en.EndOffset-stoff)
        tab.Editor.SelectedText

    let selectAll(tab: FsxTab) =
        let doc = tab.Editor.Document
        tab.Editor.Select(0,doc.TextLength)



module WindowLayout =  
    
    type Size = MaxLog | Both 

    let mutable logWinstate = Both

    // saving layout of Log and editor window:
    let mutable mainWindow:Window = null // needed for maxing it, will be set in 'init(win)'
    let mutable isMinOrMax = false
    let mutable wasMax = false
    
    let toggleSplit() =        
        let rec clean (e:obj) = // clean grid up first //BindingOperations.ClearAllBindings(UI.editorRowHeight  ) // not needed
            match e with
            | :? Grid as g -> 
                for ch in g.Children do clean ch
                g.Children.Clear()
                g.RowDefinitions.Clear()
                g.ColumnDefinitions.Clear()
            | _ -> ()                
        clean mainWindow.Content
        if Config.Settings.getBool "isVertSplit" true then 
            mainWindow.Content <- UI.gridHor()
            Config.Settings.setBool "isVertSplit" false
        else                                      
            mainWindow.Content <- UI.gridVert()
            Config.Settings.setBool "isVertSplit" true
        Config.Settings.Save ()

    let splitChangeHor (editorRow:RowDefinition) (logRow:RowDefinition) = 
        logWinstate <- Both                    
        editorRow.Height <- makeGridLength editorRow.ActualHeight
        logRow.Height    <- makeGridLength    logRow.ActualHeight
        Config.Settings.setFloat "EditorHeight"     editorRow.ActualHeight
        Config.Settings.setFloat "LogHeight"           logRow.ActualHeight            
        Config.Settings.Save ()
    
    let splitChangeVert (editorCol:ColumnDefinition) (logCol:ColumnDefinition) = 
        logWinstate <- Both                    
        editorCol.Width <- makeGridLength editorCol.ActualWidth
        logCol.Width    <- makeGridLength    logCol.ActualWidth
        Config.Settings.setFloat "EditorWidth"     editorCol.ActualWidth
        Config.Settings.setFloat "LogWidth"           logCol.ActualWidth            
        Config.Settings.Save ()
        
    let maxLog () = 
        match logWinstate with
        |MaxLog -> // if is already max size down again
            logWinstate <- Both
            UI.editorRowHeight.Height   <- makeGridLength <|Config.Settings.getFloat "EditorHeight" 99.// TODO ad vert
            UI.logRowHeight.Height      <- makeGridLength <|Config.Settings.getFloat "LogHeight"    99.
            UI.editorColumnWidth.Width  <- makeGridLength <|Config.Settings.getFloat "EditorWidth" 99.
            UI.logColumnWidth.Width     <- makeGridLength <|Config.Settings.getFloat "LogWidth"    99.
            if not wasMax then mainWindow.WindowState <- WindowState.Normal
        |Both ->
            logWinstate <- MaxLog
            wasMax <-isMinOrMax
            if not isMinOrMax then mainWindow.WindowState <- WindowState.Maximized
            UI.editorRowHeight.Height   <- makeGridLength 0.
            UI.logRowHeight.Height      <- makeGridLength 999.
            UI.editorColumnWidth.Width  <- makeGridLength 0.
            UI.logColumnWidth.Width     <- makeGridLength 999.
        
    
    let init (win) = // set startup location and size:     
        mainWindow <- win
        if Config.Settings.getBool "WindowIsMax" false then
            mainWindow.WindowState <- WindowState.Maximized
            isMinOrMax <- true
            wasMax <- true
        else
            mainWindow.WindowStartupLocation <- WindowStartupLocation.Manual
            //let maxW = float <| Array.sumBy (fun (sc:Forms.Screen) -> sc.WorkingArea.Width)  Forms.Screen.AllScreens  // neded for dual screens, needs wins.forms
            //let maxH = float <| Array.sumBy (fun (sc:Forms.Screen) -> sc.WorkingArea.Height) Forms.Screen.AllScreens //https://stackoverflow.com/questions/37927011/in-wpf-how-to-shift-a-win-onto-the-screen-if-it-is-off-the-screen/37927012#37927012
            
            let maxW = SystemParameters.VirtualScreenWidth   + 8.0
            let maxH = SystemParameters.VirtualScreenHeight  + 8.0 // somehow a windocked on the right is 7 pix bigger than the screen ??
            mainWindow.Top <-     Config.Settings.getFloat "WindowTop"    0.0
            mainWindow.Left <-    Config.Settings.getFloat "WindowLeft"   0.0 
            mainWindow.Height <-  Config.Settings.getFloat "WindowHeight" 800.0
            mainWindow.Width <-   Config.Settings.getFloat "WindowWidth"  800.0
            if  mainWindow.Top  < -8. || mainWindow.Height + mainWindow.Top  > maxH || // verify window fits screen (second screen might be off)
                mainWindow.Left < -8. || mainWindow.Width  + mainWindow.Left > maxW then                    
                    mainWindow.Top <-   0.0 ; mainWindow.Height <- 600.0
                    mainWindow.Left <-  0.0 ; mainWindow.Width  <- 800.0

          


