﻿namespace Seff.Config

open System
open Seff.Model


/// mainWindowHandle: Pointer to main window(nativeInt),
/// hostName: a string for the name of the hosting App (will be used for settings file name an displayed in the Title Bar.
/// fsiCanRun: a function to check if evaluation of fsi is currently allowed
/// logo: optional a URI to an alternative logo for hosted mode default is Uri("pack://application:,,,/Seff;component/Media/logo.ico")
type HostedStartUpData = {
    hostName:string
    mainWindowHandel: nativeint
    fsiCanRun: unit-> bool
    logo:option<Uri>
    }

//type StartupMode =  Standalone  | Hosted of HostedStartUpData


/// A class to hold the current App Run context (Standalone or Hosted)
type Hosting (startUpData:HostedStartUpData option) = 

    let isRunningOnDotNetCore = 
        Type.GetType("System.Runtime.Loader.AssemblyLoadContext") <> null //https://github.com/dotnet/runtime/issues/22779#issuecomment-315527735

    let configFolder = 
        let appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        let p = IO.Path.Combine(appData,"Seff")
        IO.Directory.CreateDirectory(p) |> ignore
        p

    let hostName = //to get a valid filename from any host app name supplied not allowed : < > : " / \ | ? *
        match startUpData with
        |None ->  "Standalone"
        |Some sd ->
            let mutable n = sd.hostName
            for c in IO.Path.GetInvalidFileNameChars() do  n <- n.Replace(c, '_')
            "Hosted." + n

    let settingsFileInfo = 
        IO.Path.Combine(configFolder, hostName + ".Settings.txt")
        |> IO.FileInfo

    /// To get a path where to save the setting files
    member this.GetPathToSaveAppData (fileNameInclExt) = 
        let file = sprintf "%s.%s" hostName fileNameInclExt
        IO.Path.Combine(configFolder, file )

    member this.ConfigFolder = configFolder

    member this.SettingsFileInfo = settingsFileInfo

    member this.FsiCanRun    =   match startUpData with None ->  true  | Some d -> d.fsiCanRun()

    member this.HostName     =  match startUpData with None ->  None | Some d -> Some d.hostName

    member this.IsHosted     = match startUpData with None ->  false | Some _ -> true

    member this.IsStandalone = match startUpData with None ->  true  | Some _ -> false

    member this.Logo         = match startUpData with None ->  None | Some d -> d.logo

    member this.IsRunningOnDotNetCore = isRunningOnDotNetCore

    /// opens up Explorer.exe
    member this.OpenSettingsFolder()= 
        Diagnostics.Process.Start("explorer.exe", "\"" + configFolder+ "\"")        |> ignore

    /// opens up Explorer.exe with folder of Seff.exe
    member this.OpenAppFolder()= 
        let ass = Reflection.Assembly.GetExecutingAssembly()
        if isNull ass then
            ISeffLog.log.PrintfnIOErrorMsg "OpenAppFolder: GetExecutingAssembly() is null"
        else
            if ass.IsDynamic then
                ISeffLog.log.PrintfnIOErrorMsg "Can get path of %A" ass.FullName
            else
                let folder = IO.Path.GetDirectoryName( ass.Location)
                Diagnostics.Process.Start("explorer.exe", "\"" + folder+ "\"")        |> ignore



