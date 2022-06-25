//Taken from:
// https://github.com/fsharp/FsAutoComplete/blob/master/src/FsAutoComplete.Core/TipFormatter.fs

//TODO take updated Docstring parser from above source

//or use:
// https://github.com/fsharp/FsAutoComplete/blob/e17692e9b29899f274ed243b168e8790762cd1ee/src/FsAutoComplete.Core/CompilerServiceInterface.fs#L156

namespace Seff

open System
open System.Text
open System.IO
open System.Collections.Generic
open Seff.Model


/// The only reason to build my own XML parser is to make it error tolerant.
/// To fix https://github.com/dotnet/fsharp/issues/12702 , this might have affected a few nuget packages.
/// The performace is actually compareable to  Xml.Linq.XDocument.Parse()
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
    
    // get string and clear stringbuilder
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
    
    /// appends trimmed Text node from String builder
    /// skips appending if text is only whiyespace,  but always clears the string builder
    let inline appendText(sb:StringBuilder) (cs:Child list)  :Child list = 
        if sb.Length=0 then  
            cs
        else
            let mutable whiteCount = 0
            let mutable onlyWhitespace = true
            while onlyWhitespace && whiteCount < sb.Length do  
                onlyWhitespace <- Char.IsWhiteSpace sb[whiteCount] 
                whiteCount     <- whiteCount+1
            if onlyWhitespace then  
                sb.Clear() |> ignore 
                cs 
            else 
                // trim end whitepasce from string builder:
                let mutable len = sb.Length 
                while Char.IsWhiteSpace sb.[len-1] do  
                    len <-len-1
                // trim start whitepasce from string builder:
                whiteCount<-whiteCount-1
                let t = sb.ToString(whiteCount, len-whiteCount)
                sb.Clear() |> ignore 
                Text t :: cs         
    
    
    /// start index and last index
    let read(x:string, from:int, till:int) =  
        
        // the main global index,  -1 because it gets incremented in getChildren immediatly
        let mutable i = from - 1 // 
        
        // the global stringbuilder used for all strings
        let sb = StringBuilder()
        let inline add (c:char) = sb.Append(c) |> ignore 
        
        /// Set index to last char of chars to match
        let skipTill (chars:string)   = 
            i<-i+1
            match x.IndexOf(chars, i) with 
            | -1 ->  i <- Int32.MaxValue 
            |  j ->  i <- j + chars.Length - 1
        
        let maxi = till - 4 // minus two because it might be incremented forut times  after the check at  </p>
        
        // move i to the next non space character
        let rec skipSpace() =  
            if i < maxi && x[i]=' ' then  
                i<-i+1
                skipSpace() 
        
        // get the name of the node 
        let rec readName () =  
            match x[i] with 
            | ' ' -> ()
            | '>' -> ()
            | '/' -> ()
            |  c  ->  
                add c  
                i<-i+1
                readName() 
        
        // reading attrerties such as <member name="T:Microsoft.FSharp.Collections.ResizeArray`1">
        let rec readAttrValue () = 
            i<-i+1
            match x[i] with 
            | '"' -> i<-i+1
            |  c  ->  
                add c 
                readAttrValue() 
        
        let rec  readAttrs (ps:Attr list) :Attr list = 
            match x[i] with 
            | '>' -> i<-i-1 ; ps
            | '/' -> i<-i-1 ; ps
            | ' ' -> i<-i+1 ; readAttrs ps // skip space
            | '=' ->  
                let name = get sb
                i<-i+1 // jump after '='
                skipSpace() 
                readAttrValue()
                let value = get sb 
                let attr = {name=name; value=value} 
                readAttrs (attr::ps)
            |  c  ->  
                add c
                i<-i+1
                readAttrs ps
        
        /// the main recursive parsing function
        let rec getChildren (cs:Child list) :Child list = 
            //if i > x.Length-3 then  
            if i > maxi then  
                cs 
            else 
                i<-i+1
                match x[i] with 
                | '<' -> // end of node or start of sub children 
                    let cs = appendText sb cs // TODO realy do shadowing ? 
                    i<-i+1
                    match x[i] with 
                    | '?' -> skipTill "?>" ;  getChildren cs 
                    | '!' -> skipTill "!>" ;  getChildren cs // skip comments
                    | '/' -> // probaly node closing 
                        if x[i+2] = '>' && x[i+1] = 'p' then // a </p> in netstandard.xml
                            skipTill ">" 
                            getChildren cs
                        else // exit from recursion
                            skipTill ">"
                            appendText sb cs 
                        
                    | _ ->  // grand child node starting 
                        skipSpace()
                        readName()
                        let name     = getConst sb
                        if name = "p" then  // a <p> in netstandard.xml
                            i<-i-1
                            skipTill ">" 
                            getChildren cs 
                        elif name = "br" then  // a <br> in netstandard.xml 
                            getChildren cs 
                        else
                            let attrs    = readAttrs [] 
                            let children = getChildren []
                            let node     = {name=name;  attrs=attrs;  children=children}
                            let nodes = Node node :: cs  
                            getChildren nodes
               
                | '/' ->  
                    i<-i+1
                    match x[i] with 
                    | '>' ->   // node closed without children 
                        cs // exit from recursion
                        
                    | c -> 
                        add '/'
                        add c
                        getChildren cs 
                
                | '>' ->  // after this the children start 
                    getChildren cs 
                
                | '&' -> 
                    match x[i+1 .. i+2] with 
                    | "lt" -> i<-i+3 ;  add '<'   // &lt;  
                    | "gt" -> i<-i+3 ;  add '>'   // &gt;  
                    | "qu" -> i<-i+5 ;  add '"'   // &quot;
                    | "am" -> i<-i+4 ;  add '&'   // &amp; 
                    | "ap" -> i<-i+5 ;  add '\''  // &apos;
                    |  _   -> add '&' 
                    getChildren cs
                    
                |  c  ->  
                    add c 
                    getChildren cs 
        
        []
        |> getChildren 
        |> appendText sb
    
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
                        | na :: r when na.name = "name" ->d.[na.value] <- Node n
                        | _ -> ()
                    else 
                        add n.children
        add cs
        d

