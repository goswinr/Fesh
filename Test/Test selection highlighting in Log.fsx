#r "nuget: Fesher"

// Tests the highlighting of all occurrences of a selected word in the Log (SelectionHighlighterLog).
// Run this script inside Fesh, in the default async mode (not synchronous on the UI thread).
//
// It prints text with tricky line breaks to the Log, while a word is highlighted there.
// After each print it checks via reflection that the marks of the highlighter
// match all occurrences of the word in the Log text, and that each mark sits on the line it belongs to.
// (Before the LogLines rewrite, a lone \n or \r in the printed text made the marks land on wrong lines.)

open System
open System.Threading
open System.Reflection
open System.Diagnostics
open System.Collections
open Fesher

module Reflect =

    let flags = BindingFlags.Public ||| BindingFlags.NonPublic ||| BindingFlags.Instance

    let rec innerMost (e:exn) =
        match e with
        | :? TargetInvocationException as t when not (isNull t.InnerException) -> innerMost t.InnerException
        | e -> e

    /// Get a property or field value by name.
    let get (name:string) (o:obj) : obj =
        let t = o.GetType()
        match t.GetProperty(name, flags) with
        | null ->
            match t.GetField(name, flags) with
            | null -> failwithf "No property or field '%s' on %s" name t.FullName
            | f -> f.GetValue o
        | p -> p.GetValue o

    /// Call a method by name.
    let call (name:string) (args:obj[]) (o:obj) : obj =
        let types = args |> Array.map (fun a -> a.GetType())
        match o.GetType().GetMethod(name, flags, null, types, null) with
        | null -> failwithf "No method '%s' on %s" name (o.GetType().FullName)
        | m ->
            try m.Invoke(o, args)
            with e -> raise (innerMost e)

open Reflect


