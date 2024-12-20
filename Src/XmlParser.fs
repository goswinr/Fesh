﻿namespace Fesh

open System
open System.Text
open System.IO
open System.Collections.Generic
open Fesh.Model


/// The only reason to build my own XML parser is to make it error tolerant.
/// To fix https://github.com/dotnet/fsharp/issues/12702
/// While this issue was fixed fast, it took a few month to be release in the official sdk.
/// So this might have affected a few nuget packages.
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

    // get string and clear string-builder
    let inline private get (sb:StringBuilder) =
        let s = sb.ToString()
        sb.Clear() |> ignore
        s

    // TODO revise if this optimization is actually usefull:
    // to not reallocate these strings all the time,
    // later use Object.ReferenceEquals(x,XmlParser.summary) for fast equality
    let see = "see"
    let param = "param"
    let membre = "member"   // yes, spelled wrong because its an F# keyword
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

    let inline isWhite c = c=' ' || c='\r' || c= '\n' || c= '\t'

    /// appends Text node from String builder. includes starting and ending whitespace.
    /// skips appending if text is only whitespace,  but always clears the string builder
    let inline appendText(sb:StringBuilder) (cs:Child list)  :Child list =
        if sb.Length=0 then
            cs
        else
            let mutable idx = 0
            let mutable onlyWhitespace = true
            while onlyWhitespace && idx < sb.Length do
                onlyWhitespace <- isWhite sb[idx]
                idx            <- idx+1
            if onlyWhitespace then
                sb.Clear() |> ignore
                cs
            else
                let t = sb.ToString()
                sb.Clear() |> ignore
                Text t :: cs

    (*
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
    *)




    /// start index and last index
    let read(x:string, from:int, till:int) =

        /// the main global index
        let mutable i = from

        /// the global string-builder used for all strings
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

        (* unused because white skipping at start of text is disabled
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
        *)

        /// Set index to first after text to match
        let skipTill (txt:string)   =
            match x.IndexOf(txt, i, StringComparison.Ordinal) with
            | -1 ->  i <- Int32.MaxValue
            |  j ->  i <- j + txt.Length


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
            // set to end skipping whitespace
            i <- e0 + tillTxt.Length + 1


        /// get the name of the node
        /// on exit current char is ' '  '/' or '&gt;'
        let rec readName () =
            match x[i] with
            | ' ' -> ()
            | '/' -> ()
            | '>' -> ()
            |  c  ->
                add c
                i<-i+1
                readName()


        /// reading attributes such as: member name='T:Microsoft.FSharp.Collections.ResizeArray`1'
        /// on exit current char is '
        let rec readSingleQuotedAttrValue () =
            match x[i] with
            | ''' -> ()
            |  c  ->
                add c
                i<-i+1
                readSingleQuotedAttrValue()

        /// reading attributes such as: member name="T:Microsoft.FSharp.Collections.ResizeArray`1"
        /// on exit current char is "
        let rec readDoubleQuotedAttrValue () =
            match x[i] with
            | '"' -> ()
            |  c  ->
                add c
                i<-i+1
                readDoubleQuotedAttrValue()

        /// reading attributes such as 'member name="T:Microsoft.FSharp.Collections.ResizeArray`1" '
        /// on exit current char is " or '
        let readAttrValue () =
            match x[i] with
            | '"' -> i<-i+1 ; readDoubleQuotedAttrValue() // i<-i+1 to jump after the " or '
            | ''' -> i<-i+1 ; readSingleQuotedAttrValue()
            |  _  -> () // should not happen after the = and some whitespace there should be a " or ' to start the value


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
                readAttrValue()
                i<-i+1 // jump after the " or '
                let value = get sb
                let attr = {name=name; value=value}
                readAttrs (attr::ps)
            |  c  ->
                add c
                i<-i+1 // advance reading name
                readAttrs ps

        /// as opposed to get children this allows any character except '&lt'
        let rec readText (cs:Child list) :Child list =
            if i > till then
                cs // exit recursion end of reading range,  or file
            else
                match x[i] with
                | '<' -> appendText sb cs // trimAppendEndText sb cs   // end of text,  TODO  or us trimAppendText to trim leading space ??
                | '&' ->
                        match x[i+1 .. i+2] with // TODO actually verify that this is a valid escape terminated by ';'
                        | "lt" -> i<-i+4 ;  add '<'   //  <  &lt;
                        | "gt" -> i<-i+4 ;  add '>'   //  >  &gt;
                        | "am" -> i<-i+5 ;  add '&'   //  &  &amp;
                        | "qu" -> i<-i+6 ;  add '"'   //  "  &quot;
                        | "ap" -> i<-i+6 ;  add '\''  //  '  &apos;
                        |  _   -> i<-i+1 ;  add '&'
                        readText cs
                | c ->
                    i<-i+1
                    add c
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
                    | '?' -> skipTill  "?>" ;  readNodes cs // xml header
                    | '!' ->
                        match x[i+1 .. i+2] with
                        | "--" -> skipTill "-->" ;  readNodes cs // skip comments
                        | "[C" -> //  <![CDATA[   ]]>
                            i<-i+8
                            readTill "]]>"
                            skipSpace()
                            appendText sb cs //trimAppendText sb cs
                            |> readNodes
                        | z -> failwithf $"unrecognized xml tag '<!{z}' in xml docstring"

                    | '/' -> // probably node closing ,  TODO read name to be sure its the right closing ?
                        if x[i+2] = '>' && x[i+1] = 'p' then // a </p> in netstandard.xml to skip
                            i<-i+3
                            readNodes cs
                        else // normal exit from recursion from node without children
                            skipTill ">"
                            appendText sb cs //trimAppendText sb cs

                    | _ ->  // grand child node starting
                        readName()
                        let name = getConst sb
                        // fix for https://github.com/dotnet/standard/issues/1527:
                        if name = "p"  then  // always skip a <p...> node without a closing (e.g. in netstandard.xml)
                            skipTill ">"
                            readNodes cs
                        elif name = "br"  then  // a simple <br> without a closing (e.g. in netstandard.xml)
                            let node  = Node {name="br";  attrs=[];  children=[]}
                            skipTill ">"
                            readNodes (node :: cs)
                        else
                            let attrs = readAttrs []
                            let children =
                                match x[i] with
                                | '>' ->
                                    i<-i+1
                                    //skipRet() // keep return and whitespace at start of next child( it might be a text node starting with a line return). see rs.AddText in Rhino.Scripting.xml
                                    readNodes []
                                | '/' ->
                                    skipTill ">"
                                    []
                                | x   ->
                                    failwithf "XML docstring Attr end is wrong on '%c'" x

                            let node     = Node {name=name;  attrs=attrs;  children=children}
                            readNodes (node :: cs)

                | '/' ->
                    i<-i+1
                    match x[i] with
                    | '>' ->   // node closed without children
                        i<-i+1
                        cs // exit from recursion

                    | _ ->
                        add '/'
                        readNodes cs
                (*
                // after > the children start
                // covered by
                let children =
                    match x[i] with
                    | '>' -> i<-i+1; skipSpace();  readNodes []
                    | '>' ->
                        skipSpace()
                        readNodes cs
                *)

                |  c  ->  // the most common case
                    add c
                    i<-i+1
                    readNodes cs
        []
        |> readNodes
        |> appendText sb  //trimAppendText sb // for lose text that might be after last member

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
                        | na :: _ when na.name = "name"  ->
                            //if na.value<>"" && d.ContainsKey na.value then Printfn.red $"duplicate: {na.value}"
                            d.[na.value] <- Node n

                        //| na :: r  -> Printfn.red $"not name: {na.name} {na.value}"
                        | _ -> ()
                    else
                        add n.children
        add cs
        d



