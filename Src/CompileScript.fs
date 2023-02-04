namespace Seff

open System
open System.Windows
open System.IO
open System.Drawing
//open System.Windows.Forms

open Seff.Model
open Seff.Util
open System.Text


module CompileScript = 

    let libFolderName = "lib"

    // TODO really only <PlatformTarget>x64</PlatformTarget><!--  x64 is required e.g by Rhino, don't us just 'Platform' tag-->   ??

    let baseXml = """<?xml version="1.0" encoding="utf-8"?>
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Library</OutputType>
            <TargetFramework>net48</TargetFramework>
            <LangVersion>preview</LangVersion>
            <SatelliteResourceLanguages>en</SatelliteResourceLanguages> <!--to only have the English resources of Fsharp.Core-->

            <RootNamespace>rootNamespace</RootNamespace> <!-- set by Seff scriptcompiler-->
            <AssemblyName>assemblyName</AssemblyName>    <!-- set by Seff scriptcompiler-->

            <GenerateDocumentationFile>true</GenerateDocumentationFile>
            <NeutralLanguage>en</NeutralLanguage>

            <Version>9.9.9.1</Version><!-- set by Seff scriptcompiler-->
            <AssemblyVersion>9.9.9.2</AssemblyVersion><!-- set by Seff scriptcompiler-->
            <FileVersion>9.9.9.3</FileVersion><!-- set by Seff scriptcompiler-->

            <!--<PlatformTarget>x64</PlatformTarget>  x64 is required e.g by Rhino, don't us just 'Platform' tag-->

          </PropertyGroup>

          <ItemGroup>
            <!--<PackageReference Update="FSharp.Core" Version="5.0.2" /> don't include in libraries-->
            <!--PLACEHOLDER FOR NUGETS--> <!-- set by Seff scriptcompiler-->
          </ItemGroup>

          <ItemGroup>
            <!--PLACEHOLDER FOR REFERENCES--> <!-- set by Seff scriptcompiler-->
          </ItemGroup>

          <ItemGroup>
            <!--PLACEHOLDER FOR FILES--> <!-- set by Seff scriptcompiler-->
          </ItemGroup>

          <!--
          <Target Name="DeleteObjFolder" BeforeTargets="AfterBuild"> <RemoveDir Directories="obj" ContinueOnError="true" /> </Target>
          -->

        </Project>
        """

    

    /// also removes "_" ;  "-" ; "+"; "|"; " " from string 
    /// first letter will be capital
    let toCamelCase (s:string) = 
        // TODo check for non valid file path characters
        s.Split([|"_" ;  "-" ; "+"; "|"; " "|], StringSplitOptions.RemoveEmptyEntries) // keep dot?!
        |> Array.map Str.up1
        |> String.concat ""
        |> (fun x -> if Char.IsLower s.[0] then Str.low1 x else x)

    let replace (a:string) (b:string) (s:string) = s.Replace(a,b)


    type DllRef = {fullPath:string option; fileName:string; nameNoExt:string; copyLocal:bool}
    type FsxRef = {fullPath:string; fileName:string}
    type NugetRef = {name:string; version:string}


    let getRefs(code:string) : ResizeArray<DllRef>*ResizeArray<FsxRef>*ResizeArray<NugetRef>*string= 
        let refs = ResizeArray()
        let nugs = ResizeArray()
        let fsxs = ResizeArray()
        let codeWithoutNugetRefs = StringBuilder()
        for ln in code.Split('\n') do
            let tln = ln.Trim()
            if tln.StartsWith "#r \"nuget" then
                codeWithoutNugetRefs.Append "// "  |> ignore 
                match Str.between "nuget:" "\"" tln with
                |None -> ()
                |Some pkgV ->
                    let pkg,version = 
                        if pkgV.Contains(",")then       pkgV |> Str.splitOnce ","
                        else                            pkgV, "*"
                    nugs.Add {name=pkg.Trim(); version=version.Trim()}

            elif tln.StartsWith "#r " then
                codeWithoutNugetRefs.Append "// "  |> ignore 
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
                codeWithoutNugetRefs.Append "// "  |> ignore 
                let _,path,_ = Str.splitTwice "\""  "\"" tln
                if path <> "" then
                    let fullPath = path.Replace ('\\','/')
                    let nameFsx = fullPath.Split('/') |> Seq.last
                    fsxs.Add{fullPath=fullPath; fileName=nameFsx }

            codeWithoutNugetRefs.AppendLine (ln.TrimEnd()) |> ignore 

        refs, fsxs, nugs, (codeWithoutNugetRefs.ToString())

    let mutable version = "0.1.0.0" // TODO find way to increment

    //if last write is more than 1h ago ask for overwrite permissions
    let overWriteExisting fsProj = 
        let maxAgeHours = 0.5
        let fi = FileInfo(fsProj)
        if fi.Exists then
            let age = DateTime.UtcNow - fi.LastWriteTimeUtc
            if age > (TimeSpan.FromHours maxAgeHours) then
                let msg = sprintf "Do you want to recompile and overwrite the existing files?\r\n \r\n%s\r\n \r\nthat are %.2f days old at\r\n \r\n(This dialog only shows if the last compilation was more than %.1f hours ago.)"fi.FullName age.TotalDays  maxAgeHours
                //match MessageBox.Show(msg, Style.dialogCaption, MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation, MessageBoxDefaultButton.Button2) with  // uses Windows.Forms  
                match MessageBox.Show(msg, Style.dialogCaption, MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.No) with  // uses  WPF
                | MessageBoxResult.Yes-> true
                | MessageBoxResult.No-> false
                | _ -> false
            else
                true
        else
            true

    let getNugsXml (nugs:ResizeArray<NugetRef>) : string = 
           seq{ for nug in nugs  do  "<PackageReference Include=\"" + nug.name + "\" Version=\"" + nug.version + "\" />" }
           |> String.concat (Environment.NewLine  + "    ")

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

    let getFsxXml (projFolder:string, nameSpace, code, fsxloads:ResizeArray<FsxRef>) : string= 
        seq{
            for load in fsxloads do
                let niceName = (load.fileName.Replace(".fsx", "") |> toCamelCase  ) + ".fs" /// TODO make case insensitive
                let newp = IO.Path.Combine(projFolder,niceName)
                if IO.File.Exists newp then IO.File.Delete newp
                IO.File.Copy(load.fullPath,newp)
                yield "<Compile Include=\"" + niceName + "\" />"

            let fsxName = nameSpace + ".fs"
            let fsxPath = IO.Path.Combine(projFolder,fsxName)
            IO.File.WriteAllText(fsxPath,code,Text.Encoding.UTF8)
            yield     "<Compile Include=\"" + fsxName + "\" />"
        }
        |> String.concat (Environment.NewLine + "    ")


    //TODO  use https://github.com/Tyrrrz/CliWrap ??

    let green  msg = ISeffLog.log.PrintfnColor 0   140 0 msg
    let black  msg = ISeffLog.log.PrintfnColor 0   0   0 msg
    let gray   msg = ISeffLog.log.PrintfnColor 190 190 190 msg
    //let grayil msg = ISeffLog.log.PrintfColor  190 190 190 msg

    let msBuild(p:Diagnostics.Process, fsProj,config:Config.Config) = 
        gray "starting MSBuild.exe ..."
        let msBuildFolders = 
            [
            config.Settings.Get "MSBuild.exe" |> Option.defaultValue ""
            @"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
            @"C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"
            @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
            @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe"
            @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"
            @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
            ]

        match msBuildFolders |> Seq.tryFind File.Exists with
        | None -> 
            // TODO use https://github.com/microsoft/MSBuildLocator
            ISeffLog.log.PrintfnIOErrorMsg  "MSBuild.exe not found at:\r\n%s " (msBuildFolders |> String.concat Environment.NewLine)
            ISeffLog.log.PrintfnIOErrorMsg  "If you have MSBuild.exe on your PC please add the path to the settings file like this:"
            ISeffLog.log.PrintfnAppErrorMsg "MSBuild.exe=C:\Folder\Where\it\is\MSBuild.exe"
            ISeffLog.log.PrintfnIOErrorMsg  "the settings file is at %s" config.Hosting.SettingsFileInfo.FullName
            false
        | Some msBuildexe ->
            p.StartInfo.FileName <- "\"" + msBuildexe + "\""
            p.StartInfo.Arguments <- String.concat " " ["\"" + fsProj + "\"" ;  "-restore" ] //; "/property:Configuration=Release"] configuration should be specified in the fsproj file
            true

    let dotnetBuild(p:Diagnostics.Process, fsProj)= 
        // TODO check if dotnet sdk is installed
        gray "starting dotnet build ..."
        p.StartInfo.FileName <- "dotnet"
        p.StartInfo.Arguments <- String.concat " " ["build"; "\"" + fsProj + "\""  ;  "--configuration Release"]
        true


    let compileScript(code, fp:FilePath, useMSBuild,config:Config.Config) = 
        match fp with
        | NotSet -> ISeffLog.log.PrintfnAppErrorMsg "Cannot compile an unsaved script save it first"
        | SetTo fi ->
            async{
                try
                    gray "compiling %s ..." fi.Name
                    let name = fi.Name.Replace(".fsx","")
                    let nameSpace = name |> toCamelCase 
                    let outLiteral = "  " + nameSpace + " -> "
                    let mutable resultDll = "" // found via matching on outLiteral below
                    let folderName = "fsxDll_" + nameSpace
                    let projFolder = IO.Path.Combine(fi.DirectoryName,folderName)
                    let libFolderFull = IO.Path.Combine(projFolder,libFolderName) 
                    IO.Directory.CreateDirectory(libFolderFull)  |> ignore
                    IO.Directory.CreateDirectory(projFolder)  |> ignore
                    let fsProj = IO.Path.Combine(projFolder,nameSpace + ".fsproj")
                    if overWriteExisting fsProj then
                        let refs,fsxs,nugs, codeWithoutNugetRefs = getRefs (code)
                        let fsxXml = getFsxXml(projFolder, nameSpace ,codeWithoutNugetRefs, fsxs)
                        let refXml = getRefsXml(libFolderFull,refs)
                        let nugXml = getNugsXml(nugs)
                        baseXml
                        |> replace "        " "" //clear white space at beginning of lines
                        |> replace "rootNamespace" nameSpace
                        |> replace "assemblyName" nameSpace
                        |> replace "9.9.9.1" version
                        |> replace "9.9.9.2" version
                        |> replace "9.9.9.3" version
                        |> replace "<!--PLACEHOLDER FOR NUGETS-->" nugXml
                        |> replace "<!--PLACEHOLDER FOR REFERENCES-->" refXml
                        |> replace "<!--PLACEHOLDER FOR FILES-->" fsxXml
                        |> fun s ->
                            IO.File.WriteAllText(fsProj,s,Text.Encoding.UTF8)
                            gray "project files created at %s" fsProj
                            //https://stackoverflow.com/questions/1145969/processinfo-and-redirectstandardoutput
                            let p = new System.Diagnostics.Process()
                            p.EnableRaisingEvents <- true
                            let compilerExists = 
                                if useMSBuild then msBuild     ( p, fsProj, config)
                                else               dotnetBuild ( p, fsProj)
                            if compilerExists then
                                ISeffLog.log.PrintfnColor 0 0 200 "%s %s" p.StartInfo.FileName p.StartInfo.Arguments
                                p.StartInfo.UseShellExecute <- false
                                p.StartInfo.CreateNoWindow <- true //true if the process should be started without creating a new window to contain it
                                p.StartInfo.RedirectStandardError <-true
                                p.StartInfo.RedirectStandardOutput <-true
                                // for console also see https://stackoverflow.com/a/1427817/969070
                                p.StartInfo.StandardOutputEncoding <- Text.Encoding.GetEncoding(Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage) //https://stackoverflow.com/a/48436394/969070
                                p.StartInfo.StandardErrorEncoding  <- Text.Encoding.GetEncoding(Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage) //https://stackoverflow.com/a/48436394/969070
                                p.OutputDataReceived.Add ( fun d ->
                                    let txt = d.Data
                                    if not <| isNull txt then // happens often actually
                                        if   txt.Contains "Build FAILED." then      ISeffLog.log.PrintfnColor 220 0 150  "%s" txt
                                        elif txt.Contains "error FS"   then         ISeffLog.log.PrintfnColor 220 0 0  "%s" txt
                                        elif txt.Contains "Build succeeded." then   green  "%s" txt
                                        elif txt.Contains outLiteral  then                                        
                                            resultDll <- txt.Replace(outLiteral,"").Trim()                                        
                                            gray "%s" txt
                                        else
                                            gray "%s" txt
                                        )
                                p.ErrorDataReceived.Add (  fun d -> ISeffLog.log.PrintfnAppErrorMsg "%s" d.Data)
                                p.Exited.Add( fun _ ->
                                    if resultDll <> "" then
                                        gray  "*build done! This line is copied to your clipboard, paste via Ctrl + V :"
                                        ISeffLog.log.PrintfColor  190 0 50 "#r @\""
                                        ISeffLog.log.PrintfColor  0 0 0 "%s" resultDll
                                        ISeffLog.log.PrintfnColor 190 0 50 "\""
                                        FsEx.Wpf.SyncWpf.doSync ( fun () -> Clipboard.SetText("#r @\"" + resultDll + "\"\r\n") )
                                    else
                                        gray  "*build process ended!"
                                    gray "--------------------------------------------------------------------------------"
                                    )
                                p.Start() |> ignore
                                p.BeginOutputReadLine()
                                p.BeginErrorReadLine()
                                //log.PrintfnInfoMsg "compiling to %s" (IO.Path.Combine(projFolder,"bin","Release","netstandard2.0",nameSpace+".dll"))
                                p.WaitForExit()
                with
                    e -> ISeffLog.log.PrintfnAppErrorMsg "%A" e
            } |> Async.Start