module DocString = 
    
    let xmlDocCache = Dictionary<string, FileInfo*Dictionary<string, XmlParser.Child>>()
    let failedPath  = Dictionary<string,string>()

    let private getXmlDocImpl (dllFile:string) : Result<FileInfo*Dictionary<string, XmlParser.Child>,string>= 
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
                    xmlDocCache.[dllFile]<- r
                    Ok r
                with e -> 
                    Error $"Error reading Xml File {e}"                       
            else                                   
                Error $"Xml File not found for : '{dllFile}'"                         
    
    
    let getXmlDoc(dllFile:string) : Result<FileInfo*Dictionary<string, XmlParser.Child>,string>= 
        if failedPath.ContainsKey dllFile then 
            Error(failedPath[dllFile]) // to not try accessing a fail path over and over again
        else
            match getXmlDocImpl (dllFile) with 
            | Ok    r1 -> Ok r1
            | Error e1 -> 
                if Path.GetFileName dllFile = "netstandard.dll" then 
                    let fsharpCoreDir = Path.Combine(Path.GetDirectoryName(Reflection.Assembly.GetAssembly([].GetType()).Location),"netstandard.dll")                        
                    match getXmlDocImpl (fsharpCoreDir) with 
                    | Ok    r2 -> Ok r2
                    | Error e2 -> 
                        let seffDir = Path.Combine(Path.GetDirectoryName(Reflection.Assembly.GetAssembly(ISeffLog.log.GetType()).Location),"netstandard.dll") 
                        match getXmlDocImpl (seffDir) with 
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