module DocString =

    type XmlFilePath = string
    type XmlFileInfo = FileInfo
    type DllFilePath = string
    type DllFilePathError = string
    type MemberName = string

    let xmlDocCache = Dictionary<DllFilePath, XmlFileInfo*Dictionary<XmlFilePath, XmlParser.Child>>()
    let failedPath  = Dictionary<DllFilePath, DllFilePathError>()

    let private getXmlDocImpl (dllFile:DllFilePath) : Result<XmlFileInfo*Dictionary<MemberName, XmlParser.Child>, DllFilePathError>=
        if xmlDocCache.ContainsKey dllFile then
            Ok xmlDocCache.[dllFile]
        else
            let xmlFile = Path.ChangeExtension(dllFile, ".xml")
            if IO.File.Exists xmlFile then
                try
                    let ms =
                        xmlFile
                        |> File.ReadAllText
                        |> XmlParser.readAll
                        |> XmlParser.getMembers
                    let r = FileInfo xmlFile , ms
                    xmlDocCache.[dllFile] <- r
                    Ok r
                with e ->
                    Error $"Error reading Xml File {e}"
            else
                Error $"Xml File not found for : '{dllFile}'"


    let getXmlDoc(dllFile:DllFilePath) : Result<XmlFileInfo*Dictionary<MemberName, XmlParser.Child>, DllFilePathError>=
        if failedPath.ContainsKey dllFile then
            Error(failedPath[dllFile]) // to not try accessing a failed path over and over again
        else
            match getXmlDocImpl (dllFile) with
            | Ok    r1 -> Ok r1
            | Error e1 ->
                if Path.GetFileName dllFile = "netstandard.dll" then
                    let fsharpCoreDir = Path.Combine(Path.GetDirectoryName(Reflection.Assembly.GetAssembly([].GetType()).Location),"netstandard.dll")
                    match getXmlDocImpl (fsharpCoreDir) with
                    | Ok    r2 -> Ok r2
                    | Error e2 ->
                        let feshDir = Path.Combine(Path.GetDirectoryName(Reflection.Assembly.GetAssembly(IFeshLog.log.GetType()).Location),"netstandard.dll")
                        match getXmlDocImpl (feshDir) with
                        | Ok    r3 -> Ok r3
                        | Error e3 ->
                            let emsg = e1+"\r\n"+e2+"\r\n"+e3
                            failedPath[dllFile] <- emsg
                            Error emsg
                else
                    failedPath[dllFile] <- e1
                    Error e1


