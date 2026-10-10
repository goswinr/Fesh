namespace Fesh.Editor

open System
open System.IO
open System.Net.Http
open System.Reflection.Metadata
open System.Reflection.PortableExecutable
open System.Text.RegularExpressions

open FSharp.Compiler.Symbols
open FSharp.Compiler.Text   // for Range

open Fesh.Model

/// For C# symbols the PDB only has the lines of code in methods.
/// So the declaration is searched upwards from there, with this regex.
type FindUp = {
    pattern  : string
    /// Stop at the end of the previous member, a line ending with ';' or '}'
    inMember : bool
    }

/// A source file of a repository, found via the SourceLink map in the debug symbols (PDB) of an assembly
type SourceLinkFile = {
    /// Where to download the file from, e.g. from https://raw.githubusercontent.com/...
    url       : string
    /// Where the downloaded file is cached
    localPath : string
    /// The line of the definition, one based
    line      : int
    findUp    : option<FindUp>
    }

/// The debug symbols of an assembly, as named on a symbol server
type SymbolFile = {
    pdbName  : string
    /// e.g. 'euclid.pdb/a0034631517e49f6a65a62eabd7d741fFFFFFFFF/euclid.pdb'
    key      : string
    /// e.g. 'SHA256:4f2a..', symbols.nuget.org requires it in the 'SymbolChecksum' header
    checksum : string
    }

type SourceLinkLookup =
    | SourceFound   of SourceLinkFile
    /// The debug symbols are not on this computer. They need to be downloaded from a symbol server first.
    | SymbolsNeeded of SymbolFile
    | NoSource      of reason:string


