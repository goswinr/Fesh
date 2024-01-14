namespace Seff.Util

open System

[<AutoOpen>]
module AutoOpenDateTime = 

    type DateTime with
        /// yyyy-MM-dd_HH-mm-ss
        static member nowStr      = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")
        /// yyyy-MM-dd_HH-mm-ss.FFF
        static member nowStrMilli = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss.FFF")
        /// yyyy-MM-dd
        static member todayStr    = System.DateTime.Now.ToString("yyyy-MM-dd")
        // month
        static member log()       = System.DateTime.Now.ToString("yyyy-MM")


/// Utility functions for System.Windows.Media.Pen
module Pen = 
    open  System.Windows.Media

    /// To make it thread safe and faster
    let freeze(br:Pen)= 
        if br.IsFrozen then
            ()
        else
            if br.CanFreeze then
                br.Freeze()
            else
               eprintfn "Could not freeze Pen: %A" br
        br


module General = 
    open System


    let inline isTrue (nb:Nullable<bool>) = nb.HasValue && nb.Value

    let inline notNull x = match x with null -> false | _ -> true


    /// get folder location of Executing Assembly
    let assemblyLocation() = 
        IO.Path.GetDirectoryName(Reflection.Assembly.GetExecutingAssembly().Location)

    /// splits a file path into an array:
    /// like: [| "C:\" ; "folder1" ; "folder2" ; "file.ext" |]
    let pathParts (f:IO.FileInfo) = 
        let rec getParent (d:IO.DirectoryInfo) ps = 
            if isNull d then ps
            else getParent d.Parent (d.Name :: ps)

        getParent f.Directory [f.Name]
        |> List.toArray

    /// Post to this agent for writing a debug string to a desktop file. Only used for bugs that can't be logged to the UI.
    let LogFile = // for async debug logging to a file (if the Log window fails to show)
        let file = IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Seff-Log.txt")
        MailboxProcessor.Start(
            fun inbox ->
                let rec loop () = 
                    async { let! msg = inbox.Receive()
                            IO.File.AppendAllText(file, Environment.NewLine + msg)
                            return! loop()}
                loop() )

    let inline sortInPlaceBy<'T, 'Key when 'Key : comparison>  (projection : 'T -> 'Key) (rarr : ResizeArray<'T>) = 
        rarr.Sort (fun x y -> compare (projection x) (projection y))

    /// Returns the index of the item found.
    /// The compare function shall return 
    /// +1 when the first value is bigger than the second one 
    /// 0 for equality
    /// -1 when the first value is smaller than the second one 
    let inline tryBinarySearchWith comparer (value: 'T) (rarr : ResizeArray<'T>) =
        let rec loop lo hi =
            if lo > hi then None
            else
                let mid = lo + (hi - lo) / 2
                match sign <| comparer value rarr.[mid] with
                | 0 -> Some mid
                | 1 -> loop (mid + 1) hi
                | _ -> loop lo (mid - 1)

        loop 0 (rarr.Count - 1)


    /// test for structural equality
    let inline areSameBy (f: 'T -> 'U) (a:ResizeArray<'T>) (b:ResizeArray<'T>) =
        if Object.ReferenceEquals(a,b) then 
            true
        else
            let len = a.Count
            if len <> b.Count then false
            else
                let rec loop i =
                    if i=len then 
                        true
                    elif f a.[i] = f b.[i] then
                        loop (i+1)
                    else
                        false // exited early                
                loop 0

       

    // for pipelining several functions like traverse
    //let ifTrueDo func predicate resizeArray condition : bool =        if condition then func predicate resizeArray else false



