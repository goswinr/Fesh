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
    let changeLuminace (amount:int) (col:Windows.Media.Color)=
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
    
    [<Struct>]
    type Position = {offset:int; line:int}

    /// a find function that will automatically exlude string , chactyer literals and  comments from search
    /// the search function shall return true on find sucess
    let findInCode search fromIdx (tx:string) = 
        
        // even if fromIdx is high value serch alwas starts from zero to have correct state

        let last = tx.Length-1
        if fromIdx > last then eprintfn "findInCode: Search from index %d  is bigger than search string last index %d" fromIdx last
        let mutable i = -1
        let mutable line = 1

        let inline isCh (c:Char) off i =
            i+off <= last && tx.[i+off] = c

        let checklast state = 
           if i > last then ValueNone 
           else
               let t = tx.[i]
               match state with
               |Code ->  if i >= fromIdx && search(i) then ValueSome {offset=i; line=line} else ValueNone
               | InComment  | InBlockComment  | InString  | InAtString  | InRawString -> ValueNone
                

        let rec find advance state  = 
            i <- i + advance
            if i >= last then checklast state 
            else
                let t = tx.[i]
                let u = tx.[i+1]
                match state with 
                |Code -> 
                    if i >= fromIdx && search(i) then ValueSome {offset=i; line=line}
                    else                     
                        match t,u with 
                        | '"' , '"'  when  isCh '"' 2 i  -> find 3 InRawString
                        | '/' , '/'   ->  find 2 InComment
                        | '(' , '*'   ->  find 2 InBlockComment
                        | '@' , '"'   ->  find 2 InAtString
                        | '"' ,  _    ->  find 2 InString    
                        // a char:
                        | '\'', '\\' -> // a escaped character
                            if   isCh 'u'  2 i  &&  isCh '\'' 7 i  then find 8 state  // a 16 bit unicode character
                            elif isCh 'U'  2 i  &&  isCh '\'' 11 i then find 12 state // a 32 bit unicode character
                            else find 4 state  // a simple escaped character
                        | '\'', _ ->                 find 3 state    // jump over a regular  character, including quote "  
                        | '\n', _ ->   line<-line+1 ;find 1 state    
                        |  _      ->                 find 1 state                                

                | InComment -> 
                    if  t='\n' then line<-line+1 ; find 1 Code 
                    else find 1 state 
            
                | InBlockComment ->
                    if   t = '*' && u = ')'  then find 2 Code 
                    elif t =  '\n' then   line<-line+1 ;find 1 state
                    else  find 1 state 

            
                | InString ->
                    match t,u with 
                    | '\\' , '"'    -> find 2 state
                    | '"'  , _      -> find 1 Code
                    | _             -> find 1 state                             
            
                | InAtString ->
                    if    t='"'  then find 1 Code 
                    else              find 1 state              

                | InRawString ->
                    match t,u with 
                    | '"' , '"'  when  isCh '"' 2 i  -> find 3 Code
                    | '\n', _        ->   line<-line+1 ;find 1 state 
                    | _                              -> find 1 state
        
        find 1 Code     
    
    /// a find function that will search full text from index
    /// the search function shall return true on find sucess
    let findInText search fromIdx (tx:string) =        

        let len = tx.Length
        if fromIdx > len-1 then eprintf "findInText:Search from index %d  is bigger than search string %d" fromIdx len
        
        let mutable line = 1                        

        let rec find i  = 
            if i = len then ValueNone 
            else                
                if search(i) then ValueSome {offset=i; line=line}
                else 
                    if tx.[i] = '\n' then line <-line + 1
                    find (i+1)         
        find fromIdx     

    /// Only starts search when not in comment or string literal
    /// Will search backwards from current position.
    /// Last charcter of search shall not be a quote or other non search delimter.
    /// Since it searches backward this allows to find ending blocks of strings and comments too,
    /// but not if they are at last position in search query
    let findWordBackward (word:string) fromIdx (inText:string) =
        let last = word.Length-1       
    
        let search (i:int) =
            if i < last then false // sreach term is bigger than space to serch back
            else                
                let mutable iw = last
                let mutable it = i            
                while iw >= 0 do                   
                    if inText.[it] = word.[iw] then 
                        iw <- iw-1
                        it <- it-1
                    else                             
                        iw <- Int32.MinValue //exit while
                iw = -1 
    
        match findInCode search fromIdx inText with 
        |ValueSome p -> 
            let off = p.offset - last
            let ln = p.line - (String.countChar '\n' word ) 
            Some {offset = off ; line = ln}
        |ValueNone   -> None

    /// Only starts search when not in comment or string literal
    /// Since it searches forward this allows to find starting blocks of strings and comments too
    let findWordAhead (word:string) fromIdx (inText:string) =        
        let max = inText.Length-1
        let len = word.Length

        let search (i:int) =            
            if i+len > max then false // serch would go over the end of text
            else                
                let mutable iw = 0
                let mutable it = i            
                while iw < len do                    
                    if inText.[it] = word.[iw] then 
                        iw <- iw+1
                        it <- it+1
                    else                             
                        iw <- Int32.MaxValue //exit while
                iw = len            
        
        match findInCode search fromIdx inText with 
        |ValueSome p -> Some p
        |ValueNone   -> None    
        




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

