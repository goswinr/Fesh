namespace Fesh.Views

open System
open System.Collections.Generic
open System.Windows
open System.Windows.Controls
open System.Windows.Media
open Fesh
open Fesh.Model

/// The icons of the menu items, drawn on the same 16 x 16 grid and with the same line width as the icons in the completion list.
/// Most parts of an icon are in the text color, one detail shows the kind of command in color, see Theme.menu.
[<RequireQualifiedAccess>]
module MenuIcons =

    /// The color of a part of an icon
    type private Ink =
        | Base | Blue | Amber | Green | Red | Purple | Teal | Olive

    type private Part =
        /// Filled with the even-odd rule, so that an inner shape cuts a hole
        | Fill   of Ink * path:string
        | Stroke of Ink * path:string * width:float
        /// A dashed line with flat caps
        | Dash   of Ink * path:string * width:float

    let private line ink path = Stroke(ink, path, 1.3)
    let private wide ink width path = Stroke(ink, path, width)
    let private fill ink path = Fill(ink, path)
    let private dash ink path = Dash(ink, path, 1.3)

    // -------------------- shapes used in several icons --------------------

    let private circle (x:float) (y:float) (r:float) =
        sprintf "M%g %g A%g %g 0 1 1 %g %g A%g %g 0 1 1 %g %g Z" (x + r) y r r (x - r) y r r (x + r) y

    /// A small square, for a visible whitespace
    let private dot (x:float) (y:float) =
        sprintf "M%g %g h1.6 v1.6 h-1.6 Z" (x - 0.8) (y - 0.8)

    /// A lowercase a, with the stem at x
    let private lowerA (x:float) =
        sprintf "M%g 8.25 V13.5 M%g 10.875 A2.625 2.625 0 1 1 %g 10.875 A2.625 2.625 0 1 1 %g 10.875" x x (x - 5.25) x

    /// A gear in the lower right corner
    let private gear =
        let cx, cy = 12.25, 12.25
        let spokes =
            [ for i in 0 .. 7 do
                let a = float i * Math.PI / 4.0
                sprintf "M%g %g L%g %g" (cx + 2.4 * cos a) (cy + 2.4 * sin a) (cx + 3.4 * cos a) (cy + 3.4 * sin a) ]
        circle cx cy 1.6 + " " + String.concat " " spokes

    /// A page with a folded corner, open at the lower right for a badge
    let private page      = "M8.5 14.5 H3 V1.5 H8.5 L11.5 4.5 V8.5 M8.5 1.5 V4.5 H11.5"
    let private pageShort = "M6 14.5 H3 V1.5 H8.5 L11.5 4.5 V7 M8.5 1.5 V4.5 H11.5"
    let private disk      = "M2.5 2.5 H11 L13.5 5 V13.5 H2.5 Z M5 2.5 V6 H10.5 V2.5 M5 13.5 V9.5 H11 V13.5"
    let private diskSmall = "M1.5 1.5 H8.5 L10.5 3.5 V10.5 H1.5 Z M4 1.5 V4.5 H8 V1.5 M4 10.5 V7.5 H8 V10.5"
    let private diskTiny  = "M1 1.5 H6 L7.5 3 V8 H1 Z M2.6 1.5 H5.1 V3.6 H2.6 Z"
    let private diskBadge = "M9.75 9.75 H14 L15.5 11.25 V15.5 H9.75 Z M11.25 9.75 H13.25 V11.5 H11.25 Z"
    /// A folder, open at the lower right for a badge
    let private folder    = "M8 13.5 H1.5 V2.5 H5.5 L7 4 H14.5 V8"
    let private window    = "M1.5 2.5 H14.5 V13.5 H1.5 Z"
    let private logWindow = "M9 13.5 H1.5 V2.5 H14.5 V8.5 M1.5 5.25 H14.5"
    let private slashes   = "M5.5 2.5 L2.5 12.5 M9.5 2.5 L6.5 12.5"
    let private play      = "M10.5 4.5 L15.5 8 L10.5 11.5 Z"
    let private playBig   = "M8.5 5 L15.5 10 L8.5 15 Z"
    let private foldBox   = "M3.5 3.5 H12.5 V12.5 H3.5 Z"
    let private hammer    = "M7 4.5 L10 1.5 L14.5 6 L11.5 9 Z"
    let private handle    = "M9.25 6.75 L3 13"

    let private parts (icon:MenuIcon) : Part list =
        match icon with
        | MenuIcon.NoIcon                    -> []
        // files:
        | MenuIcon.NewFile                   -> [ line Base page; fill Amber "M12.5 9.25 L13.35 11.65 L15.75 12.5 L13.35 13.35 L12.5 15.75 L11.65 13.35 L9.25 12.5 L11.65 11.65 Z" ] // a page with a sparkle
        | MenuIcon.OpenFile                  -> [ line Base "M1.5 13 V2.5 H5.5 L7 4 H12.5 V6.5"; fill Amber "M1.5 13 L4 7 H15 L12.5 13 Z" ] // an open folder
        | MenuIcon.OpenExternal              -> [ line Base "M7 2.5 H2.5 V13.5 H13.5 V9"; line Blue "M9.5 2.5 H13.5 V6.5 M13.5 2.5 L7.5 8.5" ] // a box with an arrow out
        | MenuIcon.EditTemplate              -> [ line Base pageShort; line Amber "M14.6 8.4 L9.1 13.9 L7.2 14.4 L7.7 12.5 L13.2 7 Z" ] // a page with a pencil
        | MenuIcon.Folder                    -> [ line Amber "M1.5 13.5 V2.5 H5.5 L7 4 H14.5 V13.5 Z" ]
        | MenuIcon.Save                      -> [ line Blue disk ]
        | MenuIcon.SaveAs                    -> [ line Blue diskSmall; line Amber "M15.25 9.75 L10.75 14.25 L9 14.75 L9.5 13 L14 8.5 Z" ] // a disk with a pencil
        | MenuIcon.SaveIncrementing          -> [ line Blue diskSmall; line Amber "M11 12.25 L13 10.25 L15 12.25 M11 15 L13 13 L15 15" ] // a disk with two chevrons up
        | MenuIcon.SaveAll                   -> [ line Blue "M4.5 4.5 V1.5 H12 L14.5 4 V11.5 H11.5 M1.5 4.5 H9 L11.5 7 V14.5 H1.5 Z M4 4.5 V7.5 H8.5 V4.5 M4 14.5 V11 H9 V14.5" ] // two disks
        | MenuIcon.Export                    -> [ line Base "M8 14.5 H2.5 V3.5 H8 L10.5 6 V9 M8 3.5 V6 H10.5 M6 3.5 V1.5 H11 L13.5 4 V9"; line Blue "M9.75 12.5 H15 M12.75 10.25 L15 12.5 L12.75 14.75" ] // two pages with an arrow out
        | MenuIcon.Rename                    -> [ line Base "M5.5 4.5 H1.5 V11.5 H5.5 M10.5 4.5 H14.5 V11.5 H10.5"; line Blue "M8 2.5 V13.5 M6.25 2.5 H9.75 M6.25 13.5 H9.75" ] // a text box with a text cursor
        | MenuIcon.Close                     -> [ line Base page; wide Red 1.6 "M10.75 10.75 L14.75 14.75 M14.75 10.75 L10.75 14.75" ] // a page with an x
        | MenuIcon.Delete                    -> [ line Red "M2.5 4 H13.5 M6 4 V2.25 H10 V4 M4 4 L4.9 14 H11.1 L12 4 M6.6 6.5 L6.9 11.5 M9.4 6.5 L9.1 11.5" ] // a trash can
        | MenuIcon.SaveLog                   -> [ line Base (logWindow + " M4 8 H11 M4 10.75 H7.5"); fill Blue diskBadge ] // a window with text and a disk
        | MenuIcon.SaveLogSelection          -> [ line Base (logWindow + " M4 10.75 H7.5"); fill Blue "M3 6.5 H12 V9.25 H3 Z M4.25 7.3 H10.75 V8.45 H4.25 Z"; fill Blue diskBadge ] // with a selected line
        // edit:
        | MenuIcon.Copy                      -> [ line Base "M5.5 5.5 H13.5 V14.5 H5.5 Z M5.5 10.5 H2.5 V1.5 H10.5 V5.5" ] // two sheets
        | MenuIcon.Cut                       -> [ line Base (circle 4.5 12. 2. + " " + circle 11.5 12. 2. + " M5.9 10.6 L11 1.75 M10.1 10.6 L5 1.75") ] // scissors
        | MenuIcon.Paste                     -> [ line Base "M5.5 2.5 H3 V14.5 H13 V2.5 H10.5 M5.5 1.5 H10.5 V4 H5.5 Z M5.5 8 H10.5 M5.5 11 H9" ] // a clipboard
        | MenuIcon.Undo                      -> [ line Base "M5.5 3.5 L2.5 6.5 L5.5 9.5 M2.5 6.5 H10 A3.5 3.5 0 0 1 10 13.5 H6" ]
        | MenuIcon.Redo                      -> [ line Base "M10.5 3.5 L13.5 6.5 L10.5 9.5 M13.5 6.5 H6 A3.5 3.5 0 0 0 6 13.5 H10" ]
        | MenuIcon.Comment                   -> [ wide Olive 1.5 slashes; line Base "M13 9.5 V15 M10.25 12.25 H15.75" ] // // with a plus
        | MenuIcon.Uncomment                 -> [ wide Olive 1.5 slashes; line Red "M10.75 10.25 L14.75 14.25 M14.75 10.25 L10.75 14.25" ] // // with an x
        | MenuIcon.ToggleComment             -> [ wide Olive 1.5 slashes; line Base "M10.5 10 H15 M13.25 8.25 L15 10 L13.25 11.75 M15 13.75 H10.5 M12.25 12 L10.5 13.75 L12.25 15.5" ] // // with two arrows
        | MenuIcon.Find                      -> [ line Base (circle 6.5 6.5 4.5); wide Amber 2.2 "M10 10 L14.25 14.25" ] // a magnifier
        | MenuIcon.Replace                   -> [ line Base "M1.5 1.5 H7 V7 H1.5 Z"; line Amber "M4.25 8.75 V10 A2 2 0 0 0 6.25 12 H8 M6.75 10.5 L8.25 12 L6.75 13.5"; fill Amber "M9.75 9.25 H14.75 V14.25 H9.75 Z" ] // a box turned into another
        | MenuIcon.GoToDefinition            -> [ line Blue "M1.5 8 H9.5 M6.5 5 L9.5 8 L6.5 11"; line Base "M11.75 2.5 H14.5 V13.5 H11.75" ] // an arrow to a bracket
        | MenuIcon.DeleteLine                -> [ line Base "M1.5 3.5 H14.5 M1.5 8 H8.5 M1.5 12.5 H14.5"; line Red "M11 6.25 L14.5 9.75 M14.5 6.25 L11 9.75" ]
        | MenuIcon.DeletePreviousWord        -> [ line Base "M5.5 3.5 H14.5 V12.5 H5.5 L1.5 8 Z"; line Red "M8.25 6 L12.25 10 M12.25 6 L8.25 10" ] // a backspace key
        | MenuIcon.DeleteNextWord            -> [ line Base "M10.5 3.5 H1.5 V12.5 H10.5 L14.5 8 Z"; line Red "M3.75 6 L7.75 10 M7.75 6 L3.75 10" ]
        | MenuIcon.Uppercase                 -> [ line Base "M1.5 13.5 L5 2.5 L8.5 13.5 M2.7 9.75 H7.3"; line Teal "M12.5 13.5 V3 M10.25 5.25 L12.5 3 L14.75 5.25" ] // A with an arrow up
        | MenuIcon.Lowercase                 -> [ line Base (lowerA 7.5); line Teal "M12.5 3 V13.5 M10.25 11.25 L12.5 13.5 L14.75 11.25" ] // a with an arrow down
        | MenuIcon.Titlecase                 -> [ line Teal "M1 13.5 L4.25 2.5 L7.5 13.5 M2.1 9.75 H6.4"; line Base (lowerA 14.5) ] // Aa
        | MenuIcon.TrailingWhitespace        -> [ line Base "M1.5 4 H8 M1.5 8 H5.5 M1.5 12 H9"; fill Red (String.concat " " [ dot 10.75 4.; dot 13.75 4.; dot 7.75 8.; dot 10.75 8.; dot 13.75 8.; dot 11.5 12.; dot 14.25 12. ]) ]
        | MenuIcon.ToggleBool                -> [ line Base "M5 4 H11 A4 4 0 0 1 11 12 H5 A4 4 0 0 1 5 4 Z"; fill Teal (circle 11. 8. 2.) ] // a switch
        | MenuIcon.AlignCode                 -> [ line Base "M3.5 4 H6.5 M1.5 8 H6.5 M4.5 12 H6.5 M10.5 4 H14.5 M10.5 8 H12.5 M10.5 12 H14"; line Teal "M8.5 1.5 V14.5" ]
        // select:
        | MenuIcon.SelectLine                -> [ line Base "M1.5 3 H11 M1.5 13 H9"; fill Blue "M1 5.75 H15 V10.25 H1 Z M3 7.4 H12.5 V8.6 H3 Z" ]
        | MenuIcon.SwapLineUp                -> [ line Base "M1.5 5 H9"; line Teal "M1.5 11 H9 M12.5 13.5 V2.5 M10.25 4.75 L12.5 2.5 L14.75 4.75" ]
        | MenuIcon.SwapLineDown              -> [ line Teal "M1.5 5 H9 M12.5 2.5 V13.5 M10.25 11.25 L12.5 13.5 L14.75 11.25"; line Base "M1.5 11 H9" ]
        | MenuIcon.SwapWordLeft              -> [ line Base "M1.5 8 H6.5 V12.5 H1.5 Z"; fill Blue "M9.5 8 H14.5 V12.5 H9.5 Z"; line Teal "M12.5 3.5 H3.5 M5.5 1.5 L3.5 3.5 L5.5 5.5" ]
        | MenuIcon.SwapWordRight             -> [ fill Blue "M1.5 8 H6.5 V12.5 H1.5 Z"; line Base "M9.5 8 H14.5 V12.5 H9.5 Z"; line Teal "M3.5 3.5 H12.5 M10.5 1.5 L12.5 3.5 L10.5 5.5" ]
        // box selection: a dashed box with chevrons
        | MenuIcon.BoxSelectLeftByCharacter  -> [ dash Base "M8.5 3.5 H14.5 V12.5 H8.5 Z"; wide Blue 1.5 "M5.5 5 L2.5 8 L5.5 11" ]
        | MenuIcon.BoxSelectRightByCharacter -> [ dash Base "M1.5 3.5 H7.5 V12.5 H1.5 Z"; wide Blue 1.5 "M10.5 5 L13.5 8 L10.5 11" ]
        | MenuIcon.BoxSelectLeftByWord       -> [ dash Base "M9.5 3.5 H14.5 V12.5 H9.5 Z"; wide Blue 1.5 "M4.5 5 L1.5 8 L4.5 11 M7.75 5 L4.75 8 L7.75 11" ]
        | MenuIcon.BoxSelectRightByWord      -> [ dash Base "M1.5 3.5 H6.5 V12.5 H1.5 Z"; wide Blue 1.5 "M11.5 5 L14.5 8 L11.5 11 M8.25 5 L11.25 8 L8.25 11" ]
        | MenuIcon.BoxSelectUpByLine         -> [ dash Base "M3.5 8.5 H12.5 V14.5 H3.5 Z"; wide Blue 1.5 "M5 5.5 L8 2.5 L11 5.5" ]
        | MenuIcon.BoxSelectDownByLine       -> [ dash Base "M3.5 1.5 H12.5 V7.5 H3.5 Z"; wide Blue 1.5 "M5 10.5 L8 13.5 L11 10.5" ]
        | MenuIcon.BoxSelectToLineStart      -> [ dash Base "M9.5 3.5 H14.5 V12.5 H9.5 Z"; wide Blue 1.5 "M1.5 3 V13 M6.75 5 L3.75 8 L6.75 11" ]
        | MenuIcon.BoxSelectToLineEnd        -> [ dash Base "M1.5 3.5 H6.5 V12.5 H1.5 Z"; wide Blue 1.5 "M14.5 3 V13 M9.25 5 L12.25 8 L9.25 11" ]
        // FSI: lines of code and a green play triangle
        | MenuIcon.EvaluateLines             -> [ line Base "M1.5 3.5 H8.5 M1.5 12.5 H8.5"; wide Green 1.8 "M1.5 8 H8.5"; fill Green play ]
        | MenuIcon.EvaluateSelection         -> [ line Base "M1.5 3.5 H8.5 M1.5 12.5 H8.5"; fill Blue "M1 6 H9 V10 H1 Z M2.5 7.4 H7.5 V8.6 H2.5 Z"; fill Green play ]
        | MenuIcon.MarkEvaluated             -> [ line Base "M1.5 3.5 H9 M1.5 7.5 H6.5 M1.5 11.5 H5"; wide Green 1.6 "M7.5 10.25 L10.25 13 L15 6.25" ] // lines with a check mark
        | MenuIcon.EvaluateTillLine          -> [ line Green "M1.5 2.5 H8.5 M1.5 6 H8.5 M1.5 9.5 H8.5"; dash Base "M1.5 13 H8.5"; fill Green play ] // the lines after the current line are dashed
        | MenuIcon.ContinueEvaluation        -> [ dash Base "M1.5 2.5 H8.5 M1.5 6 H8.5"; line Green "M1.5 9.5 H8.5 M1.5 13 H8.5"; fill Green play ] // the lines evaluated before are dashed
        | MenuIcon.EvaluateAll               -> [ fill Green "M4 2 L14 8 L4 14 Z" ]
        | MenuIcon.SaveEvaluateAll           -> [ fill Blue diskTiny; fill Green playBig ]
        | MenuIcon.ClearSaveEvaluateAll      -> [ fill Red "M1.15 5.2 L2.95 3.4 L5.95 6.4 L4.15 8.2 Z"; wide Base 1.1 "M2.95 3.4 L4.75 1.6 L7.75 4.6 L5.95 6.4"; fill Green playBig ] // an eraser and a play triangle
        | MenuIcon.GoToError                 -> [ line Base "M1.5 4 H10 M1.5 12.5 H7.5"; wide Red 1.2 "M1.5 8.5 L2.75 7.25 L4 8.5 L5.25 7.25 L6.5 8.5 L7.75 7.25 L9 8.5 L10.25 7.25"; line Base "M13.25 2.5 V13.5 M11.25 11.5 L13.25 13.5 L15.25 11.5" ] // a squiggly line and an arrow down
        | MenuIcon.ClearLog                  -> [ fill Red "M1.5 9.5 L4.5 6.5 L9.5 11.5 L6.5 14.5 Z"; line Base "M4.5 6.5 L7.5 3.5 L12.5 8.5 L9.5 11.5 M9.5 14.5 H14.5" ] // an eraser
        | MenuIcon.Cancel                    -> [ fill Red "M5 3.5 H11 A1.5 1.5 0 0 1 12.5 5 V11 A1.5 1.5 0 0 1 11 12.5 H5 A1.5 1.5 0 0 1 3.5 11 V5 A1.5 1.5 0 0 1 5 3.5 Z" ] // a stop square
        | MenuIcon.Reset                     -> [ wide Amber 1.5 "M13 8 A5 5 0 1 1 11.54 4.46"; fill Amber "M12.6 5.6 L11.85 2 L9 4.85 Z" ] // a circular arrow
        | MenuIcon.ResetArguments            -> [ line Base "M1.5 4 H14.5 M1.5 8 H14.5 M1.5 12 H14.5"; fill Amber (circle 10. 4. 1.75 + " " + circle 5. 8. 1.75 + " " + circle 11.5 12. 1.75) ] // sliders
        | MenuIcon.ToggleSync                -> [ line Teal "M2 5 H13 M10.5 2.5 L13 5 L10.5 7.5"; line Base "M14 11 H3 M5.5 8.5 L3 11 L5.5 13.5" ] // two arrows
        | MenuIcon.CompileSdk                -> [ fill Amber hammer; wide Base 2.0 handle; line Base "M10 10.75 L12 12.5 L10 14.25 M12.75 14.5 H15.5" ] // a hammer with a >_ prompt
        | MenuIcon.CompileMSBuild            -> [ fill Amber hammer; wide Base 2.0 handle; line Base "M11.25 10.75 L9.5 12.5 L11.25 14.25 M13.75 10.75 L15.5 12.5 L13.75 14.25" ] // a hammer with <>
        // view:
        | MenuIcon.ToggleSplit               -> [ line Base window; fill Purple "M9 4 H13 V12 H9 Z" ]
        | MenuIcon.MaximizeLog               -> [ line Base window; line Purple "M5 11 L11 5 M7.75 5 H11 V8.25 M5 7.75 V11 H8.25" ]
        | MenuIcon.LineWrap                  -> [ line Base "M1.5 3.5 H14.5 M1.5 12.5 H5"; line Purple "M1.5 8 H12 A2.25 2.25 0 0 1 12 12.5 H8.5 M10 11 L8.5 12.5 L10 14" ]
        | MenuIcon.FontBigger                -> [ line Base "M1.5 14 L5.5 2.5 L9.5 14 M2.9 10 H8.1"; line Purple "M12.75 2.5 V8 M10 5.25 H15.5" ]
        | MenuIcon.FontSmaller               -> [ line Base "M2 14 L5 6 L8 14 M3.03 11.25 H6.97"; line Purple "M10 5.25 H15.5" ]
        | MenuIcon.ToggleTheme               -> [ line Base (circle 8. 8. 6.); fill Base "M8 2 A6 6 0 0 1 8 14 Z" ] // a half filled circle
        | MenuIcon.Collapse                  -> [ line Base foldBox; line Purple "M5.75 8 H10.25" ] // like the folding marker in the editor
        | MenuIcon.Expand                    -> [ line Base foldBox; line Purple "M5.75 8 H10.25 M8 5.75 V10.25" ]
        | MenuIcon.CollapseAll               -> [ line Base "M2 8 H14"; line Purple "M4.5 1.5 L8 5 L11.5 1.5 M4.5 14.5 L8 11 L11.5 14.5" ]
        | MenuIcon.CollapsePrimary           -> [ line Base "M1.5 1.5 H6.5 V6.5 H1.5 Z M1.5 9.5 H6.5 V14.5 H1.5 Z M9 4 H14.5 M9 12 H14.5"; line Purple "M2.75 4 H5.25 M2.75 12 H5.25" ]
        | MenuIcon.ExpandAll                 -> [ line Base "M2 8 H14"; line Purple "M4.5 5 L8 1.5 L11.5 5 M4.5 11 L8 14.5 L11.5 11" ]
        | MenuIcon.Pin                       -> [ line Purple "M9.5 1.5 L14.5 6.5 M10.5 2.5 L7 6 L3.5 6.5 L9.5 12.5 L10 9 L13.5 5.5"; line Base "M6.5 9.5 L2 14" ]
        // about:
        | MenuIcon.Help                      -> [ line Blue (circle 8. 8. 6.5); line Base "M6 6.25 A2 2 0 1 1 9.1 7.9 C8.35 8.4 8 8.9 8 9.75"; fill Base (circle 8. 11.9 0.85) ]
        | MenuIcon.SettingsFolder            -> [ line Amber folder; line Base gear ]
        | MenuIcon.AppFolder                 -> [ line Amber folder; line Base "M9.25 9.5 H15.25 V15 H9.25 Z M9.25 11.25 H15.25" ]
        | MenuIcon.SyntaxColors              -> // a palette
            [ line Base "M8 1.5 A6.5 6.5 0 0 0 8 14.5 C9.3 14.5 9.7 13.5 9.1 12.6 C8.5 11.7 9 10.5 10.3 10.5 H12 A2.5 2.5 0 0 0 14.5 8 A6.5 6.5 0 0 0 8 1.5 Z"
              fill Red   (circle 4.6 7.4 1.15)
              fill Amber (circle 6.3 4.4 1.15)
              fill Green (circle 9.7 4.4 1.15)
              fill Blue  (circle 11.6 7.2 1.15) ]

    // -------------------- drawing --------------------

    let private colorOf (ink:Ink) : Color =
        let c = Theme.menu
        let b =
            match ink with
            | Base   -> c.text
            | Blue   -> c.blue
            | Amber  -> c.amber
            | Green  -> c.green
            | Red    -> c.red
            | Purple -> c.purple
            | Teal   -> c.teal
            | Olive  -> c.olive
        b.Color

    /// One brush per ink, shared by all icons.
    /// They are not frozen, so that all icons change color when the theme changes.
    let private brushes = Dictionary<Ink, SolidColorBrush>()

    let private brush (ink:Ink) : SolidColorBrush =
        match brushes.TryGetValue ink with
        | true, b -> b
        | _ ->
            if brushes.Count = 0 then
                Theme.Changed.Add (fun () -> for KeyValue(i, b) in brushes do b.Color <- colorOf i)
            let b = SolidColorBrush(colorOf ink)
            brushes.[ink] <- b
            b

    let private make (ps:Part list) : ImageSource =
        let square = RectangleGeometry(Rect(0., 0., 16., 16.))
        square.Freeze()
        let g = DrawingGroup()
        // a transparent square, so that all icons have the full 16 x 16 bounds, and the Image does not scale them up.
        // The clip keeps round caps that reach over the edge from making the bounds bigger, so that the Image does not scale them down.
        g.Children.Add(GeometryDrawing(Brushes.Transparent, null, square))
        g.ClipGeometry <- square
        let geometry (path:string) =
            let geo = Geometry.Parse path
            geo.Freeze()
            geo
        for p in ps do
            match p with
            | Fill (ink, path) ->
                g.Children.Add(GeometryDrawing(brush ink, null, geometry path))
            | Stroke (ink, path, width) ->
                let pen = Pen(brush ink, width, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round)
                g.Children.Add(GeometryDrawing(null, pen, geometry path))
            | Dash (ink, path, width) ->
                // WPF measures dashes in multiples of the pen width. The dashes are 2 long with gaps of 1.25.
                let pen = Pen(brush ink, width, LineJoin = PenLineJoin.Round, DashCap = PenLineCap.Flat, DashStyle = DashStyle([| 2.0 / width; 1.25 / width |], 0.0))
                g.Children.Add(GeometryDrawing(null, pen, geometry path))
        DrawingImage(g) :> ImageSource

    /// The icons, shared by all menu items with the same icon
    let private cache = Dictionary<MenuIcon, ImageSource>()

    /// The icon as an ImageSource, shared by all menu items with this icon. Null for MenuIcon.NoIcon.
    /// It is not frozen and follows the theme. Call from the UI thread.
    let get (icon:MenuIcon) : ImageSource =
        match cache.TryGetValue icon with
        | true, img -> img
        | _ ->
            let img =
                match parts icon with
                | [] -> null
                | ps -> make ps
            cache.[icon] <- img
            img

    /// A 16 x 16 Image for MenuItem.Icon, or null for MenuIcon.NoIcon.
    /// The icon fades when the menu item is disabled. Call from the UI thread.
    let image (icon:MenuIcon) : Image =
        match get icon with
        | null -> null
        | src ->
            let img = Image(Source = src, Width = 16., Height = 16.)
            img.IsEnabledChanged.Add (fun _ -> img.Opacity <- if img.IsEnabled then 1.0 else 0.4)
            img