/// operations on Strings
module Str  = 
    open System.Text.RegularExpressions
    open AvalonEditB.Document

    /// poor man's name parsing: returns the offset from end of string to last non alphanumeric or '_' character, or # for compiler directives
    /// this is used to do code completion even if a few characters are typed already. to track back to the start of the item to complete.
    let lastNonFSharpNameCharPosition (s:string) = 
        let mutable p = s.Length-1
        if p = -1 then 0 // empty string
        //elif p = 0 then 0 // single char string //TODO this is wrong? why was it there?
        else
            let mutable i = 0
            let mutable ch = s.[p]
            while p >= 0 && (Char.IsLetterOrDigit ch || ch = '_' || ch = '#' ) do // valid chars in F# names, # for compiler directives
                i <- i+1
                p <- p-1
                if p >=0 then ch <- s.[p]
            i

    /// test if char is FSharp operator, includes '~'
    let isOperator (c:Char)= 
        // https://docs.microsoft.com/en-us/dotnet/fsharp/language-reference/operator-overloading
        '!'=c || '%'=c || '&'=c || '*'=c || '+'=c || '-'=c || '.'=c || '|'=c ||
        '/'=c || '<'=c || '='=c || '>'=c || '?'=c || '@'=c || '^'=c || '~'=c
        
    /// first letter uppercase 
    let up1 (s:String)  = 
        if s="" then s else Char.ToUpper(s.[0]).ToString() + s.Substring(1)
    
    /// first letter lowercase
    let low1 (s:String) = 
        if s="" then s else Char.ToLower(s.[0]).ToString() + s.Substring(1)

    
    /// ensures all lines end on Environment.NewLine
    let unifyLineEndings (s:string) = 
        //Text.StringBuilder(s).Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine).ToString()
        //Regex.Replace(s, @"\r\n|\n\r|\n|\r", Environment.NewLine) //https://stackoverflow.com/questions/140926/normalize-newlines-in-c-sharp
        //https://learn.microsoft.com/en-us/dotnet/standard/base-types/best-practices#interpreted-vs-compiled-regular-expressions
        Regex.Replace(s, @"\r\n|\n|\r", Environment.NewLine) // simplified from https://stackoverflow.com/questions/140926/normalize-newlines-in-c-sharp

    let tabsToSpaces spaces (s:string) = 
        s.Replace("\t", String(' ',spaces))

    let inline trim  (s:string) = s.Trim()

    ///s.Replace(toReplace, replacement)
    let inline replace (toReplace:string) (replacement:string) (s:string)  = 
        s.Replace(toReplace, replacement)

    /// checks if stringToFind is part of stringToSearchIn
    let inline contains (stringToFind:string) (stringToSearchIn:string) = 
        stringToSearchIn.Contains(stringToFind)

    /// checks if stringToFind is NOT part of stringToSearchIn
    let inline notContains (stringToFind:string) (stringToSearchIn:string) = 
        not (stringToSearchIn.Contains(stringToFind))

    /// checks if charToFind is part of stringToSearchIn
    let inline containsChar (charToFind:char) (stringToSearchIn:string) = 
        stringToSearchIn.IndexOf(charToFind) <> -1

    /// checks if stringToFind is NOT part of stringToSearchIn
    let inline notContainsChar (charToFind:string) (stringToSearchIn:string) = 
        stringToSearchIn.IndexOf(charToFind) = -1

    /// Returns true if the last character of the string is equal to given char,
    /// false on null or empty string
    let lastCharIs char (s:string)= 
        if isNull s then false
        elif s = "" then false
        else char = s.[s.Length-1]

    /// Counts spaces at start of string
    /// Returns 0 on empty string
    let inline spacesAtStart (str:string) = 
        let mutable i = 0
        while i < str.Length && str.[i] = ' ' do
            i <- i + 1
        i

    /// Counts spaces after a position
    /// Returns 0 if none string
    let inline spacesAtOffset off (str:string) = 
        let mutable i = off
        while i < str.Length && str.[i] = ' ' do
            i <- i + 1
        i - off


    /// backtrack till non Whitespace
    /// Returns new offset
    let inline findBackNonWhiteFrom off (str:string) = 
        let mutable i = off
        while i > -1 && Char.IsWhiteSpace(str,i) do
            i <- i - 1
        i

    /// checks if a string is just space characters or Empty string
    let inline isJustSpaceCharsOrEmpty (str:string) = 
        let mutable isSpace = true
        let mutable i = 0
        while isSpace && i < str.Length do
            isSpace <- str.[i] = ' '
            i <- i + 1
        isSpace


    /// counts how many time a substring occurs in a string
    let inline countSubString (sub:string) (s:string) = 
        let mutable k =  0
        let mutable i = s.IndexOf(sub, StringComparison.Ordinal)
        while i >= 0 do
            k <- k + 1
            i <- s.IndexOf(sub,i + sub.Length, StringComparison.Ordinal)
        k

    /// counts how many time a character occurs in a string
    let inline countChar (c:char) (s:string) = 
        let mutable k =  0
        let mutable i = s.IndexOf(c)
        while i >= 0 do
            k <- k + 1
            i <- s.IndexOf(c,i + 1)
        k

    /// Returns the remainder after the last substring found
    let stringAfterLast (sub:string) (s:string) = 
        match s.LastIndexOf(sub, StringComparison.Ordinal) with
        | -1 -> None
        | i  -> Some (s.Substring(i + sub.Length))
        

    
    /// returns the index of the first non white char from start index
    /// if not found returns fromIdx-1
    let inline indexOfFirstNonWhiteAfter fromIdx (s:string) = 
        let mutable loop = true
        let mutable i = fromIdx-1
        let lasti = s.Length-1
        while loop && i < lasti do
            i <- i + 1
            loop <- s.[i] = ' '
        i

    /// split string into two elements,
    /// splitter is not included in the two return strings.
    /// if splitter not found first string is same as input, second string is empty
    let splitOnce (splitter:string) (s:string) = 
        let start = s.IndexOf(splitter, StringComparison.Ordinal) //TODO replace with name and implementation from FsEX
        if start = -1 then s,""
        else               s.Substring(0, start), s.Substring(start + splitter.Length)

    /// finds text between two strings
    /// between "X" "T" "cXabTk" = "c", "ab", "k"
    /// delimiters are excluded
    /// if not both splitters are found returns original string and two empty strings
    /// previously called between, but now with new return value on fail
    let splitTwice (startChar:string) (endChar:string) (s:string) = 
        let start = s.IndexOf(startChar, StringComparison.Ordinal)
        if start = -1 then s,"",""
        else
            let ende = s.IndexOf(endChar, start + startChar.Length, StringComparison.Ordinal)
            if ende = -1 then s,"",""
            else
                s.Substring(0, start ),
                s.Substring(start + startChar.Length, ende - start - startChar.Length),// finds text between two chars
                s.Substring(ende + endChar.Length)

    /// finds text between two strings
    /// delimiters are excluded
    let between (startChar:string) (endChar:string) (s:string) = 
        let start = s.IndexOf(startChar, StringComparison.Ordinal)
        if start = -1 then None
        else
            let ende = s.IndexOf(endChar, start + startChar.Length, StringComparison.Ordinal)
            if ende = -1 then None
            else Some <| s.Substring(start + startChar.Length, ende - start - startChar.Length)

    //
    let inline countCharI (c:char) (s:ITextSource) = 
        let len = s.TextLength
        let mutable k =  0
        let mutable i = s.IndexOf(c,0,len)
        while i >= 0 do
            k <- k + 1
            let searchFrom = i + 1
            i <- s.IndexOf(c, searchFrom, len-searchFrom)
        k

    /// finds text after a given string
    /// delimiters is excluded
    let after (splitter:string) (s:string) = 
        let start = s.IndexOf(splitter, StringComparison.Ordinal)
        if start = -1 then None
        else  Some <| s.Substring(start + splitter.Length)

    /// reduce string if longer than max, add suffix if trimmed
    let shrink (max:int) (suffix:string) (s:string) = 
        if s.Length <= max then s
        else
            s.Substring(0,max) + suffix


    /// remove amount of characters from end of string
    /// if count is bigger than string returns empty string
    let removeAtEnd (count:int)  (s:string) = 
        if s.Length <= count then ""
        else s.Substring(0,s.Length-count)


