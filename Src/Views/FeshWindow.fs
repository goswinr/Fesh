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


    let removeTrailingZerosOrPoints (s:string) =
        let rec loop (s:string) =
            if s.EndsWith "0" then loop (s.Substring(0,s.Length-1))
            elif s.EndsWith "." then s.Substring(0,s.Length-1)
            else s
        loop s

    let version =
        let ass =
            match config.RunContext.HostAssembly with
            |Some j -> j
            |None   -> Reflection.Assembly.GetAssembly(typeof<FeshWindow>)
        let v = ass.GetName().Version
        "v" + (v.ToString())//|> removeTrailingZerosOrPoints)
        // $"v{v.Major}.{v.Minor}.{v.Revision}"  + if  v.MinorRevision <> 0s then $".{v.MinorRevision}" else ""

    let fsCore  =
        let v = [].GetType().Assembly.GetName().Version
        "Fsharp.Core." + (v.ToString()|> removeTrailingZerosOrPoints)
        // $"Fsharp.Core {v.Major}.{v.Minor}.{v.Revision}"  + if  v.MinorRevision <> 0s then $".{v.MinorRevision}" else ""

    let frameW =
        RuntimeInformation.FrameworkDescription |> removeTrailingZerosOrPoints
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






