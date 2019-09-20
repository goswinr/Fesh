namespace Seff

open Seff.Util
open System
open System.Windows
open System.Windows.Controls
open System.Windows.Media

open ICSharpCode.AvalonEdit
open ICSharpCode.AvalonEdit.Editing
open ICSharpCode.AvalonEdit.Document
open FSharp.Compiler
open FSharp.Compiler.SourceCodeServices
open ICSharpCode.AvalonEdit.Folding

module Folding = 

    // Folding strategy
    type FsFolding() =
        member this.GenerateFoldMarkers(document: IDocument , fileName, parseInformation) =
            Seq.empty
        
        //interface  IFoldingStrategy with
        //    member this.GenerateFoldMarkers(doc , file, info ) = this.GenerateFoldMarkers(doc , file, info )