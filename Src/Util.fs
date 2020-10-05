namespace Seff.Util

open System
open System.Windows.Media
open System.IO


module General =    

    (*
    type Time()=
        //static member nowStrMenu  = System.DateTime.Now.ToString("yyyy-MM-dd  HH:mm")
        static member nowStr      = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")
        static member nowStrMilli = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss.FFF")
        static member todayStr    = System.DateTime.Now.ToString("yyyy-MM-dd")
    *)

    let rand = new Random() // to give each error checking call a unique id

    let inline notNull x = match x with null -> false | _ -> true  //not (Object.ReferenceEquals(ob,null))
    
    let inline isTrue (nb:Nullable<bool>) = nb.HasValue && nb.Value

    let inline isOdd  x = x % 2 = 1
    
    let inline isEven x = x % 2 = 0

    let inline (|>>) a f = f a |> ignore ; a
    
    let assemblyLocation() = IO.Path.GetDirectoryName(Reflection.Assembly.GetExecutingAssembly().Location)

    ///------color adjusting -----

    ///Adds bytes to each color channel to increase brightness, negative values to make darker
    /// result will be clamped between 0 and 255
    let changeLuminace (amount:int) (col:Color)=
        let inline clamp x = if x<0 then 0uy elif x>255 then 255uy else byte(x)
        let r = int col.R + amount |> clamp      
        let g = int col.G + amount |> clamp
        let b = int col.B + amount |> clamp
        Color.FromArgb(col.A, r,g,b)
    
    ///Adds bytes to each color channel to increase brightness
    /// result will be clamped between 0 and 255
    let brighter (amount:int) (br:SolidColorBrush)  = SolidColorBrush(changeLuminace amount br.Color) 
    
    ///Removes bytes from each color channel to increase darkness, 
    /// result will be clamped between 0 and 255
    let darker  (amount:int) (br:SolidColorBrush)  = SolidColorBrush(changeLuminace -amount br.Color) 

    /// splits a file path into an array:
    /// like: [| "C:\" ; "folder1" ; "folder2" ; "file.ext" |]
    let pathParts (f:FileInfo) =
        let rec getParent (d:DirectoryInfo) ps =
            if isNull d then ps
            else getParent d.Parent (d.Name :: ps)

        getParent f.Directory [f.Name]
        |> List.toArray


module String =
    
    // ensures all lines end on Environment.NewLine
    // code: s.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine)
    let unifyLineEndings (s:string) =
        s.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine)
    
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


    /// checks if a string is just space characters or Empty string 
    let inline isJustSpaceCharsOrEmpty (str:string) =
        let mutable isSpace = true
        let mutable i = 0
        while isSpace && i < str.Length do
            isSpace <- str.[i] = ' '
            i <- i + 1
        isSpace

    /// counts how many time a substring occures in a string 
    let countSubString (sub:string) (s:string) =
        let mutable k =  0
        let mutable i = s.IndexOf(sub, StringComparison.Ordinal)
        while i >= 0 do
            k <- k + 1
            i <- s.IndexOf(sub,i + sub.Length, StringComparison.Ordinal)
        k
    
    /// counts how many time a character occures in a string 
    let countChar (c:char) (s:string) =
        let mutable k =  0
        let mutable i = s.IndexOf(c)
        while i >= 0 do
            k <- k + 1
            i <- s.IndexOf(c,i + 1)
        k
    /// returns the remainder after the last substring found
    let stringAfterLast (sub:string) (s:string) =
        match s.LastIndexOf(sub) with
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

    // finds text between first occurance of two strings
    let between (a:string) (b:string) (s:string) = 
        //between "((" "))" "c((ab))c" = ("c", "ab", "c")
        let start = s.IndexOf(a, StringComparison.Ordinal) 
        if start = -1 then "","",""
        else 
            let ende = s.IndexOf(b, start + a.Length, StringComparison.Ordinal)
            if ende = -1 then "","",""
            else 
                s.Substring(0,start ),
                s.Substring(start + a.Length, ende - start - a.Length),// finds text betwween two chars
                s.Substring(ende + b.Length)


