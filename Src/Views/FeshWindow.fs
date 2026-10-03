namespace Fesh.Views

open System
open Avalonia.Media.Imaging
open System.Runtime.InteropServices
open Fesh.Model
open Fesh.Config
open Avalonia.Controls

/// A class holding the main WPF Window
/// Includes loading icon
type FeshWindow (config:Config)=

    let win =
        let w = new Fittings.PositionedWindow(config.RunContext.PositionedWindowSettingsFileInfo, IFeshLog.printError)
        IEditor.mainWindow <- w
        w

    let mutable wasMax = win.Settings.GetBool ("WindowIsMax", false) //indicating if the Window was in Full-screen mode before switching to temporary Log only full-screen

    // for Title Bar:
    let appName =
        match config.RunContext.HostName with
        |None     -> "Fesh"          //, a Scripting editor for fsharp"
        |Some n   -> "Fesh for " + n //, a Scripting editor for fsharp in " + n

    let plat =
        if Environment.Is64BitProcess then "64bit" else "32bit"


    let removeTrailingZerosAndPoints (s:string) =
        let rec loop (s:string) =
            let es = s.Split('.')
            let last = Array.last es
            let lastTrimmed = last.Trim().Replace("0","")// remove block of zeros like .000 from end
            if lastTrimmed.Length = 0 && es.Length > 1 then // es.Length > 1 to not loop forever on "0" or ""
                String.Join(".", Array.truncate (es.Length-1) es) |> loop
            else
                s
        loop s

    let version =
        let ass =
            match config.RunContext.HostAssembly with
            |Some j -> j
            |None   -> Reflection.Assembly.GetAssembly(typeof<FeshWindow>)
        let v = ass.GetName().Version
        "v" + (v.ToString())//|> removeTrailingZerosAndPoints)
        // $"v{v.Major}.{v.Minor}.{v.Revision}"  + if  v.MinorRevision <> 0s then $".{v.MinorRevision}" else ""

    let fsCore  =
        let ass = [].GetType().Assembly
        // The AssemblyVersion of FSharp.Core is only Major.Minor.0.0 (e.g. 10.1.0.0),
        // the full nuget version (e.g. 10.1.303) is only in the informational version: 10.1.303-servicing.26377.103+e730f1d..
        let v =
            match Attribute.GetCustomAttribute(ass, typeof<Reflection.AssemblyInformationalVersionAttribute>) with
            | :? Reflection.AssemblyInformationalVersionAttribute as a when not (String.IsNullOrWhiteSpace a.InformationalVersion) ->
                a.InformationalVersion.Split('-','+').[0]
            | _ ->
                ass.GetName().Version.ToString() |> removeTrailingZerosAndPoints
        "Fsharp.Core." + v

    let frameW =
        RuntimeInformation.FrameworkDescription |> removeTrailingZerosAndPoints
        // let d = RuntimeInformation.FrameworkDescription
        // let t = if d.EndsWith ".0" then d[..^2] else d
        // $"{t}"

    do
        //Add Icon:
        let defaultUri = Uri "avares://Fesh/Media/logo.ico"
        try
            let setIcon (uri:Uri)=
                let bitmap = new Bitmap(Avalonia.Platform.AssetLoader.Open uri)
                win.Icon <- new WindowIcon(bitmap)
            // Add the Icon at the top left of the window and in the status bar, musst be called  after loading window.
            // Media/logo.ico with Build action : "Resource"// WPF only
            // (for the exe file icon in explorer use <Win32Resource>Media/logo.res</Win32Resource>  in fsproj, where the .res file contains the .ico file )
            // let defaultUri = Uri("pack://application:,,,/Fesh;component/Media/logo.ico") // WPF only
            match config.RunContext.Logo with
            |Some uri ->
                try         setIcon uri
                with  _ ->  setIcon defaultUri
            |None ->
                setIcon defaultUri
        with ex ->
            config.Log.PrintfnAppErrorMsg  $"Failed to load {defaultUri} from Application.ResourceStream : {ex}"

    /// The main WPF Window
    member this.Window : Fittings.PositionedWindow = win //:> Avalonia.Window // cast to a Fittings.PositionedWindow

    /// Indicating if the Window is in Full-screen mode or minimized mode (not normal mode)
    member this.IsMinOrMax = win.IsMinOrMax

    /// Indicating if the Window was in Full-screen mode before switching to temporary Log only full-screen
    member this.WasMax
        with get() = wasMax
        and set(v) = wasMax <- v

    member this.SetFileNameInTitle (fp:FilePath) =
        match fp with
        |NotSet dummyName ->
            let txt =
                [
                dummyName
                appName
                version
                plat
                frameW
                fsCore
                ] |> String.concat "  -  "
            win.Title <- txt

        |Deleted fi |SetTo fi ->
            let txt =
                [
                fi.Name
                appName
                version
                plat
                frameW
                fsCore
                fi.DirectoryName
                ] |> String.concat "  -  "
            win.Title <- txt






