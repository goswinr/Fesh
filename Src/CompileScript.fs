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



module CompileScript = 
    
    let up1 (s:String)  = if s="" then s else Char.ToUpper(s.[0]).ToString() + s.Substring(1)
    let low1 (s:String) = if s="" then s else Char.ToLower(s.[0]).ToString() + s.Substring(1)
       
    let toCamelCase (s:string) = 
        s.Split([|"_" ; "." ; "-" ; "+"; "|"; " "|], StringSplitOptions.RemoveEmptyEntries)
        |> Array.map up1
        |> String.concat ""
        |> (fun x -> if Char.IsLower s.[0] then low1 x else x)

    let replace (a:string) (b:string) (s:string) = s.Replace(a,b)

    let baseXml = """<?xml version="1.0" encoding="utf-8"?>
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Library</OutputType>
            <TargetFramework>net472</TargetFramework> 
            <LangVersion>preview</LangVersion>
            <SatelliteResourceLanguages>en</SatelliteResourceLanguages> <!--to only have the english resources of Fsharp.Core--> 
            <RootNamespace>rootNamespace</RootNamespace> <!-- set by Seff scriptcompiler-->
            <AssemblyName>assemblyName</AssemblyName>    <!-- set by Seff scriptcompiler-->
            <GenerateDocumentationFile>true</GenerateDocumentationFile>
            <NeutralLanguage>en</NeutralLanguage>
            <Version>9.7.8.6.5.1</Version><!-- set by Seff scriptcompiler-->
            <AssemblyVersion>9.7.8.6.5.2</AssemblyVersion><!-- set by Seff scriptcompiler-->
            <FileVersion>9.7.8.6.5.3</FileVersion><!-- set by Seff scriptcompiler-->
          </PropertyGroup>      
      
          <ItemGroup>
            <!--<PackageReference Update="FSharp.Core" Version="4.7.2" /> dont include in libaries--> 
            <!--references--> <!-- set by Seff scriptcompiler-->
          </ItemGroup>    
    
          <ItemGroup>
            <Compile Include="pathToFsx" /><!-- set by Seff scriptcompiler-->
          </ItemGroup>
        </Project>
        """
                  
    
    let getRefXml (name, path, copyLocal ) =
        let ref = 
            """<Reference Include="name"><HintPath>path</HintPath><Private>False</Private></Reference>"""
        ref.Replace("name",name)
        |> fun s -> if path = "" then s.Replace("<HintPath>path</HintPath>", "") else s.Replace("path",path)
        |> fun s -> if copyLocal  then s.Replace("<Private>False</Private>", "") else s // for copy local = false of Rhinocommon

    let libFolderName = "lib"
    
    let getRefs(code:string, libFolder:string)=
        seq{ for ln in code.Split('\n') do 
                let tln = ln.Trim()
                if tln.StartsWith "#r " then 
                    let _,path,_ = String.splitTwice "\""  "\"" tln
                    let p = path.Replace ('\\','/')
                    let nameDll = p.Split('/') |> Seq.last 
                    let name = nameDll |> replace ".dll" ""
                    if p.Contains "/" || p.Contains "\\" then
                        if p.Contains "/RhinoCommon.dll" then   name, path, false
                        else 
                            if libFolder <>"" then 
                                let newp = IO.Path.Combine(libFolder,nameDll)
                                if IO.File.Exists newp then IO.File.Delete newp
                                IO.File.Copy(path,newp )
                                name, libFolderName+"/"+nameDll, true
                            else
                                name, path, true
                    else 
                        name, "", true // for BCL dlls of the  .Net framework
                    }
                
    let mutable version = "0.0.0.1"

    let createFsproj(code, fp:FilePath, log:ISeffLog, copyDlls) =
        match fp with 
        | NotSet -> log.PrintAppErrorMsg "Cannot compile an unsaved script save it first"
        |SetTo fi ->
            let version = "0.0.0.1"
            let name = fi.Name.Replace(".fsx","")
            let nameSpace = name |> toCamelCase |> up1
            let projFolder = IO.Path.Combine(fi.DirectoryName,name) 
            let libFolder = if copyDlls then IO.Path.Combine(projFolder,libFolderName) else "" 
            if libFolder<>"" then  IO.Directory.CreateDirectory(libFolder)  |> ignore 
            IO.Directory.CreateDirectory(projFolder)  |> ignore            
            let fsProj = IO.Path.Combine(projFolder,nameSpace + ".fsproj")
            let fsxName = nameSpace + ".fsx"
            let fsxPath = IO.Path.Combine(projFolder,fsxName)
            IO.File.WriteAllText(fsxPath,code)
            let refs = 
                getRefs (code ,libFolder)
                |> Seq.map getRefXml
                |> String.concat Environment.NewLine
            baseXml
            |> replace "        " "" //cler white space at beginning of lines
            |> replace "rootNamespace" nameSpace
            |> replace "assemblyName" nameSpace
            |> replace "9.7.8.6.5.1" version
            |> replace "9.7.8.6.5.2" version
            |> replace "9.7.8.6.5.3" version
            |> replace "<!--references-->" refs
            |> replace "pathToFsx" fsxName
            |> fun s -> 
                IO.File.WriteAllText(fsProj,s,Text.Encoding.UTF8)
                log.PrintInfoMsg "project created at %s\r\nnow compiling ..." fsProj
                //https://stackoverflow.com/questions/1145969/processinfo-and-redirectstandardoutput
                let p = new System.Diagnostics.Process()
                p.EnableRaisingEvents <- true
                p.StartInfo.FileName <- "dotnet"
                let fsProjinQuotes = "\"" + fsProj + "\"" 
                p.StartInfo.Arguments <- String.concat " " ["build"; fsProjinQuotes;  "--configuration Release"]
                p.StartInfo.UseShellExecute <- false
                p.StartInfo.CreateNoWindow <- true //true if the process should be started without creating a new window to contain it
                p.StartInfo.RedirectStandardError <-true
                p.StartInfo.RedirectStandardOutput <-true
                p.OutputDataReceived.Add ( fun d -> log.PrintCustomBrush Media.Brushes.DarkOliveGreen   "%s" d.Data)
                p.ErrorDataReceived.Add (  fun d -> log.PrintAppErrorMsg "%s" d.Data)               
                p.Exited.Add( fun _ -> log.PrintInfoMsg  "Compiling done")
                p.Start() |> ignore
                p.BeginOutputReadLine()
                p.BeginErrorReadLine()
                //log.PrintInfoMsg "compiling to %s" (IO.Path.Combine(projFolder,"bin","Release","netstandard2.0",nameSpace+".dll")) 
                p.WaitForExit()


           