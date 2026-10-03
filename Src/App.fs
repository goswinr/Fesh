namespace Fesh

open System
open Avalonia
open Fesh.Config
open Velopack
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Themes.Fluent
open System

module App =
    open Avalonia.Media


    /// To statically access the currently running instance.
    /// For debugging only
    let mutable current = Unchecked.defaultof<Fesh>

    /// mainWindowHandle: Pointer to main window(nativeInt),
    /// hostName: a string for the name of the hosting App (will be used for settings file name an displayed in the Title Bar.)
    /// fsiCanRun: a function to check if evaluation of fsi is currently allowed
    /// Call fesh.Window.Show() on the returned Fesh object.
    /// Use fesh.Fsi.OnStarted and fesh.Fsi.OnIsReady Events to implement undo and redo in host App.
    let createEditorForHosting (host:HostedStartUpData) : Fesh =
        current <- Initialize.everything (Some host , [| |])
        if host.mainWindowHandel <> IntPtr.Zero then
            // so that the editor window opens and closes at the same time as the main host window:
            // Interop.WindowInteropHelper(current.Window).Owner <- host.mainWindowHandel
            ()

        //win.Show() // do in host instead, so that the host can control the window show time
        current

    type FeshApp() =
        inherit Application()

        override this.Initialize() =
            this.Styles.Add (FluentTheme())
            // this.RequestedThemeVariant <- Styling.ThemeVariant.Light
            this.RequestedThemeVariant <- Styling.ThemeVariant.Dark

            // https://github.com/AvaloniaUI/AvaloniaEdit/issues/322:s
            this.Styles.Add(Avalonia.Markup.Xaml.Styling.StyleInclude(baseUri = null, Source = Uri "avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml"))

        override this.OnFrameworkInitializationCompleted() =
            match this.ApplicationLifetime with
            | :? IClassicDesktopStyleApplicationLifetime as desktopLifetime ->
                    current <- Initialize.everything (None, desktopLifetime.Args)
                    desktopLifetime.MainWindow <- current.Window
                    // current.Window.Background <- Brushes.White |> AvaloniaLog.ImmBrush.darker 5 //otherwise it is transparent !?
            | _ -> ()


    // let runEditorStandalone (args: string []) : int =
    //     // VelopackApp.Build()
    //     //     .SetAutoApplyOnStartup(false) // to not install updates even if they are downloaded
    //     //     .Run() //https://docs.velopack.io/getting-started/csharp

    //     // let app  = Application() // do first so that pack Uris work
    //     // current <- Initialize.everything (None, args)

    //     // app.Run current.Window
    //     0



    [<EntryPoint>]
    let main(args: string[]) =
        AppBuilder
            .Configure<FeshApp>()
            .UsePlatformDetect()
            // .UseSkia()
            .StartWithClassicDesktopLifetime(args)