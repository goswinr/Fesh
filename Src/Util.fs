namespace Seff.Util

open System
open System.IO
open System.Windows
open System.Windows.Media


[<AutoOpen>]
module Global =
    /// This ignore only work on Value types, 
    /// Objects and functions need to be ignored with 'ignoreObj'
    /// This is to prevent accidetially ignoring partially aplied functions that would return struct
    let inline ignore (x:'T when 'T: struct) = ()

    /// Ignores any object (and struct)
    /// For structs use 'ignore'
    let inline ignoreObj (x:obj) = ()

    let inline notNull x = match x with null -> false | _ -> true  //not (Object.ReferenceEquals(ob,null))
    
    /// returns maybeNullvalue if it is not null, else alternativeValue 
    let inline ifNull alternativeValue maybeNullvalue = match maybeNullvalue with null -> alternativeValue | _ -> maybeNullvalue  
    
    let inline isTrue (nb:Nullable<bool>) = nb.HasValue && nb.Value
    
    //let inline (|>!) a f = f a |> ignore ; a

    type DateTime with      
        /// yyyy-MM-dd_HH-mm-ss
        static member nowStr      = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")
        /// yyyy-MM-dd_HH-mm-ss.FFF
        static member nowStrMilli = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss.FFF")
        /// yyyy-MM-dd
        static member todayStr    = System.DateTime.Now.ToString("yyyy-MM-dd")


/// for SolidColorBrushes and other types from Windows.Media
module Media = 

   /// Adds bytes to each color channel to increase brightness, negative values to make darker
   /// result will be clamped between 0 and 255
   let changeLuminace (amount:int) (col:Windows.Media.Color)=
       let inline clamp x = if x<0 then 0uy elif x>255 then 255uy else byte(x)
       let r = int col.R + amount |> clamp      
       let g = int col.G + amount |> clamp
       let b = int col.B + amount |> clamp
       Color.FromArgb(col.A, r,g,b)
   
   /// Adds bytes to each color channel to increase brightness
   /// result will be clamped between 0 and 255
   let brighter (amount:int) (br:SolidColorBrush)  = SolidColorBrush(changeLuminace amount br.Color) 
   
   /// Removes bytes from each color channel to increase darkness, 
   /// result will be clamped between 0 and 255
   let darker  (amount:int) (br:SolidColorBrush)  = SolidColorBrush(changeLuminace -amount br.Color) 


   /// To make it thread safe and fatser
   let freeze(br:SolidColorBrush)= 
       if br.IsFrozen then
           ()
       else
           if br.CanFreeze then 
               br.Freeze()
           else 
              eprintfn "Could not freeze SolidColorBrush: %A" br         
       br

   /// To make it thread safe and fatser
   let freezePen(br:Pen)= 
       if br.IsFrozen then
           ()
       else
           if br.CanFreeze then 
               br.Freeze()
           else 
              eprintfn "Could not freeze SolidColorBrush: %A" br         
       br