/// Finds the source of symbols from referenced assemblies, like NuGet packages, via SourceLink:
/// The debug symbols (PDB) of an assembly can have a map from the source paths at build time to URLs in the repository.
[<RequireQualifiedAccess>]
module SourceLink =

    /// For downloaded debug symbols and source files
    let cacheFolder = Path.Combine(Path.GetTempPath(), "Fesh", "SourceLink")

    let symbolServers = [ "https://symbols.nuget.org/download/symbols/" ; "https://msdl.microsoft.com/download/symbols/" ]

    let private sourceLinkKind = Guid "CC110556-A091-4D38-9FEC-25AB9A351A6A"

    let private http =
        lazy (
            let c = new HttpClient(Timeout = TimeSpan.FromSeconds 60.0)
            c.DefaultRequestHeaders.UserAgent.ParseAdd "Fesh"
            c )

    let private cachedPdbPath (key:string) = Path.Combine(cacheFolder, "symbols", key.Replace('/', '\\'))

    /// The file in the cache folder, with the path from the URL
    let private cachedSourcePath (url:string) =
        let u = Uri url
        let bad = Path.GetInvalidFileNameChars()
        let clean (s:string) = String(s.ToCharArray() |> Array.map (fun c -> if Array.contains c bad then '_' else c))
        let parts = u.AbsolutePath.Split([|'/'|], StringSplitOptions.RemoveEmptyEntries) |> Array.map (Uri.UnescapeDataString >> clean)
        Path.Combine(Array.append [| cacheFolder; "src"; clean u.Host |] parts)

    /// Reference assemblies have no code, only the signatures, e.g. the ones of .NET in 'C:\Program Files\dotnet\packs'
    let private isReferenceAssembly (dll:string) =
        dll.Contains @"\ref\" || dll.Contains "Reference Assemblies"

    /// The name, key and checksum of the PDB on a symbol server, from the debug directory of the assembly
    let private symbolFile (pe:PEReader) : option<SymbolFile> =
        let entries = pe.ReadDebugDirectory()
        let checksum =
            entries
            |> Seq.filter (fun e -> e.Type = DebugDirectoryEntryType.PdbChecksum)
            |> Seq.map (fun e ->
                let c = pe.ReadPdbChecksumDebugDirectoryData e
                c.AlgorithmName + ":" + (c.Checksum |> Seq.map (fun b -> b.ToString "x2") |> String.concat ""))
            |> String.concat ";"
        entries
        |> Seq.tryFind (fun e -> e.Type = DebugDirectoryEntryType.CodeView && e.IsPortableCodeView)
        |> Option.map (fun e ->
            let cv = pe.ReadCodeViewDebugDirectoryData e
            let name = Path.GetFileName(cv.Path).ToLowerInvariant()
            { pdbName  = name
              key      = sprintf "%s/%sFFFFFFFF/%s" name (cv.Guid.ToString "N") name // the key format for portable PDBs
              checksum = checksum } )

    /// The debug symbols from inside the assembly, from next to it, or from the cache folder
    let private openPdb (pe:PEReader, dll:string) : option<MetadataReaderProvider> =
        let openIfExists (path:string) : Stream = if File.Exists path then File.OpenRead path :> Stream else null
        match pe.TryOpenAssociatedPortablePdb(dll, Func<string,Stream> openIfExists) with
        | true, provider, _ -> Some provider
        | _ ->
            symbolFile pe
            |> Option.map (fun s -> cachedPdbPath s.key)
            |> Option.filter File.Exists
            |> Option.map (fun p -> MetadataReaderProvider.FromPortablePdbStream(File.OpenRead p))

    /// The json is like {"documents":{"D:\\a\\_work\\1\\s\\*":"https://raw.githubusercontent.com/dotnet/fsharp/abc123/*"}}
    let private sourceLinkMap (pdb:MetadataReader) : list<string*string> =
        pdb.GetCustomDebugInformation(EntityHandle.ModuleDefinition)
        |> Seq.map (fun h -> pdb.GetCustomDebugInformation(h:CustomDebugInformationHandle))
        |> Seq.tryFind (fun cdi -> pdb.GetGuid cdi.Kind = sourceLinkKind)
        |> Option.map (fun cdi ->
            let json = Text.Encoding.UTF8.GetString(pdb.GetBlobBytes cdi.Value)
            Regex.Matches(json, @"""((?:[^""\\]|\\.)*)""\s*:\s*""((?:[^""\\]|\\.)*)""")
            |> Seq.cast<Match>
            |> Seq.map (fun m -> Regex.Unescape m.Groups[1].Value, Regex.Unescape m.Groups[2].Value)
            |> Seq.toList )
        |> Option.defaultValue []

    /// The longest matching key wins. A key ending in '*' matches all paths that start with it.
    let private mapToUrl (map:list<string*string>) (path:string) =
        map
        |> List.sortByDescending (fun (key, _) -> key.Length)
        |> List.tryPick (fun (key, url) ->
            if key.EndsWith "*" then
                let start = key.Substring(0, key.Length - 1)
                if path.StartsWith(start, StringComparison.OrdinalIgnoreCase) then
                    let rest =
                        path.Substring(start.Length).Split([|'\\'; '/'|])
                        |> Array.map Uri.EscapeDataString
                        |> String.concat "/"
                    Some (url.Replace("*", rest))
                else
                    None
            elif String.Equals(key, path, StringComparison.OrdinalIgnoreCase) then
                Some url
            else
                None )

    /// For packages without SourceLink: The GitHub repository and commit from the .nuspec file of the NuGet package,
    /// and the path below the last folder with the name of the repository, e.g. 'Src/Pnt.fs' from 'D:\a\Euclid\Euclid\Src\Pnt.fs'.
    let private urlFromNuspec (dll:string, doc:string) : Result<string,string> =
        let rec findNuspec (d:DirectoryInfo) depth = // the dll is in e.g. ~/.nuget/packages/euclid/0.51.0/lib/net6.0
            if isNull d || depth > 3 then None
            else
                match d.GetFiles "*.nuspec" with
                | [||] -> findNuspec d.Parent (depth + 1)
                | fs   -> Some fs[0].FullName
        match findNuspec (FileInfo dll).Directory 0 with
        | None -> Error "its debug symbols have no SourceLink map"
        | Some nuspec ->
            let repo = Xml.Linq.XDocument.Load(nuspec).Descendants() |> Seq.tryFind (fun e -> e.Name.LocalName = "repository")
            let attr (name:string) = repo |> Option.bind (fun e -> e.Attribute(Xml.Linq.XName.Get name) |> Option.ofObj) |> Option.map (fun a -> a.Value)
            match attr "url", attr "commit" with
            | Some url, Some commit ->
                let m = Regex.Match(url, @"^https://github\.com/([^/]+)/([^/]+?)(\.git)?/?$", RegexOptions.IgnoreCase)
                if not m.Success then
                    Error $"its debug symbols have no SourceLink map, and its repository is not on GitHub: {url}"
                else
                    let owner, name = m.Groups[1].Value, m.Groups[2].Value
                    let parts = doc.Split([|'\\'; '/'|], StringSplitOptions.RemoveEmptyEntries)
                    match parts |> Array.tryFindIndexBack (fun p -> String.Equals(p, name, StringComparison.OrdinalIgnoreCase)) with
                    | Some i when i < parts.Length - 1 ->
                        let path = parts[i+1 ..] |> Array.map Uri.EscapeDataString |> String.concat "/"
                        Ok $"https://raw.githubusercontent.com/{owner}/{name}/{commit}/{path}"
                    | _ ->
                        Error $"its debug symbols have no SourceLink map, and its build path has no folder named like the repository '{name}': {doc}"
            | _ ->
                Error "its debug symbols have no SourceLink map, and its NuGet package has no repository commit"

    /// The document in the PDB with the same file name and the longest common end of the path.
    /// The document paths in the PDB might be mapped differently than the path in the F# signature data, e.g. to '/_/src/..'
    let private findDocument (pdb:MetadataReader, path:string) : option<string> =
        let parts (p:string) = p.Split([|'\\'; '/'|], StringSplitOptions.RemoveEmptyEntries)
        let target = parts path
        let commonEnd (doc:string) =
            let d = parts doc
            let mutable k = 0
            while k < d.Length && k < target.Length && String.Equals(d[d.Length-1-k], target[target.Length-1-k], StringComparison.OrdinalIgnoreCase) do
                k <- k + 1
            k
        pdb.Documents
        |> Seq.map (fun h -> pdb.GetString (pdb.GetDocument h).Name)
        |> Seq.map (fun doc -> doc, commonEnd doc)
        |> Seq.filter (fun (_, k) -> k > 0)
        |> Seq.sortByDescending snd
        |> Seq.tryHead
        |> Option.map fst

    let rec private isType (md:MetadataReader) (td:TypeDefinition) (e:FSharpEntity) =
        md.GetString td.Name = e.CompiledName
        && (let outer = td.GetDeclaringType()
            match e.DeclaringEntity |> Option.filter (fun d -> not d.IsNamespace) with
            | Some de -> not outer.IsNil && isType md (md.GetTypeDefinition outer) de
            | None    -> outer.IsNil && md.GetString td.Namespace = defaultArg e.Namespace "")

    /// The first sequence point of a method, it has the document and line
    let private firstPoint (pdb:MetadataReader) (h:MethodDefinitionHandle) =
        pdb.GetMethodDebugInformation(h).GetSequencePoints() |> Seq.tryFind (fun sp -> not sp.IsHidden)

    let private paramCount (md:MetadataReader) (m:MethodDefinition) =
        let mutable br = md.GetBlobReader m.Signature
        let header = br.ReadSignatureHeader()
        if header.IsGeneric then br.ReadCompressedInteger() |> ignore
        br.ReadCompressedInteger()

    /// For symbols without a source location, e.g. from C# assemblies.
    /// The PDB has the lines of the code in methods, so for a type it is the first line with code.
    /// The FindUp is for searching the actual declaration in the source file later.
    let private ilLocation (md:MetadataReader, pdb:MetadataReader, sym:FSharpSymbol) : option<DocumentHandle * int * FindUp> =
        let typeOf (e:FSharpEntity) =
            md.TypeDefinitions |> Seq.tryFind (fun h -> isType md (md.GetTypeDefinition h) e)
        let findType (e:FSharpEntity) =
            { pattern = $@"\b(class|struct|interface|record|enum)\s+{Regex.Escape e.DisplayName}\b"; inMember = false }
        let topOfType (e:FSharpEntity) (th:TypeDefinitionHandle) =
            match md.GetTypeDefinition(th).GetMethods() |> Seq.choose (firstPoint pdb) |> Seq.toList with
            | [] -> None
            | p :: _ as points ->
                // for partial classes, prefer the file with the name of the type, e.g. 'JObject.cs' over 'JObject.Async.cs'
                let isNamedLikeType (h:DocumentHandle) =
                    String.Equals(Path.GetFileNameWithoutExtension(pdb.GetString (pdb.GetDocument h).Name), e.DisplayName, StringComparison.OrdinalIgnoreCase)
                let doc = points |> List.tryFind (fun q -> isNamedLikeType q.Document) |> Option.map (fun q -> q.Document) |> Option.defaultValue p.Document
                let top = points |> List.filter (fun q -> q.Document = doc) |> List.minBy (fun q -> q.StartLine)
                Some (top.Document, top.StartLine, findType e)
        let ofType (e:FSharpEntity) =
            typeOf e |> Option.bind (topOfType e)
        match sym with
        | :? FSharpEntity as e ->
            ofType e
        | :? FSharpField as f ->
            f.DeclaringEntity |> Option.bind ofType
        | :? FSharpMemberOrFunctionOrValue as m ->
            match m.DeclaringEntity |> Option.bind (fun e -> typeOf e |> Option.map (fun th -> e, th)) with
            | None -> None
            | Some (e, th) ->
                let name =
                    if   m.IsProperty && m.HasGetterMethod then m.GetterMethod.CompiledName
                    elif m.IsProperty && m.HasSetterMethod then m.SetterMethod.CompiledName
                    elif m.IsEvent then m.EventAddMethod.CompiledName
                    else m.CompiledName
                let nameInSource = // e.g. 'Count' for 'get_Count', 'Dispose' for 'System.IDisposable.Dispose'
                    if name = ".ctor" || name = ".cctor" then e.DisplayName
                    else
                        let n = name.Substring(name.LastIndexOf '.' + 1)
                        match n.IndexOf '_' with
                        | i when i > 0 && (n.StartsWith "get_" || n.StartsWith "set_" || n.StartsWith "add_" || n.StartsWith "remove_") -> n.Substring(i + 1)
                        | _ -> n
                let count = m.CurriedParameterGroups |> Seq.sumBy (fun g -> g.Count)
                let overloads =
                    md.GetTypeDefinition(th).GetMethods()
                    |> Seq.filter (fun h -> md.GetString (md.GetMethodDefinition h).Name = name)
                    |> Seq.toList
                overloads
                |> List.tryFind (fun h -> paramCount md (md.GetMethodDefinition h) = count)
                |> Option.orElse (List.tryHead overloads)
                |> Option.bind (firstPoint pdb)
                |> Option.map (fun sp -> sp.Document, sp.StartLine, { pattern = $@"\b{Regex.Escape nameInSource}\b"; inMember = true })
                |> Option.orElse (topOfType e th) // e.g. abstract members have no code
        | _ -> None

    /// For C# symbols: searches upwards from the line of code to the declaration in the downloaded file.
    /// For members it stops at the end of the previous member, because an expression bodied member has its declaration and code on the same line.
    let declarationLine (src:SourceLinkFile) : int =
        match src.findUp with
        | None -> src.line
        | Some fu ->
            try
                let lines = File.ReadAllLines src.localPath
                let re = Regex fu.pattern
                let rec up i =
                    if i < 0 then None
                    elif re.IsMatch lines[i] then Some (i + 1)
                    else
                        let t = lines[i].TrimEnd()
                        if fu.inMember && (t.EndsWith ";" || t.EndsWith "}") then None
                        else up (i - 1)
                let start = min (src.line - 2) (lines.Length - 1) // the line above the code
                up start |> Option.defaultValue src.line
            with _ ->
                src.line

    /// Looks up the source file of a symbol from a referenced assembly, in the SourceLink map of its debug symbols.
    /// Doesn't download anything.
    let lookup (sym:FSharpSymbol, dll:string, builtFrom:option<Range>) : SourceLinkLookup =
        try
            let dll = dll.Replace('/', '\\') // FCS might give paths with forward slashes
            if isReferenceAssembly dll then
                if dll.Contains @"\dotnet\packs\" then NoSource "it is a reference assembly of .NET, it has no code. The source of .NET is on https://source.dot.net"
                else                                  NoSource "it is a reference assembly, it has no code"
            else
                use stream = File.OpenRead dll
                use pe = new PEReader(stream)
                match openPdb (pe, dll) with
                | None ->
                    match symbolFile pe with
                    | Some s -> SymbolsNeeded s
                    | None   -> NoSource "it has no portable debug symbols"
                | Some provider ->
                    use provider = provider
                    let pdb = provider.GetMetadataReader()
                    let location =
                        match builtFrom with
                        | Some r ->
                            findDocument (pdb, r.FileName) |> Option.map (fun doc -> doc, r.StartLine, None)
                        | None ->
                            ilLocation (pe.GetMetadataReader(), pdb, sym)
                            |> Option.map (fun (h, line, findUp) -> pdb.GetString (pdb.GetDocument h).Name, line, Some findUp)
                    match location with
                    | None -> NoSource "it is not in the debug symbols"
                    | Some (doc, line, findUp) ->
                        let url =
                            match sourceLinkMap pdb with
                            | [] -> urlFromNuspec (dll, doc)
                            | map ->
                                match mapToUrl map doc with
                                | Some url -> Ok url
                                | None     -> Error $"the SourceLink map has no URL for {doc}"
                        match url with
                        | Ok url       -> SourceFound { url = url; localPath = cachedSourcePath url; line = line; findUp = findUp }
                        | Error reason -> NoSource reason
        with e ->
            NoSource e.Message

    /// The message of the inner exception of an AggregateException from a Task
    let errorMessage (e:exn) =
        match e with
        | :? AggregateException as a when not (isNull a.InnerException) -> a.InnerException.Message
        | e -> e.Message

    let private download (url:string, path:string, symbolChecksum:string) =
        async {
            use request = new HttpRequestMessage(HttpMethod.Get, url)
            if symbolChecksum <> "" then
                request.Headers.TryAddWithoutValidation("SymbolChecksum", symbolChecksum) |> ignore
            use! response = http.Value.SendAsync request |> Async.AwaitTask
            response.EnsureSuccessStatusCode() |> ignore
            let! bytes = response.Content.ReadAsByteArrayAsync() |> Async.AwaitTask
            Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
            File.WriteAllBytes(path, bytes)
        }

    /// Downloads the debug symbols from the first symbol server that has them. Then looks up the source file again.
    let downloadSymbols (sym:FSharpSymbol, dll:string, builtFrom:option<Range>, pdb:SymbolFile) : Async<SourceLinkLookup> =
        let rec fromServer servers =
            async {
                match servers with
                | [] -> return false
                | server :: rest ->
                    try
                        IFeshLog.log.PrintfnInfoMsg "Downloading %s%s" server pdb.key
                        do! download (server + pdb.key, cachedPdbPath pdb.key, pdb.checksum)
                        return true
                    with e ->
                        IFeshLog.log.PrintfnInfoMsg "    %s" (errorMessage e)
                        return! fromServer rest
            }
        async {
            match! fromServer symbolServers with
            | true  -> return lookup (sym, dll, builtFrom)
            | false -> return NoSource "its debug symbols are not on the symbol servers"
        }

    /// Downloads the source file, if it is not in the cache folder yet. Makes it read-only.
    let downloadSource (src:SourceLinkFile) =
        async {
            if not (File.Exists src.localPath) then
                IFeshLog.log.PrintfnInfoMsg "Downloading %s" src.url
                do! download (src.url, src.localPath, "")
                File.SetAttributes(src.localPath, FileAttributes.ReadOnly)
        }


