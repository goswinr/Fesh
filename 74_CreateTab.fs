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
                Content = new Shapes.Path( Data = Geometry.Parse("M1,7 L7,1 M1,1 L7,7"), Stroke = Brushes.White,  StrokeThickness = 1.5 )                    
                , Margin = new Thickness(4.,1.,1.,1.) )
        let bg = bttn.Background 
        bttn.MouseEnter.Add (fun _ -> bttn.Background <- Brushes.Red)
        bttn.MouseLeave.Add (fun _ -> bttn.Background <- bg)
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
        p.Margin <- new Thickness(2. , 2. , 2. , 2.)
        tab.Header <- p


    /// adds a new Tab item to Tab control and initializes it
    let newTab(code, fi:FileInfo option, makeCurrent) = 
        let tab = new FsxTab ()
        tab.FileInfo <- fi
        tab.Editor.Text  <- code //this trigger docChanged event , do befor hooking up event
        tab.Content <- tab.Editor        
        //tab.ChangesAreProbalySaved <- true // not neded. to not show star after putting in the code 
        tab.CodeAtLastSave <- code
        EventHandlers.setUpForTab tab
        XshdHighlighting.setFSharp tab.Editor
        makeTabHeader(tab)
        FileDialogs.updateHeader <- makeTabHeader
        //ModifyUI.markTabSaved(tab)// TODO this should not be needed
        
        Search.SearchPanel.Install(tab.Editor) |> ignore
        tab.FoldingManager <- Folding.FoldingManager.Install(tab.Editor.TextArea) 

        let i = UI.tabControl.Items.Add tab            
        if makeCurrent then 
            UI.tabControl.SelectedIndex <- i
            Tab.current <- Some tab
          
        //EditingServices.textChanged( EditingServices.TextChange.DocChanged , tab)//coverd by tab selection changed

        //if false then// TODO immideatly load refrences ?                
        //    let lines = code.Split( [|'\r'; '\n'|] )
        //    match Array.tryFindIndexBack (fun (ln:string) -> ln.StartsWith("#load ") || ln.StartsWith("#I ") || ln.StartsWith("#r ") ) lines with
        //    |None -> ()
        //    |Some i -> 
        //        lines
        //        |> Seq.take (i+1)
        //        |> String.concat "\r\n"
        //        |> Fsi.Evaluate
        //        |> Fsi.agent.Post
        //        // highlight too:
        //        let doc = tab.Editor.Document
        //        let st = doc.GetLineByOffset(0)
        //        let en = doc.GetLineByNumber(i)
        //        tab.Editor.Select(0,en.EndOffset)
        tab  
    