/// Access to the Log and its SelectionHighlighterLog in the running Fesh instance.
type FeshLog(feshAssembly:Assembly) =

    let logObj = feshAssembly.GetType("Fesh.Model.IFeshLogModule").GetProperty("log").GetValue(null)

    let editor = get "AvalonEditLog" logObj // the AvalonEditB TextEditor of the Log

    let highlighter =
        match get "SelectionHighlighter" logObj with
        | null -> failwith "Log.SelectionHighlighter is None"
        | opt  -> get "Value" opt

    /// The private LineTransformers<LinePartChange> of the highlighter, that the FastColorizer draws from.
    let transformers =
        highlighter.GetType().GetFields(BindingFlags.NonPublic ||| BindingFlags.Instance)
        |> Array.tryFind (fun f -> f.FieldType.Name.StartsWith "LineTransformers")
        |> function
            | Some f -> f.GetValue highlighter
            | None   -> failwith "No LineTransformers field found on SelectionHighlighterLog"

    let dispatcher = get "Dispatcher" editor

    let invoke = dispatcher.GetType().GetMethod("Invoke", [| typeof<Delegate>; typeof<obj[]> |])

    /// True if this script runs on the UI thread, then waiting for the async highlighting is not possible.
    member _.IsOnUIThread = call "CheckAccess" [||] dispatcher :?> bool

    /// Run a function on the UI thread and return its result.
    member _.OnUI (f: unit -> 'T) : 'T =
        try invoke.Invoke(dispatcher, [| box (Func<obj>(fun () -> box (f()))); box Array.empty<obj> |]) |> unbox<'T>
        with e -> raise (innerMost e)

    /// The same call the StatusBar does for a selection in the Editor.
    member this.MarkInLog(word:string) =
        this.OnUI (fun () -> call "MarkInLog" [| box word |] highlighter |> ignore)

    /// Compares the marks of the highlighter with all occurrences of the word in the Log text.
    /// Must run on the UI thread. Returns the count of occurrences or an error message.
    member _.Verify(word:string) : Result<int,string> =
        let doc  = get "Document" editor
        let text = get "Text" doc :?> string
        let expected = ResizeArray<int>()
        let mutable i = text.IndexOf(word, StringComparison.Ordinal)
        while i >= 0 do
            expected.Add i
            i <- text.IndexOf(word, i + word.Length, StringComparison.Ordinal)

        let found = (get "Offsets" highlighter :?> Generic.List<int>).ToArray()
        if (get "Word" highlighter :?> string) <> word then
            Error "the highlighter has a different word"
        elif found <> expected.ToArray() then
            Error (sprintf "the highlighter found %d occurrences, the Log text has %d" found.Length expected.Count)
        else
            // check that each mark is drawn on the line it belongs to:
            let lineCount = get "LineCount" doc :?> int
            let mutable marks = 0
            let mutable err = None
            for lineNo in 1 .. lineCount do
                let line = call "GetLineByNumber" [| box lineNo |] doc
                let st = get "Offset" line :?> int
                let en = get "EndOffset" line :?> int
                for m in call "GetLine" [| box lineNo |] transformers :?> IEnumerable do
                    marks <- marks + 1
                    let from = get "from" m :?> int
                    let till = get "till" m :?> int
                    if err.IsNone && (from < st || till > en) then
                        let belongsTo = call "GetLineByOffset" [| box from |] doc |> get "LineNumber"
                        err <- Some (sprintf "the mark at offset %d is drawn on line %d, but it is on line %O" from lineNo belongsTo)
            match err with
            | Some e -> Error e
            | None when marks <> expected.Count -> Error (sprintf "%d marks are on lines, expected %d" marks expected.Count)
            | None -> Ok expected.Count


let mutable passed = 0
let mutable failed = 0

/// Wait till the Log has printed everything. AvalonLog buffers prints for up to about 100ms.
let settle () = Thread.Sleep 400

/// Wait till the highlighter marks match the Log text, or fail after 3 seconds.
let check (log:FeshLog) (name:string) (word:string) =
    settle()
    let sw = Stopwatch.StartNew()
    let rec poll () =
        match log.OnUI (fun () -> log.Verify word) with
        | Error _ when sw.ElapsedMilliseconds < 3000L -> Thread.Sleep 20; poll()
        | res -> res
    match poll() with
    | Ok n ->
        passed <- passed + 1
        Printfn.green "PASS %-40s %5d found, settled after %d ms" name n sw.ElapsedMilliseconds
    | Error e ->
        failed <- failed + 1
        Printfn.red "FAIL %-40s %s" name e

/// Highlight a new word on the existing Log text and check it.
let markAndCheck (log:FeshLog) (name:string) (word:string) =
    settle()
    let sw = Stopwatch.StartNew()
    log.MarkInLog word
    check log name word
    Printfn.gray "     (%d ms including the 400 ms wait for the Log to settle)" sw.ElapsedMilliseconds


let run (log:FeshLog) =
    let w = "zebra"

    clearFeshLog()
    markAndCheck log "empty Log" w // from here on the word stays highlighted, every print re-marks it

    Printfn.gray "zebra at line start"
    Printfn.gray "two zebra zebra on one line"
    Printfn.blue "no match here"
    Printf.red "mixed "
    Printf.green "colors "
    Printfn.blue "zebra"
    check log "plain lines" w

    Printfn.gray "lf zebra\nlf zebra\n\nlf zebra"
    check log "lone \\n line breaks" w

    Printfn.gray "cr zebra\rcr zebra\r\rcr zebra"
    check log "lone \\r line breaks" w

    Printf.gray "split crlf zebra\r"
    settle() // to get two separate appends to the Log document
    Printfn.gray "\nafter split crlf zebra"
    check log "\\r\\n split over two appends" w

    Printf.gray "split word ze"
    settle()
    Printfn.gray "bra"
    check log "word split over two appends" w

    Printf.gray "no line break at the end zebra"
    check log "last line without line break" w
    Printfn.gray ""

    for i in 1 .. 3000 do
        if i % 7 = 0 then Printfn.gray "line %d zebra" i
        else              Printfn.gray "line %d" i
    check log "3000 lines in a burst" w

    for i in 1 .. 40 do
        Printfn.gray "trickle %d zebra" i
        Thread.Sleep 25
    check log "trickle while highlighted" w

    clearFeshLog()
    Printfn.gray "after clear zebra"
    Printfn.gray "zebra again"
    check log "clear, then print" w

    let pad = String('.', 50)
    for i in 1 .. 20_000 do
        if   i % 10 = 0 then Printfn.gray "%6d %s zebra" i pad
        elif i % 13 = 0 then Printfn.gray "%6d %s giraffe" i pad
        else                 Printfn.gray "%6d %s" i pad
    check log "20000 long lines" w
    markAndCheck log "new word on 20000 long lines" "giraffe"

    if failed = 0 then Printfn.green "All %d checks passed." passed
    else               Printfn.red   "%d of %d checks failed." failed (passed + failed)
    Printfn.gray "For a visual check: select a word in the Log or in this script, all its occurrences in the Log should be highlighted."


match AppDomain.CurrentDomain.GetAssemblies() |> Array.tryFind (fun a -> a.GetName().Name = "Fesh") with
| None -> printfn "Run this script inside the Fesh editor."
| Some fesh ->
    let log = FeshLog(fesh)
    if log.IsOnUIThread then
        Printfn.red "This script needs to run in async mode, not synchronous on the UI thread. Switch it in the FSI menu."
    else
        run log
