namespace Fesh.Editor

open System
open System.Collections.Generic
open System.Windows
open System.Windows.Media
open FSharp.Compiler.EditorServices
open Fesh

/// The kind of an item in the completion list.
/// It sets the icon of the item and the color of its icon and text.
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


/// The icons in the completion list, drawn in the color of their kind.
[<RequireQualifiedAccess>]
module CompletionIcons =

    /// The icons are drawn on a 16 x 16 grid, the size of the Image in the item template of AvalonEditB.
    type private Part =
        | Fill   of path:string
        | Stroke of path:string * width:float

    let private line (path:string) = Stroke(path, 1.3)

    let private braces =
        "M5.5 2 C3.9 2 3.8 2.9 3.8 4.4 V6.4 C3.8 7.4 3.3 8 2.25 8 C3.3 8 3.8 8.6 3.8 9.6 V11.6 C3.8 13.1 3.9 14 5.5 14 " +
        "M10.5 2 C12.1 2 12.2 2.9 12.2 4.4 V6.4 C12.2 7.4 12.7 8 13.75 8 C12.7 8 12.2 8.6 12.2 9.6 V11.6 C12.2 13.1 12.1 14 10.5 14"

    let private brackets = "M5 2.5 H2.5 V13.5 H5 M11 2.5 H13.5 V13.5 H11"

    let private hash = "M6.5 2 L5 14 M11.5 2 L10 14 M2.5 5.75 H14 M2 10.25 H13.5"

    let private field = [ line "M2 5.75 H11 V12.5 H2 Z"; Fill "M2 5.75 L4.5 3.25 H13.5 L11 5.75 Z M11 5.75 L13.5 3.25 V10 L11 12.5 Z" ]

    let private parts (kind:CompletionKind) : Part list =
        match kind with
        | CompletionKind.Method             -> [ line "M8 1.75 L13.75 4.75 V11.25 L8 14.25 L2.25 11.25 V4.75 Z M2.25 4.75 L8 7.75 L13.75 4.75 M8 7.75 V14.25" ] // a cube
        | CompletionKind.ExtensionMethod    -> [ line "M6.5 1.5 L11 3.75 V8.75 L6.5 11 L2 8.75 V3.75 Z M2 3.75 L6.5 6 L11 3.75 M6.5 6 V11"; line "M12.75 10 V15 M10.25 12.5 H15.25" ] // a cube with a plus
        | CompletionKind.Function           -> [ Stroke("M11.6 2.7 C10.7 1.8 8.7 1.8 8.1 4 L6.2 12.3 C5.7 14.4 4 14.5 3 13.6", 1.5); Stroke("M4.8 6.6 H10.8", 1.5) ] // an ƒ
        | CompletionKind.Property           -> [ Stroke("M13.55 4.88 A3 3 0 1 1 11.12 2.45", 1.9); Stroke("M8.4 7.6 L3.2 12.8", 2.3) ] // a wrench
        | CompletionKind.Field
        | CompletionKind.RecordField        -> field // a brick
        | CompletionKind.Event              -> [ Fill "M9.6 1.2 L3.4 9 H7.6 L6.4 14.8 L12.6 7 H8.4 Z" ] // a lightning bolt
        | CompletionKind.Class              -> [ line "M1.75 2.75 H6.75 V7.75 H1.75 Z M6.75 5.25 H8.5 M8.5 4 V12 H10 M8.5 4 H10"; Fill "M10 2 H14 V6 H10 Z M10 10 H14 V14 H10 Z" ] // three linked boxes
        | CompletionKind.Struct             -> [ line "M2.5 2.5 H13.5 V13.5 H2.5 Z M8 2.5 V13.5 M2.5 8 H13.5"; Fill "M2.5 2.5 H8 V8 H2.5 Z" ] // a grid
        | CompletionKind.Interface          -> [ line "M1.5 8 H7.5 M14 8 A3.25 3.25 0 1 1 7.5 8 A3.25 3.25 0 1 1 14 8 Z" ] // a lollipop
        | CompletionKind.Union              -> [ line "M1.75 8 H5 C7.75 8 7.5 3.5 10.5 3.5 M5 8 C7.75 8 7.5 12.5 10.5 12.5"; Fill "M13 1.4 L15.1 3.5 L13 5.6 L10.9 3.5 Z M13 10.4 L15.1 12.5 L13 14.6 L10.9 12.5 Z" ] // a fork to two cases
        | CompletionKind.UnionCase          -> [ Fill "M8 2.75 L13.25 8 L8 13.25 L2.75 8 Z" ] // a diamond
        | CompletionKind.Enum               -> [ Fill "M4 1.8 L6.6 4.4 L4 7 L1.4 4.4 Z M4 9 L6.6 11.6 L4 14.2 L1.4 11.6 Z"; line "M9 4.4 H14.5 M9 11.6 H14.5" ] // a list of diamonds
        | CompletionKind.Exception          -> [ line "M8 2 L14.5 13.5 H1.5 Z M8 6.2 V9.4"; Fill "M8.9 11.6 A0.9 0.9 0 1 1 7.1 11.6 A0.9 0.9 0 1 1 8.9 11.6 Z" ] // a warning triangle
        | CompletionKind.Delegate           -> [ line "M2 5 V11 M2 8 H13 M9.5 4.5 L13 8 L9.5 11.5" ] // a maps-to arrow
        | CompletionKind.TypeAbbreviation   -> [ line "M1.75 3 H8.75 M5.25 3 V13.25 M10.5 6.5 H14.5 M10.5 9.75 H14.5" ] // T=
        | CompletionKind.TypeParameter      -> [ Stroke("M3.2 2 L2.5 5.5", 1.5); line "M5.75 3 H14 M9.9 3 V13.25" ] // 'T
        | CompletionKind.Module             -> [ line braces; Fill "M6.25 6.25 H9.75 V9.75 H6.25 Z" ] // braces with a box
        | CompletionKind.Namespace          -> [ line braces ]
        | CompletionKind.Value              -> [ line (brackets + " M6 5.75 L10 10.25 M10 5.75 L6 10.25") ] // [x]
        | CompletionKind.Literal
        | CompletionKind.SourceIdentifier   -> [ line (brackets + " M5.75 6.5 H10.25 M5.75 9.5 H10.25") ] // [=]
        | CompletionKind.Keyword            -> [ line "M2 4 H14 M2 8 H10 M2 12 H12" ] // lines of text
        | CompletionKind.CompilerDirective
        | CompletionKind.ReferenceDirective -> [ line hash ] // #
        | CompletionKind.NugetPackage       -> [ line "M1.5 2.5 H14.5 V6 H1.5 Z M2.5 6 V13.5 H13.5 V6 M6.5 8.75 H9.5" ] // an archive box
        | CompletionKind.Unknown            -> []

    let private make (ps:Part list) (brush:Brush) : ImageSource =
        let g = DrawingGroup()
        // a transparent square, so that all icons have the full 16 x 16 bounds, and the Image does not scale them up:
        g.Children.Add(GeometryDrawing(Brushes.Transparent, null, RectangleGeometry(Rect(0., 0., 16., 16.))))
        for p in ps do
            match p with
            | Fill path ->
                g.Children.Add(GeometryDrawing(brush, null, Geometry.Parse path))
            | Stroke (path, width) ->
                let pen = Pen(brush, width, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round)
                g.Children.Add(GeometryDrawing(null, pen, Geometry.Parse path))
        let img = DrawingImage(g)
        img.Freeze()
        img :> ImageSource

    /// The icon and the brush it was drawn with
    let private cache = Dictionary<CompletionKind, struct(Brush * ImageSource)>()

    /// The icon of this kind, drawn in this brush. Null for CompletionKind.Unknown.
    /// Cached until the brush changes, e.g. after a theme switch. Call from the UI thread.
    let get (kind:CompletionKind) (brush:Brush) : ImageSource =
        match cache.TryGetValue kind with
        | true, struct(b, img) when Object.ReferenceEquals(b, brush) -> img
        | _ ->
            let img =
                match parts kind with
                | [] -> null
                | ps -> make ps brush
            cache.[kind] <- struct(brush, img)
            img

    /// The icon of this kind, in the color of its kind from SyntaxHighlightingFSharp.xshd. Null for CompletionKind.Unknown.
    let ofKind (kind:CompletionKind) : ImageSource =
        match CompletionKind.textStyle kind with
        | ValueSome s -> get kind s.brush
        | ValueNone   -> get kind (Theme.editor.foreground :> Brush) // the xshd file is not loaded yet, or the color is missing
