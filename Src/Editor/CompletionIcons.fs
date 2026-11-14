namespace Fesh.Editor

open System
open System.Collections.Generic
open System.Windows
open System.Windows.Media
open FSharp.Compiler.EditorServices
open Fesh

/// The kind of an item in the completion list.
/// It sets the icon of the item and the color of its text.
[<RequireQualifiedAccess>]
type CompletionKind =
    // members:
    | Method
    | ExtensionMethod
    /// let bound functions
    | Function
    | Property
    /// .NET fields
    | Field
    | RecordField
    | Event
    // types:
    /// classes and records
    | Class
    | Struct
    | Interface
    | Union
    /// union cases, enum values and active pattern cases
    | UnionCase
    | Enum
    | Exception
    | Delegate
    | TypeAbbreviation
    | TypeParameter
    // modules and values:
    | Module
    | Namespace
    | Value
    | Literal
    // added by Fesh, not from the type checker:
    | Keyword
    /// __SOURCE_DIRECTORY__, __SOURCE_FILE__ and __LINE__
    | SourceIdentifier
    /// #if, #else, #endif
    | CompilerDirective
    /// #r, #I, #load
    | ReferenceDirective
    | NugetPackage
    /// Shown without icon and color, e.g. for FSharpGlyph.Error
    | Unknown

[<RequireQualifiedAccess>]
module CompletionKind =

    /// The kind of an item from the type checker
    let ofDeclaration (it:DeclarationListItem) =
        match it.Glyph with
        | FSharpGlyph.Method
        | FSharpGlyph.OverridenMethod ->
            match it.Kind with
            | CompletionItemKind.Method _ -> CompletionKind.Method  // methods of .NET and F# types
            | _                           -> CompletionKind.Function // let bound functions have the glyph of a method too
        | FSharpGlyph.ExtensionMethod     -> CompletionKind.ExtensionMethod
        | FSharpGlyph.Property            -> CompletionKind.Property
        | FSharpGlyph.Field ->
            match it.Kind with
            | CompletionItemKind.Property -> CompletionKind.RecordField // record fields have the glyph of a field
            | _                           -> CompletionKind.Field
        | FSharpGlyph.Event               -> CompletionKind.Event
        | FSharpGlyph.Class
        | FSharpGlyph.Type                -> CompletionKind.Class
        | FSharpGlyph.Struct              -> CompletionKind.Struct
        | FSharpGlyph.Interface           -> CompletionKind.Interface
        | FSharpGlyph.Union               -> CompletionKind.Union
        | FSharpGlyph.EnumMember          -> CompletionKind.UnionCase
        | FSharpGlyph.Enum                -> CompletionKind.Enum
        | FSharpGlyph.Exception           -> CompletionKind.Exception
        | FSharpGlyph.Delegate            -> CompletionKind.Delegate
        | FSharpGlyph.Typedef             -> CompletionKind.TypeAbbreviation
        | FSharpGlyph.TypeParameter       -> CompletionKind.TypeParameter
        | FSharpGlyph.Module              -> CompletionKind.Module
        | FSharpGlyph.NameSpace           -> CompletionKind.Namespace
        | FSharpGlyph.Variable            -> CompletionKind.Value
        | FSharpGlyph.Constant            -> CompletionKind.Literal
        | FSharpGlyph.Error               -> CompletionKind.Unknown

    /// The kind of the keywords and directives that Fesh adds to the completion list
    let ofKeyword (text:string) =
        let starts (s:string) = text.StartsWith(s, StringComparison.Ordinal)
        if   starts "#r" || starts "#I" || starts "#load" then CompletionKind.ReferenceDirective
        elif starts "#"                                  then CompletionKind.CompilerDirective
        elif starts "__"                                 then CompletionKind.SourceIdentifier
        else                                                  CompletionKind.Keyword

    /// The name of the color in SyntaxHighlightingFSharp.xshd, the same color as in the editor.
    let colorName (kind:CompletionKind) =
        match kind with
        | CompletionKind.Method             -> "Semantic.Method"
        | CompletionKind.ExtensionMethod    -> "Semantic.ExtensionMethod"
        | CompletionKind.Function           -> "Semantic.Function"
        | CompletionKind.Property           -> "Semantic.Property"
        | CompletionKind.Field              -> "Semantic.Field"
        | CompletionKind.RecordField        -> "Semantic.RecordField"
        | CompletionKind.Event              -> "Semantic.Event"
        | CompletionKind.Class              -> "Semantic.ReferenceType"
        | CompletionKind.Struct             -> "Semantic.ValueType"
        | CompletionKind.Interface          -> "Semantic.Interface"
        | CompletionKind.Union              -> "Semantic.ReferenceType"
        | CompletionKind.UnionCase          -> "Semantic.UnionCase"
        | CompletionKind.Enum               -> "Semantic.Enumeration"
        | CompletionKind.Exception          -> "Semantic.Exception"
        | CompletionKind.Delegate           -> "Semantic.Delegate"
        | CompletionKind.TypeAbbreviation   -> "Semantic.Type"
        | CompletionKind.TypeParameter      -> "Semantic.TypeArgument"
        | CompletionKind.Module             -> "Semantic.Module"
        | CompletionKind.Namespace          -> "Semantic.Namespace"
        | CompletionKind.Value              -> "Semantic.Value"
        | CompletionKind.Literal            -> "Semantic.Literal"
        | CompletionKind.Keyword
        | CompletionKind.SourceIdentifier   -> "Keywords"
        | CompletionKind.CompilerDirective  -> "CompilerDirectives"
        | CompletionKind.ReferenceDirective -> "LoadDirectives"
        | CompletionKind.NugetPackage       -> "LoadNuget"
        | CompletionKind.Unknown            -> ""

    /// The color and font style for the text of this kind, from SyntaxHighlightingFSharp.xshd.
    /// ValueNone for CompletionKind.Unknown, or if the color is missing in the xshd file.
    let textStyle (kind:CompletionKind) =
        SemanticColors.textStyle (colorName kind)


