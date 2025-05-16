namespace Fesh.Config

open System
open Fesh.Model

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
    canRunAsync: bool
    }

// /// OptionalAttribute for member parameters
// type internal OPT = Runtime.InteropServices.OptionalAttribute

// /// DefaultParameterValueAttribute for member parameters
// type internal DEF =  Runtime.InteropServices.DefaultParameterValueAttribute

// type HostConfig (
//     mainWindowHandle:nativeint,
//     [<OPT;DEF("a FeshHostingApp")>]hostName:string,
//     [<OPT;DEF(null:Func<unit,bool>)>]fsiCanRun:Func<unit,bool>,
//     [<OPT;DEF(null:Uri)>] logo:Uri,
//     [<OPT;DEF("")>] defaultCode:string,
//     [<OPT;DEF(null:Reflection.Assembly)>]  hostAssembly:Reflection.Assembly
//     ) =

//     member this.hostName = hostName
//     member this.mainWindowHandle = mainWindowHandle
//     member this.fsiCanRun = fsiCanRun|> Option.ofObj
//     member this.logo = logo |> Option.ofObj
//     member this.defaultCode = defaultCode
//     member this.hostAssembly = hostAssembly |> Option.ofObj

module Folders =

    let validHost(n:string)=
        let mutable n = n
        for c in IO.Path.GetInvalidFileNameChars() do
            n <- n.Replace(c, '_') // make sure host name is a valid file name
        n

    let contains (sd:HostedStartUpData) (s:string) =
        sd.hostName.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0


    let createMutualShortcuts(pathApp:string ,pathSettings:string) =
        async{
            try
                let pathApp = pathApp.Replace('\\', '/')
                let pathSettings = pathSettings.Replace('\\', '/')

                IO.File.WriteAllLines ( IO.Path.Combine(pathApp, "Settings Folder.url") , [|
                    @"[InternetShortcut]"
                    $"URL=file:///{pathSettings}"
                    @"IconIndex=3"
                    @"IconFile=C:\WINDOWS\System32\SHELL32.dll"
                    |] )

                IO.File.WriteAllLines ( IO.Path.Combine(pathSettings, "App Folder.url") , [|
                    @"[InternetShortcut]"
                    $"URL=file:///{pathApp}"
                    @"IconIndex=3"
                    @"IconFile=C:\WINDOWS\System32\SHELL32.dll"
                    |] )

            with e ->
                eprintfn $"Error while trying to create mutual shortcuts: {e}"

        } |> Async.Start


open Folders

/// A class to hold the current App Run context (Standalone or Hosted)
type RunContext (host:HostedStartUpData option) =

    let isRunningOnDotNetCore =
        Runtime.InteropServices.RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework", StringComparison.OrdinalIgnoreCase)
        |> not
        //Type.GetType("System.Runtime.Loader.AssemblyLoadContext") <> null // https://github.com/dotnet/runtime/issues/22779#issuecomment-315527735



    let settingsFolder =
        // Because reinstalling the app with Velopack will delete the 'current' folder and its 'Settings' sibling, put the Settings outside the Fesh folder.
        // use Roaming AppData folder for Settings as suggested by https://docs.velopack.io/integrating/preserved-files#application-settings
        // unless in portable mode
        let settingsPath =
            match host with
            |None    ->
                let fi = IO.FileInfo(Reflection.Assembly.GetAssembly(typeof<HostedStartUpData>).Location )
                //Because reinstalling the app with Velopack will delete the 'current' folder and its 'Settings' sibling,
                // put the Settings outside the main Fesh folder.
                let mainFolder = fi.Directory.Parent //  the folder called 'Fesh, the parent of 'current'
                if IO.File.Exists(IO.Path.Combine(mainFolder.FullName, ".portable")) then
                    IO.Path.Combine(mainFolder.FullName, "Settings")  // for portable version don't have an outside folder
                else
                    let roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) // Roaming AppData folder
                    if isRunningOnDotNetCore then IO.Path.Combine(roamingAppData, $"Fesh", "Settings")  // Standalone
                    else                          IO.Path.Combine(roamingAppData, $"Fesh.net48", "Settings")  // Standalone .NET Framework

            |Some sd ->
                let roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) // Roaming AppData folder
                if   contains sd "Rhino" then   IO.Path.Combine(roamingAppData,  "Fesh.Rhino", "Settings") // don't use C:\Users\gwins\AppData\Roaming\McNeel\Rhinoceros\packages\8.0\Fesh\Fesh.Rhino.Settings
                elif contains sd "Revit" then   IO.Path.Combine(roamingAppData,  "Fesh.Revit", "Settings") // use same of net48 and net8
                elif contains sd "AutoCAD" then IO.Path.Combine(roamingAppData,  "Fesh.AutoCAD", "Settings")
                else                            IO.Path.Combine(roamingAppData, $"Fesh.{validHost sd.hostName}", "Settings")

        if not (IO.Directory.Exists settingsPath) then
            IO.Directory.CreateDirectory(settingsPath) |> ignore
            let appFi = IO.FileInfo(Reflection.Assembly.GetAssembly(typeof<HostedStartUpData>).Location )
            createMutualShortcuts(appFi.Directory.Parent.FullName, settingsPath )
        settingsPath

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

    member this.CanRunAsync  = match host with None ->  true | Some d -> d.canRunAsync // standalone only runs async

    member this.IsStandalone = match host with None ->  true | Some _ -> false

    member this.Logo         = match host with None ->  None | Some d -> d.logo

    member this.DefaultCode  = match host with None -> None | Some sd -> sd.defaultCode

    member this.IsRunningOnDotNetCore = isRunningOnDotNetCore

    /// opens up Explorer.exe
    member this.OpenSettingsFolder()=
        let psi = new Diagnostics.ProcessStartInfo()
        psi.UseShellExecute <- true // default chnaged from net48 to net8
        psi.FileName <- "Explorer.exe"
        psi.Arguments <- "\"" + settingsFolder+ "\""
        Diagnostics.Process.Start(psi) |> ignore
        // Diagnostics.Process.Start("explorer.exe", "\"" + settingsFolder+ "\"") |> ignore

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
                let psi = new Diagnostics.ProcessStartInfo()
                psi.UseShellExecute <- true // default chnaged from net48 to net8
                psi.FileName <- "Explorer.exe"
                psi.Arguments <- "\"" + folder+ "\""
                Diagnostics.Process.Start(psi) |> ignore
                // Diagnostics.Process.Start("explorer.exe", "\"" + folder+ "\"") |> ignore

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