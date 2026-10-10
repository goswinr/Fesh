namespace Fesh.Editor

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open System.Collections.Generic
open System.Collections.Concurrent

open AvalonEditB.Document

open Fesh.Model

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

    /// The characters allowed in a NuGet package id.
    let isPackageIdChar (c:char) =
        Char.IsLetterOrDigit c || c = '.' || c = '-' || c = '_'

    /// For the text of a line till the caret:
    /// If the caret is in the package name of a  #r "nuget: ..."  line, so after  "nuget:  and before the first comma,
    /// returns the index where the package name starts, otherwise -1.
    /// For  #r "nuget: Include=Name  the name starts after the equal sign.
    let packageNameStart (lineToCaret:string) : int =
        let ln = lineToCaret
        let len = ln.Length
        let skipWhite (from:int) =
            let mutable j = from
            while j < len && Char.IsWhiteSpace ln[j] do
                j <- j + 1
            j
        let mutable i = skipWhite 0
        if String.CompareOrdinal(ln, i, "#r", 0, 2) <> 0 then
            -1
        else
            i <- i + 2
            while i < len && ln[i] = ' ' do
                i <- i + 1
            if i < len && ln[i] = '@' then // a verbatim string
                i <- i + 1
            if i >= len || ln[i] <> '"' || String.CompareOrdinal(ln, i + 1, "nuget:", 0, 6) <> 0 then
                -1
            else
                let mutable st = skipWhite (i + 7) // after "nuget:
                match ln.IndexOf('=', st) with
                | -1 -> ()
                | eq ->
                    if String.Equals(ln.Substring(st, eq - st).TrimEnd(), "Include", StringComparison.OrdinalIgnoreCase) then
                        st <- skipWhite (eq + 1)
                    else
                        st <- -1 // after another option, like Version=
                if st < 0 then
                    -1
                else
                    // only package id characters till the caret, so no comma, space or closing quote:
                    let mutable k = st
                    while k < len && isPackageIdChar ln[k] do
                        k <- k + 1
                    if k = len then st else -1

    /// The package names of all  #r "nuget: ..."  lines in this code, without duplicates.
    let namesInCode (code:string) : string[] =
        let names = ResizeArray<string>()
        let seen = HashSet<string>(StringComparer.OrdinalIgnoreCase)
        let mutable st = 0
        while st < code.Length do
            let en = match code.IndexOf('\n', st) with -1 -> code.Length | i -> i
            let mutable i = st
            while i < en && Char.IsWhiteSpace code[i] do
                i <- i + 1
            if i + 1 < en && code[i] = '#' && code[i+1] = 'r' then // fast check before parsing
                match tryParse (code.Substring(st, en - st)) with
                | Some d when seen.Add d.name -> names.Add d.name
                | _ -> ()
            st <- en + 1
        names.ToArray()


[<RequireQualifiedAccess>]
type NugetHintKind =
    /// The version is the latest one.
    | UpToDate
    /// A newer version exists.
    | UpdateTo
    /// A newer prerelease version exists.
    | UpdateToPrerelease
    /// Return from a prerelease to the latest stable version.
    | DowngradeToStable
    /// No version or a floating version is given.
    | PinTo
    /// The package was not found on any source.
    | NotFound
    /// The versions are being looked up, there is no result yet.
    | Checking
    /// Nothing to show, e.g. no source was reachable.
    | NoHint

/// The state of the version lookup of a package.
[<RequireQualifiedAccess>]
type NugetLookupState =
    /// There is a recent result.
    | Current
    /// There is no result yet, or it is outdated, and no lookup is running.
    | NeedsLookup
    /// A lookup is running.
    | Running

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
    /// packageName: The original casing of the package id from its metadata, or "" if not found.
    /// projectUrl: The project website of the latest version that has one, or "".
    /// checkedOn: The names of the sources that answered.
    /// notReachable: The names of the sources that failed, or "".
    | Found  of versions:NuGetVersion[] * packageName:string * projectUrl:string * checkedOn:string * notReachable:string
    | Failed of reason:string

