namespace Seff.Config

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

/// A class to hold the current App Run context (Standalone or Hosted)
type RunContext (startUpData:HostedStartUpData option) = 

    let isRunningOnDotNetCore = 
        Runtime.InteropServices.RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework", StringComparison.OrdinalIgnoreCase) |> not
        //Type.GetType("System.Runtime.Loader.AssemblyLoadContext") <> null // https://github.com/dotnet/runtime/issues/22779#issuecomment-315527735

    let settingsFolder = 
        let appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        let path = 
            match startUpData with
            |None ->  
                IO.Path.Combine(appData,"Seff") // Standalone
            |Some sd ->
                let mutable host = sd.hostName
                for c in IO.Path.GetInvalidFileNameChars() do host <- host.Replace(c, '_') // make sure host name is a valid file name
                IO.Path.Combine(appData,"Seff",host) 
        
        IO.Directory.CreateDirectory(path) |> ignore
        path

    let settingsFileInfo = 
        IO.Path.Combine(settingsFolder, "Settings.txt")
        |> IO.FileInfo

    let positionedWindowSettingsFileInfo = 
        IO.Path.Combine(settingsFolder, "PositionedWindow.txt")
        |> IO.FileInfo

    /// To get a path where to save the setting files, give file name including extension
    member this.GetPathToSaveAppData (fileNameInclExt:string) = 
        IO.Path.Combine(settingsFolder, fileNameInclExt )

    member this.SettingsFolder = settingsFolder
    
    member this.SettingsFileInfo = settingsFileInfo

    member this.PositionedWindowSettingsFileInfo = positionedWindowSettingsFileInfo

    member this.FsiCanRun    = match startUpData with None ->  true | Some d -> d.fsiCanRun()

    member this.HostName     = match startUpData with None ->  None | Some d -> Some d.hostName

    member this.IsHosted     = match startUpData with None ->  false| Some _ -> true

    member this.IsStandalone = match startUpData with None ->  true | Some _ -> false

    member this.Logo         = match startUpData with None ->  None | Some d -> d.logo

    member this.IsRunningOnDotNetCore = isRunningOnDotNetCore

    /// opens up Explorer.exe
    member this.OpenSettingsFolder()= 
        Diagnostics.Process.Start("explorer.exe", "\"" + settingsFolder+ "\"")        |> ignore

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