/// For searching in string but skipping over everything that is in double quotes.
/// Also skips over escaped double quotes \"
[<RequireQualifiedAccess>]
module NotInQuotes = 
    // tested OK !
    
    /// find the end index of a string jumping over escaped quotes via \"
    let internal getStringEnd fromIdx (txt:string)= 
        let rec loop from =         
            if from = txt.Length then -1 
            else
                match txt.IndexOf('"',from) with 
                | -1 -> -1
                | i -> 
                    if i = 0 then 0
                    elif txt.[i-1] = '\\' then loop (i+1)
                    else i
        loop fromIdx

    
    /// index of a sub string in a string  but ignore everything that is between double quotes(skipping escaped quotes)
    let indexOf (find:string) (txt:string)= 
        let rec loop fromIdx =         
            if fromIdx = txt.Length then -1 
            else 
                match txt.IndexOf(find,fromIdx,StringComparison.Ordinal) with 
                | -1 -> -1
                | fi -> 
                    match txt.IndexOf('"',fromIdx) with 
                    | -1 -> fi
                    | qsi -> 
                        if qsi > fi then 
                            fi 
                        else
                            //get quote end 
                            match getStringEnd (qsi+1) txt with 
                            | -1  -> -1 // string is not closed
                            | qei -> loop (qei+1) 
        loop 0    
    
    /// test if a string contains a string but ignore everything that is between double quotes(skipping escaped quotes)
    let contains (find:string) (txt:string)= 
        indexOf find txt > -1 
        
    /// check if the last character is not in a string literal (= inside quotes)
    let isLastCharOutsideQuotes (txt:string)  =  
        let rec loop fromIdx =
            if fromIdx = txt.Length then true 
            else 
                match txt.IndexOf('"',fromIdx) with 
                | -1 -> true
                | s ->  
                    match getStringEnd (s+1)  txt with 
                    | -1 -> false
                    | e ->  loop (e+1)
        loop 0
        
    
    /// find the end index of a string jumping over escaped quotes via \"
    let internal getStringStartBack fromIdx (txt:string)= 
        let rec loop (from) =         
            if from = -1 then -1 
            else 
                match txt.LastIndexOf('"',from) with 
                | -1 -> -1
                | i -> 
                    if i = 0 then 0
                    elif txt.[i-1] = '\\' then loop (i-1)
                    else i
        loop fromIdx
    
    /// for starting to search from outside quotes. 
    /// test if a string contains a string from the end 
    /// but ignore everything that is between double quotes(skipping escaped quotes).
    /// before calling this make sure isLastCharOutsideQuotes is true
    let lastIndexOfFromOutside (find:string) (txt:string)= 
        //printf $"find '{find}' in '{txt}'"
        let rec loop fromIdx =         
            if fromIdx = -1 then -1 
            else 
                match txt.LastIndexOf(find, fromIdx, StringComparison.Ordinal) with 
                | -1 -> -1
                | fi -> 
                    match txt.LastIndexOf('"', fromIdx) with 
                    | -1 -> fi
                    | qei -> 
                        if qei< fi then // but result was after string anyways
                            fi
                        else
                            match getStringStartBack (qei-1)  txt with 
                            | -1  -> -1 //should not happen // no start of string found,  search started inside a string 
                            | qsi -> loop(qsi-1) // string had a start 
                                    
        let r = loop (txt.Length-1) 
        //printfn $" = {r}"
        r
    
    /// for starting to search from inside quotes.    
    /// test if a string contains a string from the end 
    /// but ignore everything that is between double quotes(skipping escaped quotes)
    /// before calling this make sure isLastCharOutsideQuotes is false
    let lastIndexOfFromInside (find:string) (txt:string)= 
        let rec loop fromIdx =         
            if fromIdx = -1 then -1 
            else 
                match txt.LastIndexOf(find, fromIdx,StringComparison.Ordinal) with 
                | -1 -> -1
                | fi -> 
                    match txt.LastIndexOf('"', fromIdx) with 
                    | -1 -> fi
                    | qei -> 
                        if qei< fi then // but result was after string anyways
                            fi
                        else
                            match getStringStartBack (qei-1)  txt with 
                            | -1  -> -1 //should not happen // no start of string found,  search started inside a string 
                            | qsi -> loop(qsi-1) // string had a start
        
        match getStringStartBack (txt.Length-1)  txt with 
        | -1  -> -1 //should not happen
        | q -> loop (q-1) 
        
        
    /// test if a string contains a string from the end 
    /// but ignore everything that is between double quotes(skipping escaped quotes)
    let lastIndexOf (find:string) (txt:string)=  
        if isLastCharOutsideQuotes txt then lastIndexOfFromOutside find txt
        else                                lastIndexOfFromInside  find txt


