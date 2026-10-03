namespace Fesh.Editor

open System
open Avalonia
open Avalonia.Input
open AvaloniaEdit

open Fesh.Model

module Keys =

    type AltKeyCombo =
        | AltUp
        | AltRight
        | AltDown
        | AltLeft



module KeyboardShortcuts =
    open Keys
    open Selection

    // For alt and arrow keys only since they need a special keyboard hook to not get hijacked in Rhino
    let altKeyCombo(aKey:AltKeyCombo) =
        // see bug https://discourse.mcneel.com/t/using-alt-up-key-in-plugin-does-not-work/105740/3
        match IEditor.current with
        | None -> () //never happens ?
        | Some ed ->
            if ed.AvaEdit.TextArea.IsFocused then // to skip if search panel or Log is focused
                match aKey with
                | AltUp    -> SwapLines.swapLinesUp(ed)
                | AltDown  -> SwapLines.swapLinesDown(ed)
                | AltRight -> SwapWords.right(ed.AvaEdit)  |> ignore
                | AltLeft  -> SwapWords.left(ed.AvaEdit)   |> ignore

    /// gets attached to each editor instance. via avaEdit.PreviewKeyDown.Add
    /// except for Alt and arrow keys that are handled via KeyboardNative
    let previewKeyDown (ied:IEditor, ke:KeyEventArgs) =
        let ed = ied.AvaEdit
        let ta = ed.TextArea
        let sel = getSelType(ta)
        if ta.IsFocused then // to skip if search panel is focused

            // match realKey ke  with
            match ke.Key with
            |Key.Back -> // also do if completion window is open
                // TODO check for modifier keys like Alt or Ctrl ?
                match sel with
                | NoSel   ->   CursorBehavior.backspace4Chars(ed,ke)
                | RectSel ->   RectangleSelection.backspaceKey(ed) ; ke.Handled <- true
                | RegSel  ->   ()

            |Key.Delete -> // also do if completion window is open
                // TODO check for modifier keys like Alt or Ctrl ?
                match sel with
                | NoSel   ->  CursorBehavior.deleteTillNonWhite(ed,ke)
                | RectSel ->  RectangleSelection.deleteKey(ed) ; ke.Handled <- true
                | RegSel  ->  ()

            | Key.Enter | Key.Return -> // if alt or ctrl is down this means sending to fsi ...
                if ke.KeyModifiers = KeyModifiers.None  && not ied.IsComplWinOpen then
                    CursorBehavior.addFSharpIndentation(ed,ke)  // add indent after do, for , ->, =

            (*
            These are handled in: let altKeyCombo(aKey:AltKeyCombo)
            Just because Rhino3D does not allow Alt + Up and Alt + Down to be used as shortcuts like this:

            | Input.Key.Down ->
                if isDown Ctrl && isUp Shift then
                    if isDown Alt then
                        RectangleSelection.expandDown(ed) // on Ctrl + Alt + down
                    else
                        // also use Ctrl key for swapping since Alt key does not work in rhino,
                        // swapping with alt+up is set up in commands.fs via key gestures
                        //SwapLines.swapLinesDown(ed) // on Ctrl + Down
                        //ke.Handled <- true
                        ()

            | Input.Key.Up ->
                if isDown Ctrl && isUp Shift then
                    if isDown Alt then
                        RectangleSelection.expandUp(ed) // on Ctrl + Alt + Up
                    else
                        //SwapLines.swapLinesUp(ed) // on Ctrl + Up
                        //ke.Handled <- true
                        ()

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
            *)

            | _ -> ()