(*
4 of 'see:typeparamref'
4 of 'see:paramref'
5 of 'em:'
5 of 'devremarks:'
5 of 'seealso:'
7 of 'strong:'
8 of 'devdoc:'
10 of 'comments:'
10 of 'IPermission:Read|version|class'
11 of 'p:'
11 of 'see:name'
11 of 'return:'
12 of 'a:href'
13 of 'comment:'
14 of 'code:title|region|source|lang'
14 of 'note:type'
14 of 'internalonly:'
17 of 'i:'
26 of 'b:'
31 of 'br:'
38 of 'see:href'
40 of 'IPermission:Flags|version|class'
59 of 'nodoc:'
82 of 'license:type'
82 of 'copyright:'
99 of 'code:'
140 of 'inheritdoc:cref'
155 of 'IPermission:Unrestricted|version|class'
159 of 'seealso:cref'
176 of 'PermissionSet:'
227 of 'listheader:'
260 of 'File:FileVersion|AssemblyVersion|PublicKeyToken|AssemblyName|Type|Path'
276 of 'list:type'
358 of 'code:source'
414 of 'assembly:'
414 of 'name:'
415 of 'doc:'
415 of 'members:'
497 of 'example:'
718 of 'value:'
897 of 'filterpriority:'
998 of 'typeparamref:name'
1056 of 'item:'
1102 of 'term:'
1289 of 'description:'
2240 of 'inheritdoc:'
2557 of 'remarks:'
5345 of 'c:'
9057 of 'typeparam:name'
10176 of 'para:'                    TODO !
67429 of 'exception:cref'
87357 of 'paramref:name'
98264 of 'see:langword'
111266 of 'returns:'
180623 of 'param:name'
251107 of 'summary:'
254839 of 'member:name'
285270 of 'see:cref'
*)