module Monads =  

    /// The maybe monad.
    type MaybeBuilder() = 
        // from https://github.com/fsprojects/FSharpx.Extras/blob/master/src/FSharpx.Extras/ComputationExpressions/Option.fs
        // This monad is my own and uses an 'T option. Others generally make their own Maybe<'T> type from Option<'T>.
        // The builder approach is from Matthew Podwysocki's excellent Creating Extended Builders series
        // http://codebetter.com/blogs/matthew.podwysocki/archive/2010/01/18/much-ado-about-monads-creating-extended-builders.aspx.

        member inline this.Return(x) = Some x

        member inline this.ReturnFrom(m: option<'T>) = m

        member inline this.Bind(m, f) = Option.bind f m

        member inline this.Zero() = None

        member inline this.Combine(m, f) = Option.bind f m

        member inline this.Delay(f: unit -> _) = f

        member inline this.Run(f) = f()

        member inline this.TryWith(m, h) = 
            try this.ReturnFrom(m)
            with e -> h e

        member inline  this.TryFinally(m, compensation) = 
            try this.ReturnFrom(m)
            finally compensation()

        member inline this.Using(res:#IDisposable, body) = 
            this.TryFinally(body res, fun () -> match res with null -> () | disp -> disp.Dispose())

        member this.While(guard, f) = 
            if not (guard()) then Some () else
            do f() |> ignore
            this.While(guard, f)

        member inline  this.For(sequence:seq<_>, body) = 
            this.Using(sequence.GetEnumerator(), fun enum -> this.While(enum.MoveNext, this.Delay(fun () -> body enum.Current))) 


    /// The maybe monad.for Option Types
    type ValueMaybeBuilder() = 
        // from https://github.com/fsprojects/FSharpx.Extras/blob/master/src/FSharpx.Extras/ComputationExpressions/Option.fs
        // This monad is my own and uses an 'T voption. Others generally make their own Maybe<'T> type from Option<'T>.
        // The builder approach is from Matthew Podwysocki's excellent Creating Extended Builders series
        // http://codebetter.com/blogs/matthew.podwysocki/archive/2010/01/18/much-ado-about-monads-creating-extended-builders.aspx.

        member inline this.Return(x) = ValueSome x

        member inline this.ReturnFrom(m: voption<'T>) = m

        member inline this.Bind(m, f) = ValueOption.bind f m

        member inline this.Zero() = ValueNone

        member inline this.Combine(m, f) = ValueOption.bind f m

        member inline this.Delay(f: unit -> _) = f

        member inline this.Run(f) = f()

        member inline this.TryWith(m, h) = 
            try this.ReturnFrom(m)
            with e -> h e

        member inline  this.TryFinally(m, compensation) = 
            try this.ReturnFrom(m)
            finally compensation()

        member inline this.Using(res:#IDisposable, body) = 
            this.TryFinally(body res, fun () -> match res with null -> () | disp -> disp.Dispose())

        member this.While(guard, f) = 
            if not (guard()) then ValueSome () else
            do f() |> ignore
            this.While(guard, f)

        member inline  this.For(sequence:seq<_>, body) = 
            this.Using(sequence.GetEnumerator(), fun enum -> this.While(enum.MoveNext, this.Delay(fun () -> body enum.Current))) 

    /// A maybe monad for value (struct) option types.
    let vmaybe = ValueMaybeBuilder()
    
    /// A maybe monad for option types.
    let maybe = MaybeBuilder()

    (*
    /// Generic monadic operators    
        
    module Operators =
        //https://github.com/fsprojects/FSharpx.Extras/blob/master/src/FSharpx.Extras/ComputationExpressions/Operators.fs


        /// Inject a value into the monadic type
        let inline returnM builder x = (^M: (member Return: 'b -> 'c) (builder, x))
        let inline bindM builder m f = (^M: (member Bind: 'd * ('e -> 'c) -> 'c) (builder, m, f))
        let inline liftM builder f m =
            let inline ret x = returnM builder (f x)
            bindM builder m ret

        /// Sequential application
        let inline applyM (builder1:^M1) (builder2:^M2) f m =
            bindM builder1 f <| fun f' ->
                bindM builder2 m <| fun m' ->
                    returnM builder2 (f' m') 
        
        /// Inject a value into the option type
        let inline returnMM x = returnM maybe x

        /// Sequentially compose two actions, passing any value produced by the first as an argument to the second.
        let inline ( >>= ) m f = bindM maybe m f

        /// Flipped >>=
        let inline (=<<) f m = bindM maybe m f

        /// Sequential application
        let inline (<*>) f m = applyM maybe maybe f m

        /// Sequential application
        let inline ap m f = f <*> m

        /// Infix map
        let inline (<!>) f m = Option.map f m

        /// Promote a function to a monad/applicative, scanning the monadic/applicative arguments from left to right.
        let inline lift2 f a b = returnMM f <*> a <*> b

        /// Sequence actions, discarding the value of the first argument.
        let inline ( *> ) x y = lift2 (fun _ z -> z) x y

        /// Sequence actions, discarding the value of the second argument.
        let inline ( <* ) x y = lift2 (fun z _ -> z) x y

        /// Sequentially compose two maybe actions, discarding any value produced by the first
        let inline (>>.) m f = bindM maybe m (fun _ -> f)

        /// Left-to-right Kleisli composition
        let inline (>=>) f g = fun x -> f x >>= g

         /// Transforms a function by flipping the order of its arguments.
        let inline flip f a b = f b a

        /// Right-to-left Kleisli composition
        let inline (<=<) x = flip (>=>) x

        /// Sequentially compose monadic and non monadic actions, 
        /// passing any value produced by the first as an argument to the second.
        /// Similar to >>= but for functions that always return a result (not a result option)
        let inline (|>>) m f = liftM maybe f m //match m with Some x -> Some (f x)  |None -> None
    *)



(*  module Extern = 
        open System.Runtime.InteropServices

        [<DllImport "user32.dll">]
        extern IntPtr FindWindow(string lpClassName,string lpWindowName)

        [<DllImport "user32.dll">]
        extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string lclassName , string windowTitle)

        [<DllImport("user32.dll", CharSet=CharSet.Auto)>]
        extern IntPtr SendMessage(IntPtr hWnd, uint32 Msg, IntPtr wParam , IntPtr lParam)

        /// installs a font from a filepath on this PC
        [<DllImport("gdi32.dll", EntryPoint="AddFontResourceW", SetLastError=true)>]
        extern int InstallFont([<In>][<MarshalAs(UnmanagedType.LPWStr)>]string fontFileName)

        let getChromeMainWindowUrl () = // use to scan for nuget(fuget) package and install it with paket
            let ps = Diagnostics.Process.GetProcessesByName "chrome"
            seq {
            for p in ps do
            let mainWnd = FindWindow("Chrome_WidgetWin_1", p.MainWindowTitle)
            let addrBar = FindWindowEx(mainWnd, 0n, "Chrome_OmniboxView", null)
            if addrBar <> 0n then
                let url = Marshal.AllocHGlobal 100
                let WM_GETTEXT = 0x000Du
                SendMessage (addrBar, WM_GETTEXT, 50n, url) |> ignore
                let url = Marshal.PtrToStringUni url
                yield url }
            |> Seq.head
*)

