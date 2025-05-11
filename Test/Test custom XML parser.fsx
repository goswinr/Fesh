#r "nuget: FsEx"
#r "nuget: FsEx.IO"
#r "System.Xml.Linq"

open System
open System.IO
open System.Text
open System.Collections.Generic
open FsEx

/// The only reason to build my own XML parser is to make it error tolerant.
/// To fix https://github.com/dotnet/fsharp/issues/12702 , this might have affected a few nuget packages.
/// The performance is actually comparable to  Xml.Linq.XDocument.Parse()
/// https://stackoverflow.com/questions/4081425/error-tolerant-xml-reader
module XmlParser =

    type Attr = {
        name:string
        value:string
        }

    type Child =
        |Text of string
        |Node of Node

    and Node = {
        name    :string
        attrs   :Attr list
        children:Child list
        }

    // get string and clear string builder
    let inline private get (sb:StringBuilder) =
        let s = sb.ToString()
        sb.Clear() |> ignore
        s

    // to not reallocate this string all the time,
    // use Object.ReferenceEquals(x,XmlParser.summary) for fast equality
    let see = "see"
    let param = "param"
    let membre = "member"
    let summary = "summary"
    let returns = "returns"
    let inline private getConst (sb:StringBuilder) =
        let s =
            match sb.Length with
            | 3 when  sb[0]='s' && sb[1]='e' && sb[2]='e' ->  see
            | 5 when  sb[0]='p' && sb[2]='r' && sb[4]='m' ->  param
            | 6 when  sb[0]='m' && sb[2]='m' && sb[5]='r' ->  membre
            | 7 when  sb[0]='s' && sb[2]='m' && sb[5]='r' && sb[6]='y' ->  summary
            | 7 when  sb[0]='r' && sb[2]='t' && sb[4]='r' && sb[5]='n' ->  returns
            | _ ->
                sb.ToString()
        sb.Clear() |> ignore
        s


    let inline isWhite c = c=' ' || c='\r' || c= '\n'

    /// appends start and end trimmed of whitespace Text node from String builder
    /// skips appending if text is only whitespace,  but always clears the string builder
    let inline trimAppendText(sb:StringBuilder) (cs:Child list)  :Child list =
        if sb.Length=0 then
            cs
        else
            let mutable whiteCount = 0
            let mutable onlyWhitespace = true
            while onlyWhitespace && whiteCount < sb.Length do
                onlyWhitespace <- isWhite sb[whiteCount]
                whiteCount     <- whiteCount+1
            if onlyWhitespace then
                sb.Clear() |> ignore
                cs
            else
                // trim end whitespace from string builder:
                let mutable len = sb.Length
                while isWhite sb.[len-1] do
                    len <-len-1
                // trim start whitespace from string builder:
                whiteCount<-whiteCount-1
                let t = sb.ToString(whiteCount, len-whiteCount)
                sb.Clear() |> ignore
                Text t :: cs

    /// appends End only trimmed Text node from String builder
    /// skips appending if text is only whitespace,  but always clears the string builder
    let inline trimAppendEndText(sb:StringBuilder) (cs:Child list)  :Child list =
        if sb.Length=0 then
            cs
        else
            let mutable j = 0
            let mutable onlyWhitespace = true
            while onlyWhitespace && j < sb.Length do
                onlyWhitespace <- isWhite sb[j]
                j<-j+1
            if onlyWhitespace then
                sb.Clear() |> ignore
                cs
            else
                // trim end whitespace from string builder:
                let mutable len = sb.Length
                while isWhite sb.[len-1] do
                    len <-len-1
                let t = sb.ToString(0, len)
                sb.Clear() |> ignore
                Text t :: cs

    /// start index and last index
    let read(x:string, from:int, till:int) =

        /// the main global index
        let mutable i = from

        /// the global string builder used for all strings
        let sb = StringBuilder()
        let inline add (c:char) = sb.Append(c) |> ignore

        /// if current is space or \r or \n,  increment i to the next non space character
        let rec skipSpace () =
            if i <= till then
                match x[i] with
                | ' ' | '\r' | '\n' ->
                    i<-i+1
                    skipSpace()
                | _ -> ()

         /// if current is \r or \n,  increment i to the next non \r nor \n character
        let rec skipRet () =
            if i <= till then
                match x[i] with
                | '\r' | '\n' ->
                    i<-i+1
                    skipRet()
                | _ -> ()


        /// Set index to first non white char after text to match
        let skipTillAndWhite (txt:string)   =
            match x.IndexOf(txt, i, StringComparison.Ordinal) with
            | -1 ->  i <- Int32.MaxValue
            |  j ->  i <- j + txt.Length ; skipSpace ()

        /// read till just before text to match
        /// i will be on char after text to match
        let readTill (tillTxt:string)   =
            // this actually reads each char twice,  so it is not as efficient
            // get char before tillTxt:
            let e0 = match x.IndexOf(tillTxt, i, StringComparison.Ordinal) with | -1 ->  till |  j ->  j-1
            let mutable e = e0
            // trim white space at end
            while isWhite x[e] do e<-e-1
            //read
            for j=i to e do add x[j]
            // set to end skiping whitespace
            i <- e0 + tillTxt.Length + 1


        /// get the name of the node
        /// on exit current char is ' '  '/' or '&gt;'
        let rec readName () =
            match x[i] with
            | ' ' -> ()
            | '>' -> ()
            | '/' -> ()
            |  c  ->
                add c
                i<-i+1
                readName()

        /// reading attributes such as <member name="T:Microsoft.FSharp.Collections.ResizeArray`1">
        /// on exit current char is '"'
        let rec readAttrValue () =
            match x[i] with
            | '"' -> ()
            | ''' -> ()
            |  c  ->
                add c
                i<-i+1
                readAttrValue()

        /// on exit current char is '/' or '&gt;'
        let rec readAttrs (ps:Attr list) :Attr list =
            match x[i] with
            | '>' -> ps //exit, but don't advance ,  leave this to caller
            | '/' -> ps //exit, but don't advance ,  leave this to caller
            | ' ' -> i<-i+1 ; readAttrs ps // skip space
            | '=' ->
                let name = get sb
                i<-i+1 // jump after '='
                skipSpace()
                i<-i+1 // jump after '"'
                readAttrValue()
                i<-i+1 // jump after '"'
                let value = get sb
                let attr = {name=name; value=value}
                readAttrs (attr::ps)
            |  c  ->
                add c
                i<-i+1 // advance reading name
                readAttrs ps

        /// as opposed to get children this allows any character exept '<'
        let rec readText (cs:Child list) :Child list =
            if i > till then
                cs // exit recursion end of reading range,  or file
            else
                match x[i] with
                | '<' -> trimAppendEndText sb cs   // end of text,  TODO  or us trimAppendText to trim leadin space ??
                | '&' ->
                        match x[i+1 .. i+2] with
                        | "lt" -> i<-i+4 ;  add '<'   // &lt;
                        | "gt" -> i<-i+4 ;  add '>'   // &gt;
                        | "am" -> i<-i+5 ;  add '&'   // &amp;
                        | "qu" -> i<-i+6 ;  add '"'   // &quot;
                        | "ap" -> i<-i+6 ;  add '\''  // &apos;
                        |  _   -> i<-i+1 ;  add '&'
                        readText cs
                | c ->
                    add c
                    i<-i+1
                    readText cs


        /// the main recursive parsing function
        let rec readNodes (cs0:Child list) :Child list =
            let cs = readText cs0
            if i > till then
                cs // exit recursion end of reading range,  or file
            else
                match x[i] with
                | '<' -> // end of node or start of sub children
                    i<-i+1
                    match x[i] with
                    | '?' -> skipTillAndWhite  "?>" ;  readNodes cs // xml header
                    | '!' ->
                        match x[i+1 .. i+2] with
                        | "--" -> skipTillAndWhite "-->" ;  readNodes cs // skip comments
                        | "[C" -> //  <![CDATA[   ]]>
                            i<-i+8
                            readTill "]]>"
                            skipSpace()
                            trimAppendText sb cs
                            |> readNodes
                        | z -> failwithf $"untracket xml tag <!{z}"

                    | '/' -> // probably node closing ,  TODO read name to be sure its the right closing ?
                        if x[i+2] = '>' && x[i+1] = 'p' then // a </p> in netstandard.xml to skip
                            i<-i+3
                            readNodes cs
                        else // normal exit from recursion from node without children
                            skipTillAndWhite ">"
                            trimAppendText sb cs

                    | _ ->  // grand child node starting
                        readName()
                        let name = getConst sb
                        // fix for https://github.com/dotnet/standard/issues/1527:
                        if name = "p"  then  // always skip a <p...> node a closing (e.g. in netstandard.xml)
                            skipTillAndWhite ">"
                            readNodes cs
                        elif name = "br"  then  // a simple <br> without a closing (e.g. in netstandard.xml)
                            let node  = Node {name="br";  attrs=[];  children=[]}
                            skipTillAndWhite ">"
                            readNodes (node :: cs)
                        else
                            let attrs = readAttrs []
                            let children =
                                match x[i] with
                                | '>' -> i<-i+1; skipRet();  readNodes []
                                | '/' -> skipTillAndWhite ">" ; []
                                | x   -> failwithf "Attr end wrong on %c" x

                            let node     = Node {name=name;  attrs=attrs;  children=children}
                            readNodes (node :: cs)

                | '/' ->
                    i<-i+1
                    match x[i] with
                    | '>' ->   // node closed without children
                        i<-i+1
                        cs // exit from recursion

                    | c ->
                        add '/'
                        readNodes cs

                // covered by let children = match x[i] with | '>' -> i<-i+1; skipSpace();  readNodes []
                //| '>' ->  // after this the children start
                //    skipSpace()
                //    readNodes cs

                |  c  ->
                    add c
                    i<-i+1
                    readNodes cs
        []
        |> readNodes
        |> trimAppendText sb // for lose text that might be after last member

    let readAll(x:string) =
        read(x, 0, x.Length-1)


    let getMembers(cs:Child list) =
        let d = Dictionary<string, Child>()
        let rec add (chs:Child list) =
            for c in chs do
                match c with
                |Text _ -> ()
                |Node n ->
                    if Object.ReferenceEquals(n.name, membre)  then
                        match n.attrs with
                        | [] -> ()
                        | na :: r when na.name = "name"  ->
                            if na.value<>"" && d.ContainsKey na.value then Printfn.red $"duplicate: {na.value}"
                            d.[na.value] <- Node n

                        | na :: r  ->
                            Printfn.red $"not name: {na.name} {na.value}"
                        //| _ -> ()
                    else
                        add n.children
        add cs
        d

module Test =
    open XmlParser
    open FsEx

    // this is actually not faster
    let countMembers(rawXml:string) =
        let mutable k = 0
        let mutable i = rawXml.IndexOf("<member name=", StringComparison.Ordinal)
        while i>0 do
            if rawXml[i+14]<>'"' then  // to skip <member name="">
                k <- k+1
            i <- rawXml.IndexOf("<member name=", i+13, StringComparison.Ordinal)
        k

    let rec printNodes ind (chs:Child list) =
        for c in List.rev chs do
            match c with
            |Text t -> Printfn.blue "%s'%s'" (String( ' ', ind*2))  (t.Replace("\n", "\n"+String( ' ', ind*2)))
            |Node n ->
                Printf.darkGreen "%s<%s" (String( ' ', ind*2))   n.name
                for p in List.rev n.attrs do
                    Printf.gray " %s="  p.name
                    Printf.darkGray "\"%s\""  p.value
                Printfn.darkGreen ">"
                printNodes (ind+1) n.children

    let one() =
        clearFeshLog()
        let rawXml =
            File.ReadAllText
                //@"C:\Users\gwins\.nuget\packages\avalonlog\0.7.0\lib\net472\AvaloniaLog.xml"
                //@"D:\Git\Fesh\binStandalone\net472\win-x64\netstandard.xml"
                @"D:\Git\Rhino.Scripting\bin\Release\net48\Rhino.Scripting.xml"
                //@"D:\Git\Fesh\binStandalone\net472\FSharp.Core.xml"
                //@"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\mscorlib.xml"
                //@"C:\Users\gwins\.nuget\packages\microsoft.build.tasks.core\16.6.0\lib\net472\Microsoft.Build.Tasks.Core.xml"


        let chs = XmlParser.readAll rawXml
        printNodes 0 chs
        Printfn.gray  "Done!"

    let memberNameStats() =
        clearFeshLog()
        let DoneNames = HashSet()

        let dirs =
            IO.getAllFilesByPattern "*.xml" @"C:\Users\gwins\.nuget\packages"
            |> Seq.filter( fun n ->  let s = Path.GetFileName(n)  in DoneNames.Add(s) )
            //|> Seq.truncate 100

        let mutable namesStatistic = Rarr()
        for f in dirs do
            Printfn.green $"file {f}"
            let rawXml = File.ReadAllText f
            let ns = XmlParser.readAll rawXml

            //let mutable longest = ""
            //let rec findLongest chs =
            //    for c in  chs do
            //        match c with
            //        |Text t ->
            //            if t.Length > longest.Length then
            //                longest <- t
            //        |Node n ->
            //            for p in n.attrs do
            //                if p.name.Length > p.name.Length then
            //                    longest <- p.name
            //                if p.value.Length > p.value.Length then
            //                    longest <- p.value
            //            findLongest n.children
            //findLongest ns
            //if longest.Length > 1000 then  Printfn.gray  "%s" longest

            let rec findNames chs =
                for c in  chs do
                    match c with
                    |Text _ ->  ()
                    |Node n ->
                        let atts =
                            n.attrs
                            |> List.map ( fun a -> a.name)
                            |> String.concat "|"
                        namesStatistic.Add $"{n.name}:{atts}"
                        //names.Add n.name
                        findNames n.children
            findNames ns
        namesStatistic
        |> Rarr.countBy id
        |> Rarr.sortBy snd
        |> Rarr.iter (fun (m, k) ->
            if   Object.ReferenceEquals(m, XmlParser.summary) then   Printfn.red $"{k} of '{m}'"
            elif Object.ReferenceEquals(m, XmlParser.membre)  then   Printfn.red $"{k} of '{m}'"
            elif Object.ReferenceEquals(m, XmlParser.param)   then   Printfn.red $"{k} of '{m}'"
            elif Object.ReferenceEquals(m, XmlParser.see)     then   Printfn.red $"{k} of '{m}'"
            elif Object.ReferenceEquals(m, XmlParser.returns) then   Printfn.red $"{k} of '{m}'"
            else
                printfn $"{k} of '{m}'")

        Printfn.darkRed  "Done!"

    let testAllMembersFound() =
        clearFeshLog()
        let DoneNames = HashSet()

        let dirs =
            IO.getAllFilesByPattern "*.xml" @"C:\Users\gwins\.nuget\packages"
            |> Seq.filter( fun n ->  let s = Path.GetFileName(n)  in DoneNames.Add(s) )
            //|> Seq.truncate 100

        //let mutable namesStatistic = Rarr()
        for f in dirs do
            let rawXml = File.ReadAllText f
            let ns = XmlParser.readAll rawXml

            let ms = getMembers ns
            let k  = countMembers rawXml

            if ms.Count<>k then
                Printf.orange $"// @\"{f}\" "
                Printfn.gray "// %d <> %d(count)" ms.Count k
            //else
            //    Printfn.green $"file @\"{f}\" "


        Printfn.darkRed  "Done!"


    let perf()=
        (*
        read file 45.5 ms
        XmlParser 175.9 ms // with list
        DocString 265.0 ms // with list of member Rarr
        XmlDocument 165.6 ms
        XDocument 152.1 ms
        *)


        let t = Timer()
        t.Tic()
        //let rawXml = File.ReadAllText @"C:\Users\gwins\.nuget\packages\netstandard.library\2.0.3\build\netstandard2.0\ref\netstandard.xml"
        let rawXml = File.ReadAllText @"C:\Users\gwins\.nuget\packages\microsoft.netframework.referenceassemblies.net48\1.0.0\build\.NETFramework\v4.8\mscorlib.xml"
        printfn "read file %s" t.Toc

        let _ = XmlParser.readAll rawXml
        printfn "XmlParser %s" t.Toc

        let _ = countMembers rawXml
        printfn "countMembers %s" t.Toc

        let _ = Xml.XmlDocument().LoadXml(rawXml)
        printfn "XmlDocument %s" t.Toc

        let _ = Xml.Linq.XDocument.Parse(rawXml)
        printfn "XDocument %s" t.Toc
        printfn "-----"

Test.one()
//Test.testAllMembersFound()
//Test.perf()






