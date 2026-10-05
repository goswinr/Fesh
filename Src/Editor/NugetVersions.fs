namespace Fesh.Editor

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open System.Collections.Concurrent

open AvalonEditB.Document

open NuGet.Common
open NuGet.Configuration
open NuGet.Protocol
open NuGet.Protocol.Core.Types
open NuGet.Versioning

// The NuGet client libraries are only used in this file.
// The UI code in NugetHints.fs only uses the types declared here that have no NuGet types in their signature.
// So if a hosting app has already loaded other versions of the NuGet or System.Text.Json assemblies
// the resulting load errors can be caught and the feature turned off, instead of crashing the editor.

module NugetLoad =

    /// Checks if an exception comes from failing to load the NuGet client assemblies.
    let rec isLoadError (e:exn) =
        match e with
        | :? FileLoadException
        | :? FileNotFoundException
        | :? BadImageFormatException
        | :? TypeLoadException
        | :? MissingMethodException
        | :? MissingFieldException
        | :? TypeInitializationException -> true
        | :? AggregateException as ae -> ae.InnerExceptions |> Seq.exists isLoadError
        | _ -> false


/// A parsed line like:  #r "nuget: Name, 1.2.3"
/// All indices are into the text of this line.
type NugetDirective = {
    /// The package id as written.
    name: string

    /// The version as written, or "" if none is given.
    version: string

    /// The index right after the package name, where ", 1.2.3" can be inserted.
    nameEnd: int

    /// The index of the first character of the version, or -1.
    versionStart: int

    /// The index right after the version, or -1.
    versionEnd: int

    /// True if the name was given as  Include=Name .
    /// Then a version needs to be added as  Version=1.2.3 , a positional version would be taken as a second Include.
    isKeyed: bool
    }

module NugetDirective =

    /// Fast check without allocations: does the line start with #r after optional white space?
    let isCandidate (doc:TextDocument) (line:DocumentLine) =
        let en = line.EndOffset
        let mutable i = line.Offset
        while i < en && Char.IsWhiteSpace(doc.GetCharAt i) do
            i <- i + 1
        i + 1 < en && doc.GetCharAt i = '#' && doc.GetCharAt (i+1) = 'r'

    /// Parses a line like  #r "nuget: Name, 1.2.3"  or  #r "nuget: Include=Name, Version=1.2.3" .
    /// The comma separated options are the ones that FSharp.DependencyManager.Nuget understands.
    let tryParse (ln:string) : NugetDirective option =
        let len = ln.Length
        let mutable i = 0
        while i < len && Char.IsWhiteSpace ln[i] do
            i <- i + 1
        if String.CompareOrdinal(ln, i, "#r", 0, 2) <> 0 then
            None
        else
            i <- i + 2
            while i < len && ln[i] = ' ' do
                i <- i + 1
            if i < len && ln[i] = '@' then // a verbatim string
                i <- i + 1
            if i >= len || ln[i] <> '"' || String.CompareOrdinal(ln, i + 1, "nuget:", 0, 6) <> 0 then
                None
            else
                let contentStart = i + 7 // after "nuget:
                let closingQuote = ln.IndexOf('"', contentStart)
                if closingQuote < 0 then
                    None
                else
                    let mutable name = ""
                    let mutable nameEnd = -1
                    let mutable isKeyed = false
                    let mutable version = ""
                    let mutable versionStart = -1
                    let mutable versionEnd = -1
                    let mutable positionalCount = 0
                    let mutable partStart = contentStart
                    while partStart <= closingQuote do
                        let partEnd = // exclusive
                            match ln.IndexOf(',', partStart, closingQuote - partStart) with
                            | -1 -> closingQuote
                            | c  -> c
                        // trim the part:
                        let mutable a = partStart
                        let mutable b = partEnd
                        while a < b && Char.IsWhiteSpace ln[a]     do a <- a + 1
                        while b > a && Char.IsWhiteSpace ln[b - 1] do b <- b - 1
                        if a < b then
                            match ln.IndexOf('=', a, b - a) with
                            | -1 ->
                                let part = ln.Substring(a, b - a)
                                if part = "bl" then
                                    () // the flag for writing a binary log
                                elif positionalCount = 0 then
                                    name <- part
                                    nameEnd <- b
                                    positionalCount <- 1
                                elif positionalCount = 1 then
                                    version <- part
                                    versionStart <- a
                                    versionEnd <- b
                                    positionalCount <- 2
                            | eq ->
                                let key = ln.Substring(a, eq - a).Trim().ToLowerInvariant()
                                let mutable va = eq + 1
                                while va < b && Char.IsWhiteSpace ln[va] do
                                    va <- va + 1
                                if va < b then
                                    match key with
                                    | "include" ->
                                        name <- ln.Substring(va, b - va)
                                        nameEnd <- b
                                        isKeyed <- true
                                    | "version" ->
                                        version <- ln.Substring(va, b - va)
                                        versionStart <- va
                                        versionEnd <- b
                                    | _ -> () // e.g. restoresources=, script=, timeout=
                        partStart <- partEnd + 1

                    if name = "" then
                        None
                    else
                        Some {
                            name         = name
                            version      = version
                            nameEnd      = nameEnd
                            versionStart = versionStart
                            versionEnd   = versionEnd
                            isKeyed      = isKeyed
                            }


