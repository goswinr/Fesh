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

          <Target Name="DeleteObjFolder" BeforeTargets="AfterBuild"> 
            <RemoveDir Directories="obj" ContinueOnError="true"/> 
          </Target>

        </Project>
        """
                  
    
    let getRefXml (name, path, copyLocal ) =
        let ref = 
            """<Reference Include="name"><HintPath>path</HintPath><Private>False</Private></Reference>"""
        ref.Replace("name",name)
        |> fun s -> if path = "" then s.Replace("<HintPath>path</HintPath>", "") else s.Replace("path",path)
        |> fun s -> if copyLocal  then s.Replace("<Private>False</Private>", "") else s // for copy local = false of Rhinocommon

    let libFolderName = "lib"
    
    let getRefs(code:string, libFolder:string, log:ISeffLog)=
        let refs = ResizeArray()
        for ln in code.Split('\n') do 
                let tln = ln.Trim()
                if tln.StartsWith "#r " then 
                    let _,path,_ = String.splitTwice "\""  "\"" tln
                    let p = path.Replace ('\\','/')
                    let nameDll = p.Split('/') |> Seq.last 
                    let name = nameDll |> replace ".dll" ""
                    if p.Contains "/" || p.Contains "\\" then
                        if p.Contains "/RhinoCommon.dll" then   refs.Add(name, path, false)
                        else 
                            if libFolder <>"" then 
                                let newp = IO.Path.Combine(libFolder,nameDll)
                                try
                                    if IO.File.Exists newp then IO.File.Delete newp
                                    IO.File.Copy(path,newp )

                                    let xml  = Path.ChangeExtension(path,"xml")
                                    let xmln = Path.ChangeExtension(newp,"xml")
                                    if IO.File.Exists xmln then IO.File.Delete xmln
                                    if IO.File.Exists (xml) then IO.File.Copy(xml, xmln)

                                    let pdb  = Path.ChangeExtension(path,"pdb")
                                    let pdbn = Path.ChangeExtension(newp,"pdb")
                                    if IO.File.Exists pdbn then IO.File.Delete pdbn
                                    if IO.File.Exists (pdb) then IO.File.Copy(pdb, pdbn)

                                with e ->
                                    log.PrintfnIOErrorMsg "Error in getting refrences: %A" e
                                refs.Add(name, libFolderName+"/"+nameDll, true)
                            else
                                refs.Add(name, path, true)
                    else 
                        refs.Add(name, "", true) // for BCL dlls of the  .Net framework
        refs
                
    let mutable version = "0.1.0.0" // TODO find way to increment

    //if last write is more than 1h agao ask for overwrite permissions
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


    let createFsproj(code, fp:FilePath, log:ISeffLog, copyDlls) =
        match fp with 
        | NotSet -> log.PrintfnAppErrorMsg "Cannot compile an unsaved script save it first"
        |SetTo fi ->
            async{
                log.PrintfnInfoMsg "compiling %s ..." fi.Name                
                let name = fi.Name.Replace(".fsx","")
                let nameSpace = name |> toCamelCase |> up1
                let projFolder = IO.Path.Combine(fi.DirectoryName,name) 
                let libFolder = if copyDlls then IO.Path.Combine(projFolder,libFolderName) else "" 
                if libFolder<>"" then  IO.Directory.CreateDirectory(libFolder)  |> ignore 
                IO.Directory.CreateDirectory(projFolder)  |> ignore            
                let fsProj = IO.Path.Combine(projFolder,nameSpace + ".fsproj")
                if overWriteExisting fsProj then 
                    let fsxName = nameSpace + ".fsx"
                    let fsxPath = IO.Path.Combine(projFolder,fsxName)
                    IO.File.WriteAllText(fsxPath,code)
                    let refs = 
                        getRefs (code ,libFolder,log)
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
                        log.PrintfnInfoMsg "project created at %s\r\nstarting dotnet build ..." fsProj
                        //https://stackoverflow.com/questions/1145969/processinfo-and-redirectstandardoutput
                        let p = new System.Diagnostics.Process()
                        p.EnableRaisingEvents <- true
                        p.StartInfo.FileName <- "dotnet"
                        let fsProjinQuotes = "\"" + fsProj + "\"" 
                        p.StartInfo.Arguments <- String.concat " " ["build"; fsProjinQuotes;  "--configuration Debug"]
                        log.PrintfnCustomColor 0 0 200 "%s %s" p.StartInfo.FileName p.StartInfo.Arguments
                        p.StartInfo.UseShellExecute <- false
                        p.StartInfo.CreateNoWindow <- true //true if the process should be started without creating a new window to contain it
                        p.StartInfo.RedirectStandardError <-true
                        p.StartInfo.RedirectStandardOutput <-true
                        p.OutputDataReceived.Add ( fun d -> log.PrintfnCustomColor 50 150 0 "%s" d.Data)
                        p.ErrorDataReceived.Add (  fun d -> log.PrintfnAppErrorMsg "%s" d.Data)               
                        p.Exited.Add( fun _ -> log.PrintfnInfoMsg  "Build finnished!")
                        p.Start() |> ignore
                        p.BeginOutputReadLine()
                        p.BeginErrorReadLine()
                        //log.PrintfnInfoMsg "compiling to %s" (IO.Path.Combine(projFolder,"bin","Release","netstandard2.0",nameSpace+".dll")) 
                        p.WaitForExit()
                        } |> Async.Start


           