namespace Seff.Editor

open System
open System.Windows
open System.Windows.Input

open Seff.Model


module Keys =
    type CtrlKey = Ctrl | Alt | Shift
    
    let inline isUp k = 
        match k with 
        | Ctrl  ->  Keyboard.IsKeyUp Key.LeftCtrl  && Keyboard.IsKeyUp Key.RightCtrl
        | Alt   ->  Keyboard.IsKeyUp Key.LeftAlt   && Keyboard.IsKeyUp Key.RightAlt
        | Shift ->  Keyboard.IsKeyUp Key.LeftShift && Keyboard.IsKeyUp Key.RightShift
    
    let inline isDown k = 
        match k with 
        | Ctrl  ->  Keyboard.IsKeyDown Key.LeftCtrl  || Keyboard.IsKeyDown Key.RightCtrl
        | Alt   ->  Keyboard.IsKeyDown Key.LeftAlt   || Keyboard.IsKeyDown Key.RightAlt
        | Shift ->  Keyboard.IsKeyDown Key.LeftShift || Keyboard.IsKeyDown Key.RightShift

    /// because Alt key actually returns Key.System 
    let inline realKey(e:KeyEventArgs) =
        match e.Key with //https://stackoverflow.com/questions/39696219/how-can-i-handle-a-customizable-hotkey-setting
        | Key.System             -> e.SystemKey
        | Key.ImeProcessed       -> e.ImeProcessedKey
        | Key.DeadCharProcessed  -> e.DeadCharProcessedKey
        | k                      -> k


module KeyboardShortcuts =
    open Keys    
    open Selection

    let previewKeyDown (ed:IEditor, ke: Input.KeyEventArgs) =  
        //if not ed.IsComplWinOpen then  
         match realKey ke  with  
         
         |Input.Key.Back ->  
             /// TODO check for modifier keys like Alt or Ctrl ?
             match getSelType(ed.AvaEdit.TextArea) with 
             | NoSel ->     CursorBehaviour.backspace4Chars(ed,ke)
             | RectSel ->   RectangleSelection.backspaceKey(ed) ; ke.Handled <- true 
             | RegSel  ->   ()        
    
         |Input.Key.Delete ->                
             /// TODO check for modifier keys like Alt or Ctrl ?
             match getSelType(ed.AvaEdit.TextArea) with 
             | NoSel  ->   CursorBehaviour.deleteTillNonWhite(ed,ke)                
             | RectSel ->  RectangleSelection.deleteKey(ed) ; ke.Handled <- true 
             | RegSel ->   ()

         | Input.Key.Enter | Input.Key.Return ->
             if isUp Ctrl // if alt or ctrl is down this means sending to fsi ...         
             && isUp Alt 
             && isUp Shift then CursorBehaviour.addIndentation(ed,ke)  // add indent after do, for , ->, =  
     
         | Input.Key.Down -> 
             if isDown Ctrl && isUp Shift then
                 if isDown Alt then
                     RectangleSelection.expandDown(ed) 
                 else  
                     // also use Ctrl key for swaping since Alt key does not work in rhino, 
                     // swaping with alt+up is set up in commands.fs via key gesteures                                        
                     SwapLines.swapLinesDown(ed) // on Ctrl + Down
                     ke.Handled <- true
         
         | Input.Key.Up -> 
             if isDown Ctrl && isUp Shift then
                 if isDown Alt then
                     RectangleSelection.expandUp(ed)
                 else 
                     SwapLines.swapLinesUp(ed) // on Ctrl + Up
                     ke.Handled <- true
         
         | Input.Key.Left -> 
             if isDown Ctrl && isUp Shift then                
                 match getSelType(ed.AvaEdit.TextArea) with 
                 | RegSel ->   if SwapWords.left(ed.AvaEdit) then  ke.Handled <- true
                 | NoSel  | RectSel ->  ()
         
         | Input.Key.Right -> 
             if isDown Ctrl && isUp Shift then                
                 match getSelType(ed.AvaEdit.TextArea) with 
                 | RegSel ->   if SwapWords.right(ed.AvaEdit) then  ke.Handled <- true
                 | NoSel  | RectSel ->  ()

         | _ -> ()
 