/// A package found by a search on nuget.org.
type NugetSearchResult = {
    /// The package id.
    id: string

    /// The latest version, including prereleases.
    version: string

    /// The description from the package metadata, or "".
    description: string

    /// The total download count.
    downloads: int64
    }


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

    /// For searching package ids, independent of the NuGet.Config files.
    static let nugetOrg = lazy (Repository.Factory.GetCoreV3 "https://api.nuget.org/v3/index.json")

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
                                    let found = metas |> Seq.map (fun m -> struct(m.Identity.Version, m.Identity.Id, m.ProjectUrl)) |> Seq.toArray
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
                    let all = // prefer the newest stable metadata, then the newest prerelease
                        answered
                        |> Array.collect snd
                        |> Array.sortWith (fun struct(a, _, _) struct(b, _, _) ->
                            match compare a.IsPrerelease b.IsPrerelease with
                            | 0 -> VersionComparer.Default.Compare(b, a)
                            | c -> c)
                    let versions =
                        all
                        |> Array.map (fun struct(v, _, _) -> v)
                        |> Array.distinctBy (fun v -> v.ToNormalizedString().ToLowerInvariant())
                        |> Array.sortWith (fun a b -> VersionComparer.Default.Compare(a, b))
                    let projectUrl = // from the latest stable version that has one, else from the latest prerelease
                        all
                        |> Array.tryFind (fun struct(_, _, u) -> isWebUrl u)
                        |> Option.map (fun struct(_, _, u) -> u.AbsoluteUri)
                        |> Option.defaultValue ""
                    let packageName =
                        all
                        |> Array.tryHead
                        |> Option.map (fun struct(_, name, _) -> name)
                        |> Option.defaultValue ""
                    let checkedOn = answered |> Array.map fst |> String.concat ", "
                    return NugetLookup.Found (versions, packageName, projectUrl, checkedOn, String.Join(", ", failed))
        }

    /// Search nuget.org for packages, including prereleases, the most relevant first.
    static member Search(query:string, take:int) : Async<NugetSearchResult[]> =
        async {
            use cts = new CancellationTokenSource(TimeSpan.FromSeconds 10.0)
            let! res = nugetOrg.Value.GetResourceAsync<PackageSearchResource>(cts.Token) |> Async.AwaitTask
            let! found = res.SearchAsync(query, SearchFilter(true), 0, take, NullLogger.Instance, cts.Token) |> Async.AwaitTask // true: includePrerelease
            return
                [| for m in found do
                    {
                    id          = m.Identity.Id
                    version     = m.Identity.Version.ToNormalizedString()
                    description = if isNull m.Description then "" else m.Description.Trim()
                    downloads   = if m.DownloadCount.HasValue then m.DownloadCount.Value else 0L
                    } |]
        }


/// A cache of the versions of all packages used in #r "nuget: ..." lines, shared by all editor tabs.
type NugetVersions private () =

    /// The completed lookups and when they finished.
    static let results  = ConcurrentDictionary<string, struct(NugetLookup * DateTime)>()

    /// The running lookups, so that each package is only looked up once at a time.
    static let inFlight = ConcurrentDictionary<string, Lazy<Task<NugetLookup>>>()

    static let mutable loadError : exn option = None

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

    static let makeHints (d:NugetDirective) (res:NugetLookup) : NugetHint list =
        match res with
        | NugetLookup.Failed _ ->
            []

        | NugetLookup.Found (versions, _, projectUrl, checkedOn, notReachable) ->
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
                [hint NugetHintKind.NotFound "" $"Package '{d.name}' was not found.\r\n{sources}"]

            | Some latestAny, latestStable ->
                let latestStableOrAny = defaultArg latestStable latestAny
                if d.version = "" then
                    let v = str latestStableOrAny
                    [
                        hint NugetHintKind.PinTo v $"Click to pin {d.name} to its latest version {v}.\r\n{sources}"
                        if latestAny.IsPrerelease && VersionComparer.Default.Compare(latestAny, latestStableOrAny) > 0 then
                            let p = str latestAny
                            hint NugetHintKind.UpdateToPrerelease p $"Click to pin {d.name} to its latest prerelease {p}.\r\n{sources}"
                    ]
                else
                    let mutable current : NuGetVersion = null
                    let mutable range   : VersionRange = null
                    if NuGetVersion.TryParse(d.version, &current) then
                        let target = if current.IsPrerelease then latestAny else latestStableOrAny
                        let change kind (target:NuGetVersion) action =
                            let t = str target
                            hint kind t $"Click to {action} {d.name} from {d.version} to {t}.\r\n{sources}\r\n\r\nIf version {d.version} is already loaded in the running FSI session,\r\nFSI needs a reset to use version {t}."
                        [
                            if VersionComparer.Default.Compare(current, target) >= 0 then
                                hint NugetHintKind.UpToDate d.version $"{d.name} {d.version} is the latest version.\r\n{sources}"
                            else
                                let kind = if target.IsPrerelease then NugetHintKind.UpdateToPrerelease else NugetHintKind.UpdateTo
                                change kind target "update"

                            if current.IsPrerelease then
                                match latestStable with
                                | Some stable when VersionComparer.Default.Compare(stable, current) < 0 ->
                                    change NugetHintKind.DowngradeToStable stable "downgrade to stable"
                                | Some stable when VersionComparer.Default.Compare(stable, target) <> 0 ->
                                    change NugetHintKind.UpdateTo stable "update to stable"
                                | _ -> ()
                            elif latestAny.IsPrerelease && VersionComparer.Default.Compare(latestAny, current) > 0 && VersionComparer.Default.Compare(latestAny, target) <> 0 then
                                change NugetHintKind.UpdateToPrerelease latestAny "update to prerelease"
                        ]

                    elif VersionRange.TryParse(d.version, true, &range) && range.IsFloating then // like *, 1.* or *-*
                        match range.FindBestMatch(versions) with
                        | null -> []
                        | best ->
                            let b = str best
                            [hint NugetHintKind.PinTo b $"Click to pin {d.name} to {b}.\r\n'{d.version}' currently resolves to {b}.\r\n{sources}"]
                    else
                        [] // a version range without floating, or an invalid version

    /// Set if the NuGet client assemblies could not be loaded.
    static member LoadError = loadError

    /// The original package id casing from a completed lookup, otherwise the name as written.
    /// Only accept casing changes, never a different package id.
    static member GetPackageName(folder:string, name:string) : string =
        match results.TryGetValue(keyOf folder name) with
        | true, struct(NugetLookup.Found (_, packageName, _, _, _), _)
            when String.Equals(name, packageName, StringComparison.OrdinalIgnoreCase) -> packageName
        | _ -> name

    /// Returns the hints to show for this line and the state of its lookup.
    /// While a lookup runs and there is no successful result yet, the hint is NugetHintKind.Checking.
    /// Never blocks and does not start a lookup itself, call Fetch for that.
    static member TryGetHints(folder:string, d:NugetDirective) : struct(NugetHint list * NugetLookupState) =
        let key = keyOf folder d.name
        let checking () = [{ kind = NugetHintKind.Checking; version = ""; tip = $"Looking up the versions of {d.name} ..."; projectUrl = "" }]
        match results.TryGetValue key with
        | true, entry ->
            let struct(res, _) = entry
            let state =
                if not (isStale entry)      then NugetLookupState.Current
                elif inFlight.ContainsKey key then NugetLookupState.Running
                else                             NugetLookupState.NeedsLookup
            match res with
            | NugetLookup.Failed _ when state = NugetLookupState.Running -> struct(checking(), state) // retrying, e.g. after being offline
            | _ -> struct(makeHints d res, state)
        | _ ->
            if inFlight.ContainsKey key then struct(checking(), NugetLookupState.Running)
            else                             struct([], NugetLookupState.NeedsLookup)

    /// Starts looking up the versions of the given packages right away, unless there is a recent result already.
    /// The returned Async completes when all of these lookups are done, also the ones that were already running.
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