module General = 

    let rand = new Random() // to give each error checking call a unique id
    
    let inline isOdd  x = x % 2 = 1
    
    let inline isEven x = x % 2 = 0
    
    
    /// get flolder location of Executing Assembly
    let assemblyLocation() = 
        IO.Path.GetDirectoryName(Reflection.Assembly.GetExecutingAssembly().Location)   

    /// splits a file path into an array:
    /// like: [| "C:\" ; "folder1" ; "folder2" ; "file.ext" |]
    let pathParts (f:FileInfo) =
        let rec getParent (d:DirectoryInfo) ps =
            if isNull d then ps
            else getParent d.Parent (d.Name :: ps)

        getParent f.Directory [f.Name]
        |> List.toArray

    /// Post to this agent for writing a debug string to a desktop file. Only used for bugs that cant be logged to the UI.
    let LogFile = // for async debug logging to a file (if the Log window fails to show)
        let file = IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),"Seff-Log.txt")
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

    // ensures all lines end on Environment.NewLine    
    let unifyLineEndings (s:string) =        
        //Text.StringBuilder(s).Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine).ToString()
        //Regex.Replace(s, @"\r\n|\n\r|\n|\r", Environment.NewLine) //https://stackoverflow.com/questions/140926/normalize-newlines-in-c-sharp
        Regex.Replace(s, @"\r\n|\n|\r", Environment.NewLine) // simlified from https://stackoverflow.com/questions/140926/normalize-newlines-in-c-sharp
        
    let tabsToSpaces spaces (s:string) = 
        s.Replace("\t", String(' ',spaces))    
    
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

    /// returns true if the last charcter of the string is equal to given char, 
    /// false on null or empty string
    let lastCharIs char (s:string)= 
        if isNull s then false
        elif s = "" then false
        else char = s.[s.Length-1]
    
    /// Counts spaces at start of string
    /// returns 0 on empty string
    let inline spacesAtStart (str:string) =        
        let mutable i = 0
        while i < str.Length && str.[i] = ' ' do
            i <- i + 1                       
        i
    
    /// Counts spaces after a position
    /// returns 0 if none string
    let inline spacesAtOffset off (str:string) =        
        let mutable i = off
        while i < str.Length && str.[i] = ' ' do
            i <- i + 1                       
        i - off   


    /// backtrack till non Whitspace
    /// returns new offset
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


    /// counts how many time a substring occures in a string 
    let inline countSubString (sub:string) (s:string) =
        let mutable k =  0
        let mutable i = s.IndexOf(sub, StringComparison.Ordinal)
        while i >= 0 do
            k <- k + 1
            i <- s.IndexOf(sub,i + sub.Length, StringComparison.Ordinal)
        k
    
    /// counts how many time a character occures in a string 
    let inline countChar (c:char) (s:string) =
        let mutable k =  0
        let mutable i = s.IndexOf(c)
        while i >= 0 do
            k <- k + 1
            i <- s.IndexOf(c,i + 1)
        k

    /// returns the remainder after the last substring found
    let stringAfterLast (sub:string) (s:string) =
        match s.LastIndexOf(sub, StringComparison.Ordinal) with
        | -1 -> None
        | i  -> Some (s.Substring(i + sub.Length))
         /// retursn the remainder after the last substring found
    
    /// test if the first Letter in a string is Uppercase, skipping Whitespace
    let startsWithUppercaseAfterWhitespace (s:string) =
        let mutable loop = true
        let mutable i = -1
        while loop && i < s.Length do
            i <- i + 1
            loop <- Char.IsWhiteSpace s.[i]            
        Char.IsUpper s.[i]
    
    /// poor man's name parsing: returns the offset from end of string to last non alphanumeric or '_' character
    let lastNonFSharpNameCharPosition (s:string) =        
        let mutable p = s.Length-1
        if p = -1 then 0 // empty string
        //elif p = 0 then 0 // single char string //TODO this is wrong? why was it there?
        else
            let mutable i = 0
            let mutable ch = s.[p]
            while p >= 0 && (Char.IsLetterOrDigit ch || ch = '_') do // valid chars in F# names                
                i <- i+1
                p <- p-1
                if p >=0 then ch <- s.[p]
            i

    /// test if char is Fharp opreator, includes '~'
    let isOperator (c:Char)=
        // https://docs.microsoft.com/en-us/dotnet/fsharp/language-reference/operator-overloading
        '!'=c || '%'=c || '&'=c || '*'=c || '+'=c || '-'=c || '.'=c || '|'=c ||
        '/'=c || '<'=c || '='=c || '>'=c || '?'=c || '@'=c || '^'=c || '~'=c
    
    /// split string into two elements, 
    /// splitter is not included in the two return strings.
    /// if splitter not found first string is same as input, second string is empty 
    let splitOnce (spliter:string) (s:string) =  
        let start = s.IndexOf(spliter, StringComparison.Ordinal) //TODO replace with name and implementation from FsEX
        if start = -1 then s,""
        else               s.Substring(0, start), s.Substring(start + spliter.Length)

    /// finds text betwween two strings
    /// between "X" "T" "cXabTk" = "c", "ab", "k"
    /// delimiters are excluded
    /// if not both splitters are found returns original string and two empty strings 
    /// previously called between, but now with new retuen value on fail
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

    /// finds text betwween two strings
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
    let after (spliter:string) (s:string) =  
        let start = s.IndexOf(spliter, StringComparison.Ordinal) 
        if start = -1 then None
        else  Some <| s.Substring(start + spliter.Length)
 
    /// reduce string if longer than max, add suffix if trimmed   
    let shrink (max:int) (suffix:string) (s:string) =  
        if s.Length <= max then s
        else
            s.Substring(0,max) + suffix


 

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