/// A file in a GitHub repository at a commit
type GitHubFile = {
    owner  : string
    repo   : string
    commit : string
    /// The path in the repository, with '/'
    path   : string
    }

/// Clones the repository of a source file found via SourceLink, so that VS Code can load its project and navigate further.
[<RequireQualifiedAccess>]
module GitClone =

    /// From URLs like https://raw.githubusercontent.com/goswinr/Euclid/e3a75e6/Src/Pnt.fs
    let tryGitHubFile (url:string) : option<GitHubFile> =
        let u = Uri url
        let parts = u.AbsolutePath.Split([|'/'|], StringSplitOptions.RemoveEmptyEntries)
        if u.Host = "raw.githubusercontent.com" && parts.Length >= 4 then
            Some { owner = parts[0]; repo = parts[1]; commit = parts[2]; path = parts[3..] |> Array.map Uri.UnescapeDataString |> String.concat "/" }
        else
            None

    /// Repositories with more files get only the folder of the project checked out
    let maxFilesForFullCheckout = 3000

    /// The short commit keeps the paths shorter
    let folder (g:GitHubFile) =
        Path.Combine(SourceLink.cacheFolder, "repos", g.owner, g.repo, g.commit.Substring(0, min 12 g.commit.Length))

    let fileInClone (g:GitHubFile) = Path.Combine(folder g, g.path.Replace('/', '\\'))

    let isCloned (g:GitHubFile) = File.Exists (fileInClone g)

    let private git (dir:string) (args:string) : Async<string> =
        async {
            let psi = Diagnostics.ProcessStartInfo("git", args)
            psi.WorkingDirectory       <- dir
            psi.UseShellExecute        <- false
            psi.CreateNoWindow         <- true
            psi.RedirectStandardOutput <- true
            psi.RedirectStandardError  <- true
            psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] <- "0" // public repositories need no login, so never ask for one
            psi.EnvironmentVariables["GCM_INTERACTIVE"]     <- "never"
            use p =
                try Diagnostics.Process.Start psi
                with e -> failwithf "git could not be started. Is git installed and on the PATH? %s" e.Message
            let outTask = p.StandardOutput.ReadToEndAsync()
            let! err = p.StandardError.ReadToEndAsync() |> Async.AwaitTask
            let! out = outTask |> Async.AwaitTask
            p.WaitForExit()
            if p.ExitCode <> 0 then failwithf "git %s\r\n%s" args (err.Trim())
            return out
        }

    /// Git makes its object files read-only, so Directory.Delete would fail on them
    let rec private deleteFolder (d:DirectoryInfo) =
        for f in d.GetFiles() do
            f.Attributes <- FileAttributes.Normal
            f.Delete()
        for s in d.GetDirectories() do
            deleteFolder s
        d.Delete()

    /// The deepest folder above the file that has a project or solution file, as path in the repository
    let private projectFolder (files:string[]) (path:string) =
        let isProject (f:string) = f.EndsWith ".fsproj" || f.EndsWith ".csproj" || f.EndsWith ".vbproj" || f.EndsWith ".sln" || f.EndsWith ".slnx"
        let dirOf (f:string) = match f.LastIndexOf '/' with -1 -> "" | i -> f.Substring(0, i)
        let projectDirs = files |> Seq.filter isProject |> Seq.map dirOf |> Set.ofSeq
        let rec up (dir:string) =
            if dir = "" then None
            elif projectDirs.Contains dir then Some dir
            else up (dirOf dir)
        up (dirOf path)

    /// The folder of the project of the file, or else the folder of the file
    let private folderToCheckOut (tree:string) (path:string) =
        let files = tree.Split([|'\n'|], StringSplitOptions.RemoveEmptyEntries)
        let folder = projectFolder files path |> Option.defaultValue (path.Substring(0, max 0 (path.LastIndexOf '/')))
        files.Length, folder

    /// Clones the repository at the commit, with only that commit and without the history.
    /// For big repositories only the folder of the project, plus the files in all folders above it, like Directory.Build.props.
    /// Returns the folder of the clone.
    let clone (g:GitHubFile) : Async<string> =
        async {
            let dir = folder g
            if isCloned g then
                return dir

            elif Directory.Exists (Path.Combine(dir, ".git")) then
                // a big repository, cloned before for another project
                let! tree = git dir "ls-tree -r --name-only HEAD"
                let _, sparse = folderToCheckOut tree g.path
                IFeshLog.log.PrintfnInfoMsg "Adding the folder %s to the clone in %s" sparse dir
                let! _ = git dir $"sparse-checkout add \"{sparse}\""
                return dir

            else
                let partial = dir + "-partial"
                if Directory.Exists partial then deleteFolder (DirectoryInfo partial) // from a failed attempt
                Directory.CreateDirectory partial |> ignore
                IFeshLog.log.PrintfnInfoMsg "Cloning https://github.com/%s/%s at commit %s to %s" g.owner g.repo g.commit dir
                let! _ = git partial "init -q"
                let! _ = git partial "config core.longpaths true"   // the paths in the Temp folder can get long
                let! _ = git partial "config credential.helper \"\"" // public repositories need no login, so never ask for one
                let! _ = git partial $"remote add origin https://github.com/{g.owner}/{g.repo}.git"
                let! _ = git partial $"fetch -q --depth 1 --filter=blob:none origin {g.commit}" // the file contents come with the checkout
                let! tree = git partial "ls-tree -r --name-only FETCH_HEAD"
                let fileCount, sparse = folderToCheckOut tree g.path
                if fileCount > maxFilesForFullCheckout then
                    IFeshLog.log.PrintfnInfoMsg "    The repository has %d files, checking out only the folder %s" fileCount sparse
                    let! _ = git partial $"sparse-checkout set --cone \"{sparse}\""
                    ()
                let! _ = git partial "checkout -q FETCH_HEAD"
                Directory.Move(partial, dir)
                return dir
        }