/// The icons in the completion list.
/// Like the menu icons, most parts of an icon are gray and one detail is in color.
/// The color shows the family of the kind, see Family. It does not depend on the color of the text next to the icon,
/// so that the icon stays apart from the text and says something that the color of the text doesn't.
[<RequireQualifiedAccess>]
module CompletionIcons =

    /// The family of a kind sets the color of the detail of its icon
    type private Family =
        /// Methods and functions, purple
        | Call
        /// Properties, fields, values and union cases, blue
        | Data
        /// Types and events, amber
        | Type
        /// Exceptions, red
        | Error
        /// Modules, namespaces, keywords, directives and packages, all gray
        | Plain

    let private familyOf (kind:CompletionKind) =
        match kind with
        | CompletionKind.Method
        | CompletionKind.ExtensionMethod
        | CompletionKind.Function           -> Call
        | CompletionKind.Property
        | CompletionKind.Field
        | CompletionKind.RecordField
        | CompletionKind.Value
        | CompletionKind.Literal
        | CompletionKind.UnionCase          -> Data
        | CompletionKind.Event
        | CompletionKind.Class
        | CompletionKind.Struct
        | CompletionKind.Interface
        | CompletionKind.Union
        | CompletionKind.Enum
        | CompletionKind.Delegate
        | CompletionKind.TypeAbbreviation
        | CompletionKind.TypeParameter      -> Type
        | CompletionKind.Exception          -> Error
        | CompletionKind.Module
        | CompletionKind.Namespace
        | CompletionKind.Keyword
        | CompletionKind.SourceIdentifier
        | CompletionKind.CompilerDirective
        | CompletionKind.ReferenceDirective
        | CompletionKind.NugetPackage
        | CompletionKind.Unknown            -> Plain

    /// The same colors as the details of the menu icons
    let private detailColor (family:Family) : SolidColorBrush =
        match family with
        | Call  -> Theme.menu.purple
        | Data  -> Theme.menu.blue
        | Type  -> Theme.menu.amber
        | Error -> Theme.menu.red
        | Plain -> Theme.completion.iconBody

    /// The gray body or the colored detail of an icon
    type private Ink =
        | Body
        | Detail

    /// The icons are drawn on a 16 x 16 grid, the size of the Image in the item template of AvalonEditB.
    type private Part =
        | Fill       of Ink * path:string
        | Stroke     of Ink * path:string * width:float
        /// Filled and outlined in the same color, e.g. the lid of a cube
        | FillStroke of Ink * path:string * width:float

    let private body   (path:string) = Stroke(Body, path, 1.3)
    let private detail (path:string) = Stroke(Detail, path, 1.3)

    let private braces =
        "M5.5 2 C3.9 2 3.8 2.9 3.8 4.4 V6.4 C3.8 7.4 3.3 8 2.25 8 C3.3 8 3.8 8.6 3.8 9.6 V11.6 C3.8 13.1 3.9 14 5.5 14 " +
        "M10.5 2 C12.1 2 12.2 2.9 12.2 4.4 V6.4 C12.2 7.4 12.7 8 13.75 8 C12.7 8 12.2 8.6 12.2 9.6 V11.6 C12.2 13.1 12.1 14 10.5 14"

    let private brackets = "M5 2.5 H2.5 V13.5 H5 M11 2.5 H13.5 V13.5 H11"

    let private field = [ body "M2 5.75 H11 V12.5 H2 Z"; Fill(Detail, "M2 5.75 L4.5 3.25 H13.5 L11 5.75 Z M11 5.75 L13.5 3.25 V10 L11 12.5 Z") ] // a brick with a colored top and side

    let private literal = [ body brackets; Stroke(Detail, "M5.75 6.5 H10.25 M5.75 9.5 H10.25", 1.5) ] // [=]

    let private parts (kind:CompletionKind) : Part list =
        match kind with
        | CompletionKind.Method             -> [ FillStroke(Detail, "M8 1.75 L13.75 4.75 L8 7.75 L2.25 4.75 Z", 1.3); body "M2.25 4.75 V11.25 L8 14.25 L13.75 11.25 V4.75 M8 7.75 V14.25" ] // a cube with a colored lid
        | CompletionKind.ExtensionMethod    -> [ FillStroke(Detail, "M6.5 1.5 L11 3.75 L6.5 6 L2 3.75 Z", 1.3); body "M2 3.75 V8.75 L6.5 11 L11 8.75 V3.75 M6.5 6 V11"; detail "M12.75 10 V15 M10.25 12.5 H15.25" ] // a cube with a plus
        | CompletionKind.Function           -> [ Stroke(Body, "M11.6 2.7 C10.7 1.8 8.7 1.8 8.1 4 L6.2 12.3 C5.7 14.4 4 14.5 3 13.6", 1.5); Stroke(Detail, "M4.8 6.6 H10.8", 1.7) ] // an ƒ with a colored crossbar
        | CompletionKind.Property           -> [ Stroke(Detail, "M13.55 4.88 A3 3 0 1 1 11.12 2.45", 1.9); Stroke(Body, "M8.4 7.6 L3.2 12.8", 2.3) ] // a wrench with a colored head
        | CompletionKind.Field
        | CompletionKind.RecordField        -> field
        | CompletionKind.Event              -> [ Fill(Detail, "M9.6 1.2 L3.4 9 H7.6 L6.4 14.8 L12.6 7 H8.4 Z") ] // a lightning bolt, all colored
        | CompletionKind.Class              -> [ body "M1.75 2.75 H6.75 V7.75 H1.75 Z M6.75 5.25 H8.5 M8.5 4 V12 H10 M8.5 4 H10"; Fill(Detail, "M10 2 H14 V6 H10 Z M10 10 H14 V14 H10 Z") ] // three linked boxes
        | CompletionKind.Struct             -> [ Fill(Detail, "M2.5 2.5 H8 V8 H2.5 Z"); body "M2.5 2.5 H13.5 V13.5 H2.5 Z M8 2.5 V13.5 M2.5 8 H13.5" ] // a grid with a colored cell
        | CompletionKind.Interface          -> [ body "M1.5 8 H7.5"; Stroke(Detail, "M14 8 A3.25 3.25 0 1 1 7.5 8 A3.25 3.25 0 1 1 14 8 Z", 1.5) ] // a lollipop
        | CompletionKind.Union              -> [ body "M1.75 8 H5 C7.75 8 7.5 3.5 10.5 3.5 M5 8 C7.75 8 7.5 12.5 10.5 12.5"; Fill(Detail, "M13 1.4 L15.1 3.5 L13 5.6 L10.9 3.5 Z M13 10.4 L15.1 12.5 L13 14.6 L10.9 12.5 Z") ] // a fork to two cases
        | CompletionKind.UnionCase          -> [ Fill(Detail, "M8 2.75 L13.25 8 L8 13.25 L2.75 8 Z") ] // a diamond, all colored
        | CompletionKind.Enum               -> [ Fill(Detail, "M4 1.8 L6.6 4.4 L4 7 L1.4 4.4 Z M4 9 L6.6 11.6 L4 14.2 L1.4 11.6 Z"); body "M9 4.4 H14.5 M9 11.6 H14.5" ] // a list of diamonds
        | CompletionKind.Exception          -> [ Stroke(Detail, "M8 2 L14.5 13.5 H1.5 Z", 1.5); body "M8 6.2 V9.4"; Fill(Body, "M8.9 11.6 A0.9 0.9 0 1 1 7.1 11.6 A0.9 0.9 0 1 1 8.9 11.6 Z") ] // a warning triangle
        | CompletionKind.Delegate           -> [ body "M2 5 V11"; Stroke(Detail, "M2 8 H13 M9.5 4.5 L13 8 L9.5 11.5", 1.5) ] // a maps-to arrow
        | CompletionKind.TypeAbbreviation   -> [ body "M1.75 3 H8.75 M5.25 3 V13.25"; Stroke(Detail, "M10.5 6.5 H14.5 M10.5 9.75 H14.5", 1.5) ] // T=
        | CompletionKind.TypeParameter      -> [ Stroke(Detail, "M3.2 2 L2.5 5.5", 1.7); body "M5.75 3 H14 M9.9 3 V13.25" ] // 'T
        | CompletionKind.Module             -> [ body braces; Fill(Detail, "M6.25 6.25 H9.75 V9.75 H6.25 Z") ] // braces with a box
        | CompletionKind.Namespace          -> [ body braces ]
        | CompletionKind.Value              -> [ body brackets; Stroke(Detail, "M6 5.75 L10 10.25 M10 5.75 L6 10.25", 1.5) ] // [x]
        | CompletionKind.Literal
        | CompletionKind.SourceIdentifier   -> literal
        | CompletionKind.Keyword            -> [ Stroke(Detail, "M2 4 H14", 1.5); body "M2 8 H10 M2 12 H12" ] // lines of text
        | CompletionKind.CompilerDirective
        | CompletionKind.ReferenceDirective -> [ detail "M6.5 2 L5 14 M11.5 2 L10 14 M2.5 5.75 H14 M2 10.25 H13.5" ] // #
        | CompletionKind.NugetPackage       -> [ FillStroke(Detail, "M1.5 2.5 H14.5 V6 H1.5 Z", 1.3); body "M2.5 6 V13.5 H13.5 V6 M6.5 8.75 H9.5" ] // an archive box
        | CompletionKind.Unknown            -> []

    let private make (ps:Part list) (bodyBrush:SolidColorBrush) (detailBrush:SolidColorBrush) : ImageSource =
        let square = RectangleGeometry(Rect(0., 0., 16., 16.))
        square.Freeze()
        let g = DrawingGroup()
        // a transparent square, so that all icons have the full 16 x 16 bounds, and the Image does not scale them up.
        // The clip keeps round caps that reach over the edge from making the bounds bigger, so that the Image does not scale them down.
        g.Children.Add(GeometryDrawing(Brushes.Transparent, null, square))
        g.ClipGeometry <- square
        let brush ink =
            match ink with
            | Body   -> bodyBrush
            | Detail -> detailBrush
        let pen ink width = Pen(brush ink, width, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round)
        for p in ps do
            match p with
            | Fill (ink, path)              -> g.Children.Add(GeometryDrawing(brush ink, null, Geometry.Parse path))
            | Stroke (ink, path, width)     -> g.Children.Add(GeometryDrawing(null, pen ink width, Geometry.Parse path))
            | FillStroke (ink, path, width) -> g.Children.Add(GeometryDrawing(brush ink, pen ink width, Geometry.Parse path))
        let img = DrawingImage(g)
        img.Freeze()
        img :> ImageSource

    /// The icons for the light and for the dark theme
    let private cache = Dictionary<struct(CompletionKind * bool), ImageSource>()

    /// The icon of this kind, in the colors of the current theme. Null for CompletionKind.Unknown. Call from the UI thread.
    let ofKind (kind:CompletionKind) : ImageSource =
        let key = struct(kind, Theme.isDark)
        match cache.TryGetValue key with
        | true, img -> img
        | _ ->
            let img =
                match parts kind with
                | [] -> null
                | ps -> make ps Theme.completion.iconBody (detailColor (familyOf kind))
            cache.[key] <- img
            img
