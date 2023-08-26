namespace Seff.Config

open System.IO
open FsEx.Wpf
open Seff.Model


type ScriptCompilerFsproj ( runContext:RunContext) = 

    let filePath0 = runContext.GetPathToSaveAppData("ScriptCompiler.fsproj")

    let writer = SaveReadWriter(filePath0,ISeffLog.printError)
        

    // TODO really only <PlatformTarget>x64</PlatformTarget><!--  x64 is required e.g by Rhino, don't us just 'Platform' tag-->   ??

    let defaultFsproj = """<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">
  <!-- all values that are enclose in curly brackets {} will be replaced by Seff ScriptCompiler -->
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>preview</LangVersion>
    <SatelliteResourceLanguages>en</SatelliteResourceLanguages> <!--to only have the English resources of Fsharp.Core-->

    <RootNamespace>{rootNamespace}</RootNamespace> <!-- set by Seff ScriptCompiler-->
    <AssemblyName>{assemblyName}</AssemblyName>    <!-- set by Seff ScriptCompiler-->

    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NeutralLanguage>en</NeutralLanguage>
    <Configuration>Release</Configuration>

    <Version>{version}</Version><!-- set by Seff ScriptCompiler-->
    <AssemblyVersion>{version}</AssemblyVersion><!-- set by Seff ScriptCompiler-->
    <FileVersion>{version}</FileVersion><!-- set by Seff ScriptCompiler-->

    <!--<PlatformTarget>x64</PlatformTarget> -->

  </PropertyGroup>

  <ItemGroup>
  <PackageReference Update="FSharp.Core" Version="5.0.2" /> <!--included so that the current SDK doesn't force a maybe to high version-->
    {nuget-packages} <!-- set by Seff ScriptCompiler-->
  </ItemGroup>
  
  <ItemGroup>
    {dll-file-references} <!-- set by Seff ScriptCompiler-->
  </ItemGroup>
  
  <ItemGroup>
    {code-files} <!-- set by Seff ScriptCompiler-->
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