module Parse = 
    
    type State =     
        |Code
        |InComment      
        |InBlockComment 
        |InString       
        |InAtString      // with @
        |InRawString     // with """
    

    /// a search function that will automatically exlude string literals and code comments from search
    /// the search function shall return true on find sucess
    let findInCode search fromIdx (tx:string) = 
        
        // even if fromIdx is high value serch alwas starts from zero to have correct state

        let len = tx.Length
        let mutable i = -1

        let rec find state = 
            i <- i+1
            if i >= len then None // exit, end of string
            else
                let t = tx.[i]
                match state with 

                |Code -> 
                    match t with 
                    | '/' when i>0 && tx.[i-1] = '/'-> find InComment
                    | '(' when i>0 && tx.[i-1] = '*'-> find InBlockComment
                    | '"' when i>1 && tx.[i-1] = '"' && tx.[i-2] = '"'-> find InRawString
                    | '"' when i>0 && tx.[i-1] = '@' -> find InAtString
                    | x -> 
                        if i >= fromIdx && search(i) then Some i //so that a search can als be startet at the begining of a string, (but not inside a string)
                        else 
                            if t = '"'then find InString
                            else           find state                                

                | InComment -> 
                    if  t='\n' then find Code 
                    else find state 
            
                | InBlockComment ->
                    if  t=')' && i>0 && tx.[i-1] = '*' then find Code 
                    else  find state 

            
                | InString ->
                    if  t='"' then 
                        if i>0 && tx.[i-1] = '\\' then find state //an escaped quote in a string
                        else find Code
                    else
                        find state               
            
                | InAtString ->
                    if    t='"'  then find Code 
                    else              find state              

                | InRawString ->
                    if   t='"' && i>1 && tx.[i-1] = '"' && tx.[i-2] = '"' then find Code
                    else find state
        
        find Code     

    /// Only starts search when not in comment or string literal
    /// Since it searches backward this allows to find ending blocks of strings and comments too
    /// returns offset 
    let findWordBackward (word:string) fromIdx (inText:string) =
        let last = word.Length-1        
    
        let search (i:int) =
            if i < last then false
            else
                //printfn "%d for %d" i last
                let mutable iw = last
                let mutable it = i            
                while iw >= 0 do
                    //printfn "it %d iw %d" it iw
                    if inText.[it] = word.[iw] then 
                        iw <- iw-1
                        it <- it-1
                    else                             
                        iw <- -99 //exit while
                iw = -1            
        
    
        match  findInCode search fromIdx inText with 
        | None  ->   None
        | Some  off -> Some (off - word.Length + 1 )

    /// only starts search when not in comment or string literal
    /// returns offset 
    /// Since it searches forward this allows to find starting blocks of strings and comments too
    let findWordAhead (word:string) fromIdx (inText:string) =
        let last = word.Length-1 
        let max =  inText.Length-1
    
        let search (i:int) =            
            if i > max then false
            else
                //printfn "%d for %d" i last
                let mutable iw = 0
                let mutable it = i            
                while iw < word.Length do
                    //printfn "it %d iw %d" it iw
                    if inText.[it] = word.[iw] then 
                        iw <- iw+1
                        it <- it+1
                    else                             
                        iw <- Int32.MaxValue //exit while
                iw = word.Length            
        
    
        match  findInCode search fromIdx inText with 
        | None ->   None
        | Some  off -> Some off 






(*
    
    open System.ComponentModel
    open Microsoft.FSharp.Quotations
    open Microsoft.FSharp.Quotations.Patterns
       

    /// <summary>Uses reflection to get the field value from an object.</summary>
    /// <param name="type">The instance type.</param>
    /// <param name="instance">The instance object.</param>
    /// <param name="fieldName">The field's name which is to be fetched.</param>
    /// <returns>The field value from the object.</returns>
    let getField(typeDecs:Type, instance:obj, fieldName)=
        let  bindFlags = BindingFlags.Instance ||| BindingFlags.Public ||| BindingFlags.NonPublic ||| BindingFlags.Static
        let field = typeDecs.GetField(fieldName, bindFlags)
        field.GetValue(instance)
    
    ////Getting a sequence of all union cases in discriminated union 
    ///This returns a sequence of union cases for a given discriminated union type, 
    ///the values in this sequence can be passed into any place that expects a case of that discriminated union. 
    ///Useful for use as the option values for a combobox, 
    ///or for printing out all available options for that given discriminated union    
    let getAllUnionCases<'T>() =
        FSharpType.GetUnionCases(typeof<'T>)
        |> Seq.map (fun x -> FSharpValue.MakeUnion(x, Array.zeroCreate(x.GetFields().Length)) :?> 'T)
    

    module Extern =
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

