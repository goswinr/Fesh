namespace Seff


open System
open System.Windows
open System.IO
open System.Threading
open FSharp.Compiler.Interactive.Shell
open Seff.Model
open Seff.Config
open Seff.Util
open System.Windows.Media
open System.Drawing
open System.Windows.Forms



module CompileScript = 
    
    let libFolderName = "lib"

    let baseXml = """<?xml version="1.0" encoding="utf-8"?>
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Library</OutputType>
            <TargetFramework>net48</TargetFramework> <!-- needed for latest RhinoCommon-->
            <LangVersion>preview</LangVersion>
            <SatelliteResourceLanguages>en</SatelliteResourceLanguages> <!--to only have the english resources of Fsharp.Core--> 

            <RootNamespace>rootNamespace</RootNamespace> <!-- set by Seff scriptcompiler-->
            <AssemblyName>assemblyName</AssemblyName>    <!-- set by Seff scriptcompiler-->

            <GenerateDocumentationFile>true</GenerateDocumentationFile>
            <NeutralLanguage>en</NeutralLanguage>

            <Version>9.9.9.1</Version><!-- set by Seff scriptcompiler-->
            <AssemblyVersion>9.9.9.2</AssemblyVersion><!-- set by Seff scriptcompiler-->
            <FileVersion>9.9.9.3</FileVersion><!-- set by Seff scriptcompiler--> 
            
          </PropertyGroup>      
      
          <ItemGroup>
            <!--<PackageReference Update="FSharp.Core" Version="5.0.1" /> dont include in libaries--> 
            <!--PLACEHOLDER FOR REFERENCES--> <!-- set by Seff scriptcompiler-->
          </ItemGroup>    
    
          <ItemGroup>
            <!--PLACEHOLDER FOR FILES--> <!-- set by Seff scriptcompiler-->
          </ItemGroup>

          <Target Name="DeleteObjFolder" BeforeTargets="AfterBuild"> 
            <RemoveDir Directories="obj" ContinueOnError="true"/> 
          </Target>

        </Project>
        """
     
    let up1 (s:String)  = if s="" then s else Char.ToUpper(s.[0]).ToString() + s.Substring(1)
    let low1 (s:String) = if s="" then s else Char.ToLower(s.[0]).ToString() + s.Substring(1)
       
    let toCamelCase (s:string) = 
        s.Split([|"_" ; "." ; "-" ; "+"; "|"; " "|], StringSplitOptions.RemoveEmptyEntries)
        |> Array.map up1
        |> String.concat ""
        |> (fun x -> if Char.IsLower s.[0] then low1 x else x)

    let replace (a:string) (b:string) (s:string) = s.Replace(a,b)

                 
    type DllRef = {fullPath:string option; fileName:string; nameNoExt:string; copyLocal:bool}

    type FsxRef = {fullPath:string; fileName:string}
    

    let getRefs(code:string, libFolderFull:string, log:ISeffLog) : ResizeArray<DllRef>*ResizeArray<FsxRef> =
        let refs = ResizeArray()
        let fsxs = ResizeArray()
        for ln in code.Split('\n') do 
                let tln = ln.Trim()  
                if tln.StartsWith "#r " then                     
                    let _,path,_ = Str.splitTwice "\""  "\"" tln // get part in quotes
                    let stPath = path.Replace ('\\','/')
                    if stPath.Contains "/RhinoCommon.dll" then   
                        refs.Add {fullPath= Some stPath; fileName="RhinoCommon.dll" ;nameNoExt="RhinoCommon"  ;  copyLocal=false}
                    
                    elif path.Contains "/" || path.Contains "\\" then                        
                        let fileName = stPath.Split('/') |> Seq.last
                        let nameNoExt = fileName.Replace(".dll", "") // TODO make case insensitive, cover .exe
                        refs.Add { fullPath=Some stPath; fileName=fileName ;nameNoExt=nameNoExt; copyLocal=true}
                    else 
                        let nameNoExt = path.Replace(".dll", "") // TODO make case insensitive, cover .exe
                        refs.Add{ fullPath=None; fileName=path ;nameNoExt=nameNoExt; copyLocal=false} // for BCL dlls of the .Net framework
                    
                elif tln.StartsWith "#load " then 
                    let _,path,_ = Str.splitTwice "\""  "\"" tln
                    if path <> "" then 
                        let fullPath = path.Replace ('\\','/')
                        let nameFsx = fullPath.Split('/') |> Seq.last
                        fsxs.Add{fullPath=fullPath; fileName=nameFsx }  
                             
        refs,fsxs
                
    let mutable version = "0.1.0.0" // TODO find way to increment

    //if last write is more than 1h ago ask for overwrite permissions
    let overWriteExisting fsProj =
        let maxAgeHours = 0.5
        let fi = FileInfo(fsProj)
        if fi.Exists then             
            let age = DateTime.UtcNow - fi.LastWriteTimeUtc
            if age > (TimeSpan.FromHours maxAgeHours) then
                let msg = sprintf "Do you want to recompile and overwrite the existing files?\r\n\r\n%s\r\n\r\nthat are %.2f days old at\r\n\r\n(This dialog only shows if the last compilation was more than %.1f hours ago.)"fi.FullName age.TotalDays  maxAgeHours              
                match MessageBox.Show(msg, Style.dialogCaption, MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation, MessageBoxDefaultButton.Button2) with              // TODO uses Windows.Forms   ok ?
                | DialogResult.Yes-> true
                | DialogResult.No-> false
                | _ -> false 
            else
                true
        else
            true
    
    let getRefsXml (libFolderFull:string,  refs:ResizeArray<DllRef>) : string=              
        seq{ 
            for ref in refs |> Seq.sortBy (fun r -> if r.fullPath.IsNone then 0 else 1) do 
                match ref.fullPath with 
                | None -> // for BCL dlls of the .Net framework
                    "<Reference Include=\"" + ref.nameNoExt + "\" />"
                |Some fPath -> 
                    if ref.copyLocal then                     
                        let newp = IO.Path.Combine(libFolderFull, ref.fileName)                    
                        if IO.File.Exists newp then IO.File.Delete newp
                        IO.File.Copy(fPath,newp )

                        let xml  = Path.ChangeExtension(fPath,"xml")
                        let xmln = Path.ChangeExtension(newp,"xml")
                        if IO.File.Exists xmln then IO.File.Delete xmln
                        if IO.File.Exists (xml) then IO.File.Copy(xml, xmln)

                        let pdb  = Path.ChangeExtension(fPath,"pdb")
                        let pdbn = Path.ChangeExtension(newp,"pdb")
                        if IO.File.Exists pdbn then IO.File.Delete pdbn
                        if IO.File.Exists (pdb) then IO.File.Copy(pdb, pdbn)
                        "<Reference Include=\"" + ref.nameNoExt + "\"><HintPath>" + libFolderName + "/" + ref.fileName + "</HintPath></Reference>"
                   
                    else
                        "<Reference Include=\"" + ref.nameNoExt + "\"><HintPath>" + fPath + "</HintPath><Private>False</Private></Reference>"
        } 
        |> String.concat (Environment.NewLine  + "    ")      
    
    let getFsxXml (projFolder:string, nameSpace ,code, fsxloads:ResizeArray<FsxRef>) : string= 
               
        seq{ 
            for load in fsxloads do 
                let niceName = (
                        load.fileName.Replace(".fsx", "") /// TODO make case insensitive
                        |> toCamelCase 
                        |> up1 ) + ".fs"                    
                
                let newp = IO.Path.Combine(projFolder,niceName)
                if IO.File.Exists newp then IO.File.Delete newp
                IO.File.Copy(load.fullPath,newp)
                yield "<Compile Include=\"" + niceName + "\" />"
            
            let fsxName = nameSpace + ".fs"
            let fsxPath = IO.Path.Combine(projFolder,fsxName)
            IO.File.WriteAllText(fsxPath,code) 
            yield     "<Compile Include=\"" + fsxName + "\" />"        
        } 
        |> String.concat (Environment.NewLine + "    ")      

    
    //TODO  use https://github.com/Tyrrrz/CliWrap ??

    

    let createFsproj(code, fp:FilePath, log:ISeffLog, copyDlls, releaseOrDebug) =
        let gray msg = log.PrintfnColor 190 190 190 msg
        
        match fp with 
        | NotSet -> log.PrintfnAppErrorMsg "Cannot compile an unsaved script save it first"
        | SetTo fi ->
            async{
                try
                    gray "compiling %s ..." fi.Name                
                    let name = fi.Name.Replace(".fsx","")
                    let nameSpace = name |> toCamelCase |> up1
                    let projFolder = IO.Path.Combine(fi.DirectoryName,nameSpace) 
                    let libFolderFull = if copyDlls then IO.Path.Combine(projFolder,libFolderName) else "" 
                    if libFolderFull<>"" then  IO.Directory.CreateDirectory(libFolderFull)  |> ignoreObj 
                    IO.Directory.CreateDirectory(projFolder)  |> ignoreObj            
                    let fsProj = IO.Path.Combine(projFolder,nameSpace + ".fsproj")
                    if overWriteExisting fsProj then 
                        let refs,fsxs = getRefs (code ,libFolderFull, log)
                        let fsxXml = getFsxXml(projFolder, nameSpace ,code, fsxs)
                        let refXml = getRefsXml(libFolderFull,refs)
                        baseXml
                        |> replace "        " "" //clear white space at beginning of lines
                        |> replace "rootNamespace" nameSpace
                        |> replace "assemblyName" nameSpace
                        |> replace "9.9.9.1" version
                        |> replace "9.9.9.2" version
                        |> replace "9.9.9.3" version
                        |> replace "<!--PLACEHOLDER FOR REFERENCES--> " refXml
                        |> replace "<!--PLACEHOLDER FOR FILES-->" fsxXml
                        |> fun s -> 
                            IO.File.WriteAllText(fsProj,s,Text.Encoding.UTF8)
                            gray "project created at %s\r\nstarting dotnet build ..." fsProj
                            //https://stackoverflow.com/questions/1145969/processinfo-and-redirectstandardoutput
                            let p = new System.Diagnostics.Process()
                            p.EnableRaisingEvents <- true
                            p.StartInfo.FileName <- "dotnet"
                            let fsProjInQuotes = "\"" + fsProj + "\"" 
                            p.StartInfo.Arguments <- String.concat " " ["build"; fsProjInQuotes;  "--configuration "+releaseOrDebug]
                            log.PrintfnColor 0 0 200 "%s %s" p.StartInfo.FileName p.StartInfo.Arguments
                            p.StartInfo.UseShellExecute <- false
                            p.StartInfo.CreateNoWindow <- true //true if the process should be started without creating a new window to contain it
                            p.StartInfo.RedirectStandardError <-true
                            p.StartInfo.RedirectStandardOutput <-true
                            //p.OutputDataReceived.Add ( fun d -> log.PrintfnColor 80 80 80 "%s" d.Data)
                            p.OutputDataReceived.Add ( fun d -> 
                                let txt = d.Data
                                if not <| isNull txt then // happens often actually
                                    if txt.Contains "Build FAILED." then        log.PrintfnColor 220 0 150  "%s" txt
                                    elif txt.Contains "error FS"   then         log.PrintfnColor 220 0 0  "%s" txt
                                    elif txt.Contains "Build succeeded." then   log.PrintfnColor 0 140 0  "%s" txt
                                    else                                        gray "%s" txt
                                    )
                            p.ErrorDataReceived.Add (  fun d -> log.PrintfnAppErrorMsg "%s" d.Data)               
                            p.Exited.Add( fun _ -> gray  "dotnet build process ended!")
                            p.Start() |> ignore
                            p.BeginOutputReadLine()
                            p.BeginErrorReadLine()
                            //log.PrintfnInfoMsg "compiling to %s" (IO.Path.Combine(projFolder,"bin","Release","netstandard2.0",nameSpace+".dll")) 
                            p.WaitForExit()
                with
                    e -> log.PrintfnAppErrorMsg "%A" e
            } |> Async.Start


           