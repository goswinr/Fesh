namespace Fittings

[<RequireQualifiedAccess>]
module WebBrowser =

    open System.Diagnostics
    open System.Runtime.InteropServices
    open System

    let goto url =
        try
            if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
                use proc = new Process()
                proc.StartInfo.UseShellExecute <- true
                proc.StartInfo.FileName <- url
                proc.Start() |> ignore
            elif RuntimeInformation.IsOSPlatform(OSPlatform.Linux) then
                Process.Start("x-www-browser", url) |> ignore
            elif RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then
                Process.Start("open", url) |> ignore
            else
                eprintfn $"Unsupported OS for opening URL:{RuntimeInformation.OSDescription}"
        with ex ->
            eprintfn $"Error opening URL failed '{url}' {Environment.NewLine}{ex.Message}"
            raise ex


    let goToAction url =
        System.Action(fun () -> goto url)