[<RequireQualifiedAccess>]
type NugetHintKind =
    /// The version is the latest one.
    | UpToDate
    /// A newer version exists.
    | UpdateTo
    /// No version or a floating version is given.
    | PinTo
    /// The package was not found on any source.
    | NotFound
    /// Nothing to show, e.g. while looking up, or no source was reachable.
    | NoHint

/// What to show after a  #r "nuget: ..."  line.
type NugetHint = {
    kind: NugetHintKind

    /// The version to insert on click.
    version: string

    /// The text for the tooltip.
    tip: string

    /// The project website from the package metadata, or "".
    projectUrl: string
    }

/// The result of looking up all versions of one package on all NuGet sources.
[<RequireQualifiedAccess>]
type NugetLookup =
    /// versions: All listed versions from all sources that answered, sorted ascending. Empty if the package does not exist.
    /// projectUrl: The project website of the latest version that has one, or "".
    /// checkedOn: The names of the sources that answered.
    /// notReachable: The names of the sources that failed, or "".
    | Found  of versions:NuGetVersion[] * projectUrl:string * checkedOn:string * notReachable:string
    | Failed of reason:string


/// Queries the NuGet package sources.
/// NuGet.Protocol is only used here, so that its assemblies only get loaded on a background thread when the first lookup starts.
type internal NugetFeeds private () =

    static let reposPerFolder = ConcurrentDictionary<string, SourceRepository[]>(StringComparer.OrdinalIgnoreCase)

    /// Shared for all lookups, also enables NuGet's own HTTP cache on disk.
    static let cacheContext = new SourceCacheContext()

    /// All enabled sources from the NuGet.Config files that apply to this folder.
    /// These are the same sources that 'dotnet restore' uses for #r "nuget: ..."
    static let loadRepos (folder:string) : SourceRepository[] =
        let settings = Settings.LoadDefaultSettings(if String.IsNullOrWhiteSpace folder then null else folder)
        PackageSourceProvider(settings).LoadPackageSources()
        |> Seq.filter (fun s -> s.IsEnabled)
        |> Seq.map (fun s -> Repository.Factory.GetCoreV3 s)
        |> Seq.toArray

    /// Only http and https links from the package metadata get opened in the browser.
    static let isWebUrl (u:Uri) =
        not (isNull u) && u.IsAbsoluteUri && (u.Scheme = Uri.UriSchemeHttps || u.Scheme = Uri.UriSchemeHttp)

    /// Get all listed versions and the project website of a package from all sources.
    static member GetVersions(folder:string, pkgId:string) : Async<NugetLookup> =
        async {
            let repos = reposPerFolder.GetOrAdd(folder, fun f -> loadRepos f)
            if repos.Length = 0 then
                return NugetLookup.Failed "No enabled NuGet package source found in the NuGet.Config files."
            else
                use cts = new CancellationTokenSource(TimeSpan.FromSeconds 30.0)
                let! perSource =
                    repos
                    |> Array.map (fun repo ->
                        async {
                            let srcName = repo.PackageSource.Name
                            try
                                let! res = repo.GetResourceAsync<PackageMetadataResource>(cts.Token) |> Async.AwaitTask
                                if isNull res then
                                    return None // this kind of source can't list versions
                                else
                                    // this downloads the same registration data as MetadataResource.GetVersions, but keeps the project URL too:
                                    let! metas = res.GetMetadataAsync(pkgId, true, false, cacheContext, NullLogger.Instance, cts.Token) |> Async.AwaitTask // true: includePrerelease, false: includeUnlisted
                                    let found = metas |> Seq.map (fun m -> struct(m.Identity.Version, m.ProjectUrl)) |> Seq.toArray
                                    return Some (Ok (srcName, found))
                            with e ->
                                if NugetLoad.isLoadError e then
                                    return raise e
                                else
                                    return Some (Error srcName)
                        })
                    |> Async.Parallel

                let answered = perSource |> Array.choose (function Some (Ok r)    -> Some r | _ -> None)
                let failed   = perSource |> Array.choose (function Some (Error n) -> Some n | _ -> None)
                if answered.Length = 0 then
                    if failed.Length = 0 then
                        return NugetLookup.Failed "None of the NuGet package sources can list package versions."
                    else
                        return NugetLookup.Failed ("Not reachable: " + String.Join(", ", failed))
                else
                    let all = answered |> Array.collect snd
                    let versions =
                        all
                        |> Array.map (fun struct(v, _) -> v)
                        |> Array.distinctBy (fun v -> v.ToNormalizedString().ToLowerInvariant())
                        |> Array.sortWith (fun a b -> VersionComparer.Default.Compare(a, b))
                    let projectUrl = // from the latest stable version that has one, else from the latest prerelease
                        all
                        |> Array.filter (fun struct(_, u) -> isWebUrl u)
                        |> Array.sortWith (fun struct(a, _) struct(b, _) ->
                            match compare a.IsPrerelease b.IsPrerelease with
                            | 0 -> VersionComparer.Default.Compare(b, a) // newest first
                            | c -> c )                                    // stable first
                        |> Array.tryHead
                        |> Option.map (fun struct(_, u) -> u.AbsoluteUri)
                        |> Option.defaultValue ""
                    let checkedOn = answered |> Array.map fst |> String.concat ", "
                    return NugetLookup.Found (versions, projectUrl, checkedOn, String.Join(", ", failed))
        }


