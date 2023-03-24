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

    let rand = new Random() // to give each error checking call a unique id

    let inline isOdd  x = x % 2 = 1

    let inline isEven x = x % 2 = 0

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

    /// Post to this agent for writing a debug string to a desktop file. Only used for bugs that cant be logged to the UI.
    let LogFile = // for async debug logging to a file (if the Log window fails to show)
        let file = IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Seff-Log.txt")
        MailboxProcessor.Start(
            fun inbox ->
                let rec loop () = 
                    async { let! msg = inbox.Receive()
                            IO.File.AppendAllText(file, Environment.NewLine + msg)
                            return! loop()}
                loop() )

    let sortInPlaceBy<'T, 'Key when 'Key : comparison>  (projection : 'T -> 'Key) (rarr : ResizeArray<'T>) = 
        rarr.Sort (fun x y -> compare (projection x) (projection y))



/// operations on Strings
module Str  = 
    open System.Text.RegularExpressions

    /// first letter uppercase 
    let up1 (s:String)  = 
        if s="" then s else Char.ToUpper(s.[0]).ToString() + s.Substring(1)
    
    /// first letter lowercase
    let low1 (s:String) = 
        if s="" then s else Char.ToLower(s.[0]).ToString() + s.Substring(1)

    
    /// Trims strings to 80 chars for showing in one line.
    /// It returns the input string trimmed to 80 chars, a count of skipped characters and the last 5 characters
    /// Replace line breaks with '\r\n' or '\n' literal
    /// Does not include surrounding quotes
    /// If string is null returns "-null string-"
    let truncateFormattedInOneLine (stringToTrim:string) :string = 
        if isNull stringToTrim then "-null string-"
        else
            let s = 
                let maxChars = 80
                if stringToTrim.Length <= maxChars + 20 then  stringToTrim
                else
                    let len   = stringToTrim.Length
                    let st    = stringToTrim.Substring(0,maxChars)
                    let last5 = stringToTrim.Substring(len-6)
                    sprintf "%s[..%d more Chars..]%s" st (len - maxChars - 5) last5
            s.Replace("\r","\\r").Replace("\n","\\n")

    /// Trims strings ot maximum line count.
    /// Adds note about trimmed line count if there are more [ ... and %d more lines.]
    /// Does not include surrounding quotes
    /// If string is null returns "-null string-"
    let truncateToMaxLines (lineCount:int) (stringToTrim:string) :string = 
        if isNull stringToTrim then "-null string-"
        else
            let lns = stringToTrim.Split([|'\n'|],StringSplitOptions.None)
            let t = 
                lns
                |> Seq.truncate lineCount
                |> Seq.map ( fun l -> l.TrimEnd() )
                |> String.concat Environment.NewLine

            if lns.Length > lineCount then 
                sprintf "%s\%s[ ... and %d more lines.]" t Environment.NewLine (lns.Length - lineCount)
            else
                t


    // ensures all lines end on Environment.NewLine
    let unifyLineEndings (s:string) = 
        //Text.StringBuilder(s).Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine).ToString()
        //Regex.Replace(s, @"\r\n|\n\r|\n|\r", Environment.NewLine) //https://stackoverflow.com/questions/140926/normalize-newlines-in-c-sharp
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
         /// returns the remainder after the last substring found

    
    /// returns the index of the first non white char from start index
    /// if not found returns fromIdx-1
    let inline indexOfFirstNonWhiteAfter idx (s:string) = 
        let mutable loop = true
        let mutable i = idx-1
        while loop && i < s.Length do
            i <- i + 1
            loop <- s.[i]=' '
        i


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
                s.Substring(start + startChar.Length, ende - start - startChar.Length),// finds text betwween two chars
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


/// for searching in string but skipping over everything that is in double quotes.
/// also skips over escaped double quotes \"
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
        
    /// check if the last character is in a string literal (= inside quotes)
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
    /// before caling this make sure isLastCharOutsideQuotes is true
    let lastIndexOfFromOutside (find:string) (txt:string)= 
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
                                    
        loop (txt.Length-1) 
    
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

