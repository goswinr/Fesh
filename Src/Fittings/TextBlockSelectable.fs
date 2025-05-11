namespace Fittings

open System
open Avalonia
open Avalonia.Controls
open System.Reflection

// https://docs.avaloniaui.net/docs/reference/controls/selectable-textblock
type TextBlockSelectable = SelectableTextBlock

(* TODO delete:


/// Provides a type called TextBlockSelectable.
/// A Textblock with selectable text.
[<AutoOpen>]
module TextBlockAutoOpen =
    // adapted from https://stackoverflow.com/a/45627524/969070

    /// Utility to make a Textblock with selectable text
    type internal TextEditorWrapper(textContainer : obj, uiScope : FrameworkElement, isUndoEnabled : bool) =

        static let flagsI = BindingFlags.Instance ||| BindingFlags.NonPublic
        static let flagsS = BindingFlags.Static   ||| BindingFlags.NonPublic

        static let textEditorType = Type.GetType ("Avalonia.Documents.TextEditor, PresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35")

        static let isReadOnlyProp = textEditorType.GetProperty ("IsReadOnly", flagsI)

        static let textViewProp = textEditorType.GetProperty ("TextView", flagsI)

        static let registerMethod = textEditorType.GetMethod ("RegisterCommandHandlers", flagsS, null, [|typeof<Type>; typeof<bool>; typeof<bool>; typeof<bool>|], null)

        static let textContainerType = Type.GetType ("Avalonia.Documents.ITextContainer, PresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35")

        static let textContainerTextViewProp = textContainerType.GetProperty ("TextView")

        static let textContainerProp = typeof<TextBlock>.GetProperty ("TextContainer", flagsI)

        static member TextEditorType = textEditorType

        static member RegisterCommandHandlers(controlType : Type, acceptsRichContent : bool, readOnly : bool, registerEventListeners : bool) =
            registerMethod.Invoke (null, [|(controlType :> obj); (acceptsRichContent :> obj); (readOnly :> obj); (registerEventListeners :> obj)|])

        static member CreateFor(tb : TextBlock) =
            let mutable textContainer = textContainerProp.GetValue (tb)
            let mutable editorWr = new TextEditorWrapper(textContainer, tb, false)
            isReadOnlyProp.SetValue (editorWr.Editor, true)
            textViewProp.SetValue (editorWr.Editor, (textContainerTextViewProp.GetValue (textContainer)))
            editorWr

        member val Editor:obj =
            let flagsC = BindingFlags.Instance ||| BindingFlags.NonPublic ||| BindingFlags.CreateInstance
            Activator.CreateInstance (TextEditorWrapper.TextEditorType, flagsC, null, [|textContainer; (uiScope :> obj); (isUndoEnabled :> obj)|], null)

    /// Textblock with selectable text.
    /// Fails to serialize and on Hyperlinks as last run and on TextTrimming="CharacterEllipsis".
    /// see https://stackoverflow.com/a/45627524/969070
    type TextBlockSelectable() as this=
        inherit TextBlock()
        let _editor = TextEditorWrapper.CreateFor(this)

        static do
            Control.FocusableProperty.OverrideMetadata(typeof<TextBlockSelectable>, new FrameworkPropertyMetadata(true))
            TextEditorWrapper.RegisterCommandHandlers(typeof<TextBlockSelectable>, true, true, true) |> ignore

            // remove the focus rectangle around the control
            FrameworkElement.FocusVisualStyleProperty.OverrideMetadata(typeof<TextBlockSelectable>, new FrameworkPropertyMetadata(null))


*)
