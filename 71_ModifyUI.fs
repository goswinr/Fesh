namespace Seff

open System
open System.Windows
open System.Windows.Controls
open Seff.UtilWPF
open Seff.Util


module ModifyUI = 
   
    let toggleLogLineWrap()=
        if Config.getBool "logHasLineWrap" true then 
            UI.log.WordWrap         <- false 
            UI.log.HorizontalScrollBarVisibility <- ScrollBarVisibility.Auto
            Config.setBool "logHasLineWrap" false
        else
            UI.log.WordWrap         <- true  
            UI.log.HorizontalScrollBarVisibility <- ScrollBarVisibility.Disabled 
            Config.setBool "logHasLineWrap" true
        Config.save Log.dlog

    //----------------------
    //-------- Fontsize-----
    //----------------------

    let setFontSize newSize = 
        UI.log.FontSize    <- newSize
        for t in Tab.allTabs do                
            t.Editor.FontSize  <- newSize
        Appearance.fontSize <- newSize // use for UI completion line too
        Config.setFloat "FontSize" newSize    
        Config.save Log.dlog
        Log.printf "new Fontsize: %.1f" newSize

    /// affects Editor and Log
    let fontBigger()= 
        let cs = if Tab.current.IsSome then Tab.currEditor.FontSize else Config.getFloat "FontSize" 14.
        //let step = 
        //    if   cs >= 36. then 4. 
        //    elif cs >= 20. then 2. 
        //    else                1.
        //if cs < 112. then setFontSize(cs+step)
        if cs < 250. then setFontSize(cs* 1.03) // 3% steps
    
    /// affects Editor and Log
    let fontSmaller()=
        let cs = if Tab.current.IsSome then Tab.currEditor.FontSize else Config.getFloat "FontSize" 14.
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

    let mutable state = Both

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
        if Config.getBool "isVertSplit" true then 
            mainWindow.Content <- UI.gridHor()
            Config.setBool "isVertSplit" false
        else                                      
            mainWindow.Content <- UI.gridVert()
            Config.setBool "isVertSplit" true
        Config.save Log.dlog

    let splitChangeHor (editorRow:RowDefinition) (logRow:RowDefinition) = 
        state <- Both                    
        editorRow.Height <- makeGridLength editorRow.ActualHeight
        logRow.Height    <- makeGridLength    logRow.ActualHeight
        Config.setFloat "EditorHeight"     editorRow.ActualHeight
        Config.setFloat "LogHeight"           logRow.ActualHeight            
        Config.save Log.dlog
    
    let splitChangeVert (editorCol:ColumnDefinition) (logCol:ColumnDefinition) = 
        state <- Both                    
        editorCol.Width <- makeGridLength editorCol.ActualWidth
        logCol.Width    <- makeGridLength    logCol.ActualWidth
        Config.setFloat "EditorWidth"     editorCol.ActualWidth
        Config.setFloat "LogWidth"           logCol.ActualWidth            
        Config.save Log.dlog
        
    let maxLog () = 
        match state with
        |MaxLog -> // if is already max size down again
            state <- Both
            UI.editorRowHeight.Height   <- makeGridLength <|Config.getFloat "EditorHeight" 99.// TODO ad vert
            UI.logRowHeight.Height      <- makeGridLength <|Config.getFloat "LogHeight"    99.
            UI.editorColumnWidth.Width  <- makeGridLength <|Config.getFloat "EditorWidth" 99.
            UI.logColumnWidth.Width     <- makeGridLength <|Config.getFloat "LogWidth"    99.
            if not wasMax then mainWindow.WindowState <- WindowState.Normal
        |Both ->
            state <- MaxLog
            wasMax <-isMinOrMax
            if not isMinOrMax then mainWindow.WindowState <- WindowState.Maximized
            UI.editorRowHeight.Height   <- makeGridLength 0.
            UI.logRowHeight.Height      <- makeGridLength 999.
            UI.editorColumnWidth.Width  <- makeGridLength 0.
            UI.logColumnWidth.Width     <- makeGridLength 999.
        
    
    let init (win) = // set startup location and size:     
        mainWindow <- win
        if Config.getBool "WindowIsMax" false then
            mainWindow.WindowState <- WindowState.Maximized
            isMinOrMax <- true
            wasMax <- true
        else
            mainWindow.WindowStartupLocation <- WindowStartupLocation.Manual
            //let maxW = float <| Array.sumBy (fun (sc:Forms.Screen) -> sc.WorkingArea.Width)  Forms.Screen.AllScreens  // neded for dual screens, needs wins.forms
            //let maxH = float <| Array.sumBy (fun (sc:Forms.Screen) -> sc.WorkingArea.Height) Forms.Screen.AllScreens //https://stackoverflow.com/questions/37927011/in-wpf-how-to-shift-a-win-onto-the-screen-if-it-is-off-the-screen/37927012#37927012
            
            let maxW = SystemParameters.VirtualScreenWidth   + 8.0
            let maxH = SystemParameters.VirtualScreenHeight  + 8.0 // somehow a windocked on the right is 7 pix bigger than the screen ??
            mainWindow.Top <-     Config.getFloat "WindowTop"    0.0
            mainWindow.Left <-    Config.getFloat "WindowLeft"   0.0 
            mainWindow.Height <-  Config.getFloat "WindowHeight" 800.0
            mainWindow.Width <-   Config.getFloat "WindowWidth"  800.0
            if  mainWindow.Top  < -8. || mainWindow.Height + mainWindow.Top  > maxH || // verify window fits screen (second screen might be off)
                mainWindow.Left < -8. || mainWindow.Width  + mainWindow.Left > maxW then                    
                    mainWindow.Top <-   0.0 ; mainWindow.Height <- 600.0
                    mainWindow.Left <-  0.0 ; mainWindow.Width  <- 800.0

          


