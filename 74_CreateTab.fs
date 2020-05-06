namespace Seff

open System.IO
open System.Windows
open System.Windows.Controls
open ICSharpCode.AvalonEdit
open System.Windows.Media
open Seff.UtilWPF


module CreateTab = 

    let makeTabHeader(tab:FsxTab) = 
        let bttn = 
            new Button(
                Content = new Shapes.Path( Data = Geometry.Parse("M1,8 L8,1 M1,1 L8,8"), Stroke = Brushes.Black,  StrokeThickness = 1.0 ) ,                   
                Margin =  new Thickness(5., 0.5, 0.5, 3.), //left ,top, right, bottom
                Padding = new Thickness(2.))
        let bg = bttn.Background 
        //bttn.MouseEnter.Add (fun _ -> bttn.Background <- Brushes.Red) // control template triggers have higher precedence compared to Style triggers // https://stackoverflow.com/questions/28346852/background-does-not-change-of-button-c-sharp-wpf
        //bttn.MouseLeave.Add (fun _ -> bttn.Background <- bg)
        bttn.Click.Add (fun _ -> FileDialogs.closeTab tab |> ignore)
        let name = if tab.FileInfo.IsSome then tab.FileInfo.Value.Name else FileDialogs.textForUnsavedFile
        let txtBl = new TextBlock( Text = name , VerticalAlignment = VerticalAlignment.Bottom)        
        tab.HeaderTextBlock <- txtBl
        match tab.FileInfo with 
        |Some fi -> txtBl.ToolTip     <- "file saved at:\r\n" + fi.FullName
        |None    -> 
            txtBl.Foreground   <- Brushes.Gray
            txtBl.ToolTip     <- "this file has not yet been saved to disk"
        let p = makePanelHor [txtBl :> UIElement; bttn :> UIElement ]
        p.Margin <- new Thickness(2.5 , 0.5 , 0.5 , 2.5) //left ,top, right, bottom
        tab.Header <- p


    /// adds a new Tab item to Tab control and initializes it
    let newTab(code, fi:FileInfo option, makeCurrent) = 
        let tab = new FsxTab ()
        tab.FileInfo <- fi
        tab.Editor.Text  <- code //this trigger docChanged event , do before hooking up event
        tab.Content <- tab.Editor  
        tab.CodeAtLastSave <- code
        EventHandlers.setUpForTab tab
        XshdHighlighting.setFSharp (tab.Editor,false)

        makeTabHeader(tab)
        FileDialogs.updateHeader <- makeTabHeader //TODO why set this on every new Tab ?????
        
        //ModifyUI.markTabSaved(tab)// TODO this should not be needed
        //tab.ChangesAreProbalySaved <- true // not neded. to not show star after putting in the code 

        Search.SearchPanel.Install(tab.Editor) |> ignore
        tab.FoldingManager <- Folding.FoldingManager.Install(tab.Editor.TextArea) 

        let i = UI.tabControl.Items.Add tab  
        if makeCurrent then 
            UI.tabControl.SelectedIndex <- i
            Tab.current <- Some tab
        
        tab  
    
    