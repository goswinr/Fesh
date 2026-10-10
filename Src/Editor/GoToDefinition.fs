namespace Fesh.Editor

open System
open System.IO

open FSharp.Compiler.Symbols
open FSharp.Compiler.Syntax // for PrettyNaming
open FSharp.Compiler.Text   // for Range

open Fesh.Model

/// Where a symbol is defined
type DefinitionLocation =
    /// In the file of the editor
    | InThisFile      of Range

    /// In a file that the script loads with #load
    | InLoadedFile    of FileInfo * Range

    /// In a source file on this computer, of a referenced assembly, e.g. of a locally built F# library
    | InLibrarySource of FileInfo * Range

    /// In a referenced assembly without a source file on this computer, e.g. from a NuGet package or the .NET runtime.
    /// For F# assemblies, builtFrom is the source location on the computer that built the assembly.
    | InAssembly      of assemblyName:string * assemblyPath:option<string> * builtFrom:option<Range>

type Definition = {
    /// The name of the symbol, e.g. 'map' for 'List.map'
    name     : string
    /// The full name of the symbol, e.g. 'Microsoft.FSharp.Collections.List.map'
    fullName : string
    symbol   : FSharpSymbol
    location : DefinitionLocation
    }

[<RequireQualifiedAccess>]
module GoToDefinition =

    let private isRooted (r:Range) = Path.IsPathRooted r.FileName // IL symbols have 'unknown' as file name

    let private onDisk (r:option<Range>) = r |> Option.filter (fun r -> isRooted r && File.Exists r.FileName)

    /// Finds the definition of the symbol at the offset, from the last type check result.
    /// Returns None if there is no symbol at the offset or if the file is not type checked yet.
    let find (ied:IEditor, offset:int) : option<Definition> =
        match ied.FileCheckState with
        | NotChecked | WaitForCompl _ | WaitForErr _ -> None
        | Done res when not res.checkRes.HasFullTypeCheckInfo -> None
        | Done res ->
            try
                let doc = ied.AvaEdit.Document
                if offset < 0 || offset > doc.TextLength then
                    None
                else
                    let ln = doc.GetLineByOffset offset
                    let lineTxt = doc.GetText ln
                    match TypeInfo.IdentifierIsland(true, lineTxt, offset - ln.Offset) with
                    | None -> None
                    | Some (word, colAtEndOfNames, _) ->
                        let names = PrettyNaming.GetLongNameFromString word
                        match res.checkRes.GetSymbolUseAtLocation(ln.LineNumber, colAtEndOfNames, lineTxt, names) with
                        | None -> None
                        | Some su ->
                            let sym = su.Symbol
                            let asm = try Some sym.Assembly with _ -> None
                            let dll = asm |> Option.bind (fun a -> a.FileName) // None for this script and the files it loads with #load
                            // su.FileName is the name of this file as given to the checker, e.g. with '.fsx' appended to '.fs' files
                            let isThisFile (r:Range) = String.Equals(r.FileName, su.FileName, StringComparison.OrdinalIgnoreCase)
                            let asmName = asm |> Option.map (fun a -> a.SimpleName) |> Option.defaultValue "?"
                            let location =
                                match dll with
                                | None -> // this script or a file it loads with #load
                                    match sym.DeclarationLocation with
                                    | Some r when isThisFile r -> InThisFile r
                                    | Some r when onDisk (Some r) |> Option.isSome -> InLoadedFile (FileInfo r.FileName, r)
                                    | r -> InAssembly (asmName, None, r |> Option.filter isRooted)
                                | Some _ ->
                                    // C# types have the range of the '#r' line in this file that references their assembly
                                    let decl = sym.DeclarationLocation    |> Option.filter (isThisFile >> not)
                                    let impl = sym.ImplementationLocation |> Option.filter (isThisFile >> not)
                                    // for libraries with .fsi files the implementation is more useful than the signature
                                    match onDisk impl |> Option.orElse (onDisk decl) with
                                    | Some r -> InLibrarySource (FileInfo r.FileName, r)
                                    | None   -> InAssembly (asmName, dll, impl |> Option.orElse decl |> Option.filter isRooted)
                            Some { name = sym.DisplayName; fullName = sym.FullName; symbol = sym; location = location }
            with e ->
                IFeshLog.log.PrintfnAppErrorMsg "GoToDefinition.find failed:\r\n%A" e
                None

    /// Finds the definition of the symbol under the mouse
    let findAtMouse (ied:IEditor, e:Windows.Input.MouseEventArgs) : option<Definition> =
        Mouse.getOffset(e, ied.AvaEdit)
        |> Option.bind (fun off -> find(ied, off))