/// A cache of the versions of all packages used in #r "nuget: ..." lines, shared by all editor tabs.
type NugetVersions private () =

    /// The completed lookups and when they finished.
    static let results  = ConcurrentDictionary<string, struct(NugetLookup * DateTime)>()

    /// The running lookups, so that each package is only looked up once at a time.
    static let inFlight = ConcurrentDictionary<string, Lazy<Task<NugetLookup>>>()

    static let mutable loadError : exn option = None

    static let noHint = { kind = NugetHintKind.NoHint; version = ""; tip = ""; projectUrl = "" }

    /// The sources depend on the NuGet.Config files of the script folder, so the folder is part of the key.
    static let keyOf (folder:string) (name:string) =
        folder.ToLowerInvariant() + "|" + name.ToLowerInvariant()

    static let isStale (struct(res:NugetLookup, time:DateTime)) =
        let maxAge =
            match res with
            | NugetLookup.Found _  -> TimeSpan.FromHours 1.0
            | NugetLookup.Failed _ -> TimeSpan.FromMinutes 2.0 // retry sooner if offline
        DateTime.UtcNow - time > maxAge

    static let startLookup (key:string) (folder:string) (name:string) : Task<NugetLookup> =
        async {
            let! res =
                async {
                    try
                        return! NugetFeeds.GetVersions(folder, name)
                    with e ->
                        if NugetLoad.isLoadError e then
                            loadError <- Some e
                        return NugetLookup.Failed e.Message
                }
            results[key] <- struct(res, DateTime.UtcNow)
            inFlight.TryRemove(key) |> ignore
            return res
        }
        |> Async.StartAsTask

    static let makeHint (d:NugetDirective) (res:NugetLookup) : NugetHint =
        match res with
        | NugetLookup.Failed _ ->
            noHint

        | NugetLookup.Found (versions, projectUrl, checkedOn, notReachable) ->
            let sources =
                if notReachable = "" then $"Checked on: {checkedOn}"
                else                      $"Checked on: {checkedOn}\r\nNot reachable: {notReachable}"

            let hint kind version tip = { kind = kind; version = version; tip = tip; projectUrl = projectUrl }

            let latest (includePrerelease:bool) = // versions are sorted ascending
                let mutable i = versions.Length - 1
                while i >= 0 && not includePrerelease && versions[i].IsPrerelease do
                    i <- i - 1
                if i >= 0 then Some versions[i] else None

            let str (v:NuGetVersion) = v.ToNormalizedString()

            match latest true, latest false with
            | None, _ ->
                hint NugetHintKind.NotFound "" $"Package '{d.name}' was not found.\r\n{sources}"

            | Some latestAny, latestStable ->
                let latestStableOrAny = defaultArg latestStable latestAny
                if d.version = "" then
                    let v = str latestStableOrAny
                    hint NugetHintKind.PinTo v $"Click to pin {d.name} to its latest version {v}.\r\n{sources}"
                else
                    let mutable current : NuGetVersion = null
                    let mutable range   : VersionRange = null
                    if NuGetVersion.TryParse(d.version, &current) then
                        let target = if current.IsPrerelease then latestAny else latestStableOrAny
                        if VersionComparer.Default.Compare(current, target) >= 0 then
                            let preNote =
                                if not current.IsPrerelease && VersionComparer.Default.Compare(latestAny, current) > 0 then
                                    $"\r\nA newer prerelease exists: {str latestAny}"
                                else
                                    ""
                            hint NugetHintKind.UpToDate d.version $"{d.name} {d.version} is the latest version.{preNote}\r\n{sources}"
                        else
                            let t = str target
                            hint NugetHintKind.UpdateTo t $"Click to update {d.name} from {d.version} to {t}.\r\n{sources}\r\n\r\nIf version {d.version} is already loaded in the running FSI session,\r\nFSI needs a reset to use version {t}."

                    elif VersionRange.TryParse(d.version, true, &range) && range.IsFloating then // like *, 1.* or *-*
                        match range.FindBestMatch(versions) with
                        | null -> noHint
                        | best ->
                            let b = str best
                            hint NugetHintKind.PinTo b $"Click to pin {d.name} to {b}.\r\n'{d.version}' currently resolves to {b}.\r\n{sources}"
                    else
                        noHint // a version range without floating, or an invalid version

    /// Set if the NuGet client assemblies could not be loaded.
    static member LoadError = loadError

    /// Returns the hint to show for this line and whether a lookup is needed because there is no result yet or it is outdated.
    /// Never blocks and does not start a lookup itself, call Fetch for that.
    static member TryGetHint(folder:string, d:NugetDirective) : struct(NugetHint * bool) =
        match results.TryGetValue(keyOf folder d.name) with
        | true, entry ->
            let struct(res, _) = entry
            struct(makeHint d res, isStale entry)
        | _ ->
            struct(noHint, true)

    /// Looks up the versions of the given packages, unless there is a recent result already.
    /// Completes when all of these lookups are done.
    static member Fetch(folder:string, names:string[]) : Async<unit> =
        let tasks =
            [| for name in names do
                let key = keyOf folder name
                let needed =
                    match results.TryGetValue key with
                    | true, entry -> isStale entry
                    | _ -> true
                if needed then
                    inFlight.GetOrAdd(key, fun k -> lazy (startLookup k folder name)).Value
            |]
        async {
            if tasks.Length > 0 then
                do! Task.WhenAll(tasks) |> Async.AwaitTask |> Async.Ignore
        }
