namespace Seff

open System
open System.Threading
open System.Windows.Threading
open System.Reflection
open Microsoft.FSharp.Reflection

module Sync = 
    
    /// the UI SynchronizationContext to switch to inside async CEs
    let mutable syncContext : SynchronizationContext = null  // will be set in main UI STAThread
    
    let internal installSynchronizationContext () = 
        // see https://github.com/fsprojects/FsXaml/blob/c0979473eddf424f7df83e1b9222a8ca9707c45a/src/FsXaml.Wpf/Utilities.fs#L132
        if SynchronizationContext.Current = null then 
            // Create our UI sync context, and install it:
            DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher) |> SynchronizationContext.SetSynchronizationContext
        syncContext <- SynchronizationContext.Current
    
    /// evaluates a function on UI thread
    let doSync f args = 
        async{  do! Async.SwitchToContext syncContext
                return f args
             } |> Async.RunSynchronously
    
    ///evaluates the given function application on the UI thread
    let postToUI : ('a -> 'b) -> 'a -> 'b =
        fun f x ->
          //app.Dispatcher.Invoke(new System.Func<_>(fun () -> f x), [||]) 
          Dispatcher.CurrentDispatcher.Invoke(new System.Func<_>(fun () -> f x), [||])  // from https://www.ffconsultancy.com/products/fsharp_journal/subscribers/FSharpIDE.html        
          |> unbox






module Util =    

    /// a timer for measuring performance, similar to the timer built into FSI
    type Timer() = 
        // GC : https://github.com/fsharp/fsharp/blob/master/src/fsharp/fsi/fsi.fs#L124

        let numGC = System.GC.MaxGeneration
    
        let formatGCs prevGC =  
            prevGC
            |> Array.fold (fun (i,txt) _ -> i+1, sprintf "%s  G%d: %d" txt i (System.GC.CollectionCount(i) - prevGC.[i]) ) (0," ; ") //"GC:") 
            |> snd
    
        let formatMilliSeconds ms = 
            if ms < 0.1 then "less than 0.1 μs"
            elif ms < 1e3 then sprintf "%.1f μs" ms         //less than 1 sec
            elif ms < 1e4 then sprintf "%.2f sec" (ms/1e3)  //less than 10 sec
            elif ms < 6e4 then sprintf "%.1f sec" (ms/1e3)  //less than 1 min       
            else sprintf "%.0f min %.0f sec" (Math.Floor (ms/6e4)) ((ms % 6e4)/1e3)         
        
        let ticWithGC (sw:Diagnostics.Stopwatch) (kGC:int[]) =
            sw.Reset();  GC.Collect() ;  GC.WaitForPendingFinalizers()
            for i=0 to numGC do kGC.[i] <- GC.CollectionCount(i) // reset GC counter base
            sw.Start()

        let tocWithGC (sw:Diagnostics.Stopwatch) countGC = 
            sw.Stop()        
            let txt = sprintf "%s, %s" (formatMilliSeconds sw.Elapsed.TotalMilliseconds) (formatGCs countGC)
            for i=0 to numGC do countGC.[i] <- GC.CollectionCount(i) // reset GC counter base
            sw.Reset() 
            sw.Start()
            txt

        let tocNoGC (sw:Diagnostics.Stopwatch) = 
            sw.Stop()
            let txt = formatMilliSeconds sw.Elapsed.TotalMilliseconds
            sw.Reset() 
            GC.Collect()   
            GC.WaitForPendingFinalizers() 
            sw.Start()
            txt
    
        let kGC = [| for i in 0 .. numGC -> GC.CollectionCount(i) |] 

        //--------------------------------------------------
        // first public timer
        let stopWatch = new Diagnostics.Stopwatch()

        do ticWithGC stopWatch kGC // start stopwatch immediatly
    
        ///* returns time since last tic (or toc) as string, resetes clock
        member this.tocEx = tocWithGC stopWatch kGC
        ///* returns time since last tic (or toc) as string, resetes clock
        member this.toc = tocNoGC stopWatch
        ///* reset and start Timer
        member this.tic() =  ticWithGC stopWatch kGC

        member this.stop() =  stopWatch.Stop()

    type Time()=
        //static member nowStrMenu  = System.DateTime.Now.ToString("yyyy-MM-dd  HH:mm")
        static member nowStr      = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")
        static member nowStrMilli = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss.FFF")
        static member todayStr    = System.DateTime.Now.ToString("yyyy-MM-dd")
 

    let rand = new Random() // to give each error checking call a unique id

    let inline notNull x = match x with null -> true | _ -> false  //not (Object.ReferenceEquals(ob,null))
    
    let inline isTrue (nb:Nullable<bool>) = nb.HasValue && nb.Value

    let inline isOdd  x = x % 2 = 1
    
    let inline isEven x = x % 2 = 0

    let (|>>) a f = f a |> ignore ;a
    
    let assemblyLocation() = IO.Path.GetDirectoryName(Reflection.Assembly.GetExecutingAssembly().Location)

    let sameFile (f1:IO.FileInfo) (f2:IO.FileInfo) =
        f1.FullName.ToLowerInvariant() = f2.FullName.ToLowerInvariant()



     
   
    
    /// Post to this agent for writing a debug string to a desktop file. Only used for bugs that cant be logged to the UI.
    let fileLoggingAgent = // for async debug logging to a file (if the Log window fails to show)
        let file = IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),"Seff-DebugLog.txt")
        MailboxProcessor.Start(
            fun inbox ->
                let rec loop () = 
                    async { let! msg = inbox.Receive()
                            IO.File.AppendAllText(file, Environment.NewLine + msg)
                            return! loop()}
                loop() )

    // Post to this agent for writing a file async.
    let fileWriter = 
        MailboxProcessor.Start( //MailboxProcessor allows writing even when previous write is not finisched yet
            fun inbox ->
                let rec loop () = 
                    async { let! (path,content) = inbox.Receive()
                            IO.File.WriteAllText(path, content)
                            return! loop()}
                loop() )
    
    // Post to this agent for writing a file async.
    let fileWriterLines = 
        MailboxProcessor.Start( //MB allows writing even when previous write is not finisched yet
            fun inbox ->
                let rec loop () = 
                    async { let! (path,content) = inbox.Receive()
                            IO.File.WriteAllLines(path, content)
                            return! loop()}
                loop() )


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

module StringUtil =
    let removeSpecialChars (str:string) = 
        let sb = new Text.StringBuilder()
        for c in str do
            if (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') then  //|| c = '.' || c = '_' then 
                sb.Append(c) |> ignore
        sb.ToString()
    
    let lastCharIs char (s:string)= 
        if isNull s then false
        elif s = "" then false
        else char = s.[s.Length-1]

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
        let mutable i = s.IndexOf(sub)
        while i >= 0 do
            k <- k + 1
            i <- s.IndexOf(sub,i + sub.Length)
        k
    
    /// counts how many time a character occures in a string 
    let countChar (c:char) (s:string) =
        let mutable k =  0
        let mutable i = s.IndexOf(c)
        while i >= 0 do
            k <- k + 1
            i <- s.IndexOf(c,i + 1)
        k
    /// retursn the remainder after the last substring found
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
    
    /// poor man's name parsing: retruns the offset from end of string to last non alphanumeric or '_' character
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
        let start = s.IndexOf(a) 
        if start = -1 then "","",""
        else 
            let ende = s.IndexOf(b, start + a.Length)
            if ende = -1 then "","",""
            else 
                s.Substring(0,start ),
                s.Substring(start + a.Length, ende - start - a.Length),// finds text betwween two chars
                s.Substring(ende + b.Length)

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

        

