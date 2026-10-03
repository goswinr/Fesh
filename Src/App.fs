namespace Fesh

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Markup.Xaml.Styling
open Avalonia.Themes.Fluent
open Fesh.Config
open Velopack

module App =

    /// To statically access the currently running instance.
    /// For debugging only
    let mutable current = Unchecked.defaultof<Fesh>

    /// The styles of the AvaloniaEdit TextEditor, they are not part of the Fluent theme.
    /// https://github.com/AvaloniaUI/AvaloniaEdit/issues/322
    let private avaloniaEditStyles = Uri "avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml"

    type FeshApp() =
        inherit Application()

        override this.Initialize() =
            this.Styles.Add (FluentTheme())
            this.RequestedThemeVariant <- Styling.ThemeVariant.Light
            // this.RequestedThemeVariant <- Styling.ThemeVariant.Dark
            this.Styles.Add(StyleInclude(baseUri = null, Source = avaloniaEditStyles))

        override this.OnFrameworkInitializationCompleted() =
            match this.ApplicationLifetime with
            | :? IClassicDesktopStyleApplicationLifetime as desktopLifetime ->
                    current <- Initialize.everything (None, desktopLifetime.Args)
                    desktopLifetime.MainWindow <- current.Window
                    // current.Window.Background <- Brushes.White |> AvaloniaLog.ImmBrush.darker 5 //otherwise it is transparent !?
            | _ -> () // when hosted there is no lifetime, the host creates the editor via createEditorForHosting


    /// Makes sure that Avalonia is set up in this process.
    let private ensureAvaloniaIsSetUp() =
        match Application.Current with
        | null ->
            // The host is not an Avalonia app itself (e.g. Rhino, Revit or AutoCAD), so there is no Avalonia Application yet.
            // It gets set up without starting a main loop. The host keeps running its own message loop, that drives the Avalonia windows too.
            AppBuilder
                .Configure<FeshApp>()
                .UsePlatformDetect()
                .SetupWithoutStarting()
                |> ignore<AppBuilder>
        | app ->
            // The host is an Avalonia app. Its Application needs the styles of AvaloniaEdit too.
            let hasStyles = app.Styles |> Seq.exists (function :? StyleInclude as si -> si.Source = avaloniaEditStyles | _ -> false)
            if not hasStyles then
                app.Styles.Add(StyleInclude(baseUri = null, Source = avaloniaEditStyles))


    /// mainWindowHandle: Pointer to main window(nativeInt),
    /// hostName: a string for the name of the hosting App (will be used for settings file name an displayed in the Title Bar.)
    /// fsiCanRun: a function to check if evaluation of fsi is currently allowed
    /// Call this on the UI thread of the host.
    /// If the host is not an Avalonia app, Avalonia gets set up on the calling thread.
    /// Call fesh.Window.Show() on the returned Fesh object.
    /// Use fesh.Fsi.OnStarted and fesh.Fsi.OnIsReady Events to implement undo and redo in host App.
    let createEditorForHosting (host:HostedStartUpData) : Fesh =
        ensureAvaloniaIsSetUp()
        current <- Initialize.everything (Some host , [| |])

        //win.Show() // do in host instead, so that the host can control the window show time
        current


    [<EntryPoint>]
    [<STAThread>]
    let main(args: string[]) =
        VelopackApp.Build()
            .SetAutoApplyOnStartup(false) // to not install updates even if they are downloaded
            .Run() //https://docs.velopack.io/getting-started/csharp

        try
            AppBuilder
                .Configure<FeshApp>()
                .UsePlatformDetect()
                // .UseSkia()
                .StartWithClassicDesktopLifetime(args)
        with e ->
            eprintfn $"Fesh Application Run Error:\r\n{e}"
            1
