namespace Fesh.Config

open System
open Fesh.Model
open System.IO



/// hostName: a string for the name of the hosting App (will be used for settings file name an displayed in the Title Bar.
/// mainWindowHandle: Pointer to main window(nativeInt),
/// fsiCanRun: a function to check if evaluation of fsi is currently allowed
/// logo: optional a URI to an alternative logo for hosted mode default is Uri("pack://application:,,,/Fesh;component/Media/logo.ico")
/// hostAssembly: to get version number of hosting assembly
type HostedStartUpData = {
    hostName:string
    mainWindowHandel: nativeint
    fsiCanRun: unit-> bool
    logo:option<Uri>
    defaultCode:option<string>
    hostAssembly :option<Reflection.Assembly>
    }

module Folders =
    let appDataLocal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
    let validHost(n:string)=
        let mutable n = n
        for c in IO.Path.GetInvalidFileNameChars() do
            n <- n.Replace(c, '_') // make sure host name is a valid file name
        n


open Folders

/// A class to hold the current App Run context (Standalone or Hosted)
type RunContext (host:HostedStartUpData option) =

    let isRunningOnDotNetCore =
        Runtime.InteropServices.RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework", StringComparison.OrdinalIgnoreCase) |> not
        //Type.GetType("System.Runtime.Loader.AssemblyLoadContext") <> null // https://github.com/dotnet/runtime/issues/22779#issuecomment-315527735

    let settingsFolder =
        let path =
            match host with
            |None    ->
                if isRunningOnDotNetCore then IO.Path.Combine(appDataLocal,$"Fesh.Settings")  // Standalone
                else                          IO.Path.Combine(appDataLocal,$"Fesh.net48.Settings")  // Standalone .NET Framework
            |Some sd ->                       IO.Path.Combine(appDataLocal,$"Fesh.{validHost sd.hostName}.Settings")
        IO.Directory.CreateDirectory(path) |> ignore
        path

    let settingsFileInfo =
        IO.Path.Combine(settingsFolder, "Settings.txt")
        |> IO.FileInfo

    let positionedWindowSettingsFileInfo =
        IO.Path.Combine(settingsFolder, "Position-of-Window-on-Screen.txt")
        |> IO.FileInfo


    /// To get a path where to save the setting files, give file name including extension
    member this.GetPathToSaveAppData (fileNameInclExt:string) =
        IO.Path.Combine(settingsFolder, fileNameInclExt )

    member this.SettingsFileInfo = settingsFileInfo

    member this.PositionedWindowSettingsFileInfo = positionedWindowSettingsFileInfo

    member this.FsiCanRun    = match host with None ->  true | Some d -> d.fsiCanRun()

    member this.HostName     = match host with None ->  None | Some d -> Some d.hostName

    /// to get version number of hosting assembly
    member this.HostAssembly = match host with None ->  None | Some d -> d.hostAssembly

    member this.IsHosted     = match host with None ->  false| Some _ -> true

    member this.IsStandalone = match host with None ->  true | Some _ -> false

    member this.Logo         = match host with None ->  None | Some d -> d.logo

    member this.DefaultCode  = match host with None -> None | Some sd -> sd.defaultCode

    member this.IsRunningOnDotNetCore = isRunningOnDotNetCore

    /// opens up Explorer.exe
    member this.OpenSettingsFolder()=
        Diagnostics.Process.Start("explorer.exe", "\"" + settingsFolder+ "\"") |> ignore

    /// opens up Explorer.exe with folder of Fesh.exe
    member this.OpenAppFolder()=
        let ass = Reflection.Assembly.GetExecutingAssembly()
        if isNull ass then
            IFeshLog.log.PrintfnIOErrorMsg "OpenAppFolder: GetExecutingAssembly() is null"
        else
            if ass.IsDynamic then
                IFeshLog.log.PrintfnIOErrorMsg "Can get path of %A" ass.FullName
            else
                let folder = IO.Path.GetDirectoryName( ass.Location)
                Diagnostics.Process.Start("explorer.exe", "\"" + folder+ "\"") |> ignore

    // let settingFile =
    //     [|
    //     "AutoCompleteStatistic.txt"
    //     "CurrentlyOpenFiles.txt"
    //     "DefaultCode.fsx"
    //     "FoldingStatus.txt"
    //     "FsiArguments.txt"
    //     "PositionedWindow.txt"
    //     "RecentlyUsedFiles.txt"
    //     "Settings.txt"
    //     |]

    // // restore settings files from version 0.15.0 or lower
    // let restoreSettingsFolder(oldFolder:string, newFolder:string) =
    //     try
    //         let oldSett = IO.Path.Combine(oldFolder, "Settings.txt")
    //         let newSett = IO.Path.Combine(newFolder, "Settings.txt")
    //         if IO.File.Exists oldSett && not ( IO.File.Exists newSett) then
    //             for f in settingFile do
    //                 let oldFile= IO.Path.Combine(oldFolder, f)
    //                 let newFile = IO.Path.Combine(newFolder, f)
    //                 if IO.File.Exists oldFile && not (IO.File.Exists newFile) then
    //                     IO.File.Move(oldFile,newFile)
    //     with e ->
    //         IFeshLog.log.PrintfnIOErrorMsg $"Error while trying to restore old settings file: {e}"




    // let restoreSettingsFolderFromOldLocations() =
    //     let oldFolder = // before version 0.15.0
    //         match host with
    //         |None ->    IO.Path.Combine(appDataLocal, "Fesh") // Standalone
    //         |Some _ ->  IO.Path.Combine(appDataLocal, "Fesh", appSuffix)
    //     restoreSettingsFolder(oldFolder, settingsFolder)
    //     let oldFolder = // before version 0.17.0
    //         match host with
    //         |None ->    IO.Path.Combine(appDataLocal, "Fesh", "Settings", "Standalone") // Standalone
    //         |Some _ ->  IO.Path.Combine(appDataLocal, "Fesh", "Settings", appSuffix)
    //     restoreSettingsFolder(oldFolder, settingsFolder)