/// Searches nuget.org for package ids, for the completion list after  #r "nuget:
/// The results are cached, shared by all editor tabs.
type NugetSearch private () =

    /// The completed searches and when they finished, by the lowercase query.
    static let results  = ConcurrentDictionary<string, struct(NugetSearchResult[] * DateTime)>()

    /// The running searches, so that each query is only searched once at a time.
    static let inFlight = ConcurrentDictionary<string, Lazy<Task<NugetSearchResult[]>>>()

    /// When the last search failed, to not try again for a while when offline.
    static let mutable lastFailure = DateTime.MinValue

    /// Set when the NuGet client assemblies can't be loaded, e.g. because of version conflicts in a hosting app.
    static let mutable disabled = false

    static let startSearch (key:string) (query:string) : Task<NugetSearchResult[]> =
        async {
            let! res =
                async {
                    try
                        let! found = NugetFeeds.Search(query, 20)
                        results[key] <- struct(found, DateTime.UtcNow)
                        return found
                    with e ->
                        if NugetLoad.isLoadError e then
                            if not disabled then
                                disabled <- true
                                IFeshLog.log.PrintfnAppErrorMsg "The search on nuget.org for the completions in #r \"nuget: ...\" lines is turned off because the NuGet client libraries could not be loaded:\r\n%s" e.Message
                        else
                            lastFailure <- DateTime.UtcNow
                        return [||]
                }
            inFlight.TryRemove(key) |> ignore
            return res
        }
        |> Async.StartAsTask

    /// The minimum length of the typed package name to start a search on nuget.org.
    static member MinQueryLength = 3

    /// Returns the packages found on nuget.org for this query, the most relevant first.
    /// The result is empty if nuget.org can't be reached, then no new search is started for one minute.
    /// The results are cached for ten minutes.
    static member Search(query:string) : Task<NugetSearchResult[]> =
        let key = query.ToLowerInvariant()
        match results.TryGetValue key with
        | true, struct(found, time) when DateTime.UtcNow - time < TimeSpan.FromMinutes 10.0 ->
            Task.FromResult found
        | _ ->
            if disabled || DateTime.UtcNow - lastFailure < TimeSpan.FromMinutes 1.0 then
                Task.FromResult [||]
            else
                inFlight.GetOrAdd(key, fun k -> lazy (startSearch k query)).Value

    /// Like Search, but waits at most maxWait for the result, then returns an empty array.
    /// Also returns an empty array right away if the query is shorter than MinQueryLength.
    static member SearchWithin(query:string, maxWait:TimeSpan) : Async<NugetSearchResult[]> =
        async {
            if query.Length < NugetSearch.MinQueryLength then
                return [||]
            else
                let t = NugetSearch.Search query
                if not t.IsCompleted then
                    do! Task.WhenAny(t :> Task, Task.Delay maxWait) |> Async.AwaitTask |> Async.Ignore
                return if t.Status = TaskStatus.RanToCompletion then t.Result else [||]
        }
