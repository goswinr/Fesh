namespace Fesh.Config

open System.IO
open Fittings
open Fesh.Model


type ScriptCompilerFsproj ( runContext:RunContext) =

    let filePath0 = runContext.GetPathToSaveAppData("Script-Compiler-Template-Project.fsproj")

    let writer = SaveReadWriter(filePath0,IFeshLog.printError)


    // TODO really only <PlatformTarget>x64</PlatformTarget><!--  x64 is required e.g by Rhino, don't us just 'Platform' tag-->   ??

    let target = if runContext.IsRunningOnDotNetCore then "net8.0-windows" else "net48"

    let defaultFsproj = $$"""<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">
  <!-- all values that are enclose in curly brackets {} will be replaced by Fesh ScriptCompiler -->
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>{{target}}</TargetFramework>
    <LangVersion>preview</LangVersion>
    <SatelliteResourceLanguages>en</SatelliteResourceLanguages> <!--to only have the English resources of Fsharp.Core-->

    <RootNamespace>{rootNamespace}</RootNamespace> <!-- set by Fesh ScriptCompiler-->
    <AssemblyName>{assemblyName}</AssemblyName>    <!-- set by Fesh ScriptCompiler-->

    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NeutralLanguage>en</NeutralLanguage>
    <Configuration>Release</Configuration>

    <Version>{version}</Version><!-- set by Fesh ScriptCompiler-->
    <AssemblyVersion>{version}</AssemblyVersion><!-- set by Fesh ScriptCompiler-->
    <FileVersion>{version}</FileVersion><!-- set by Fesh ScriptCompiler-->

    <!--<PlatformTarget>x64</PlatformTarget> -->

  </PropertyGroup>

  <ItemGroup>
  <!-- <PackageReference Update="FSharp.Core" Version="5.0.2" /> included only if the current SDK forces a to high version-->
    {nuget-packages} <!-- set by Fesh ScriptCompiler-->
  </ItemGroup>

  <ItemGroup>
    {dll-file-references} <!-- set by Fesh ScriptCompiler-->
  </ItemGroup>

  <ItemGroup>
    {code-files} <!-- set by Fesh ScriptCompiler-->
  </ItemGroup>

  <!--
  <Target Name="DeleteObjFolder" BeforeTargets="AfterBuild"> <RemoveDir Directories="obj" ContinueOnError="false" /> </Target>
  -->

</Project>
"""


    member this.FileInfo = FileInfo(filePath0)

    ///loads sync
    member this.Get() =
        writer.CreateFileIfMissing(defaultFsproj)  |> ignore // create file so it can be found and edited manually
        match writer.ReadAllText() with
        |None -> defaultFsproj
        |Some code -> code


    /// The name of the subfolder for precompiled libraries
    /// By default 'lib'
    static member val LibFolderName = "lib" with get, set

    /// The assembly version written.
    /// by default 1.0.0
    static member val AssemblyVersionToWrite = "1.0.0" with get, set


