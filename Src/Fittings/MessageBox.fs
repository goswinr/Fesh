namespace Fittings

[<RequireQualifiedAccess>]
type MessageBoxButton =
    | OK
    | YesNo
    | YesNoCancel

[<RequireQualifiedAccess>]
type MessageBoxImage =
    | Exclamation
    | Question
    | Error

[<RequireQualifiedAccess>]
type MessageBoxResult =
    | Yes
    | No
    | OK
    | Cancel

[<RequireQualifiedAccess>]
type MessageBoxOptions =
    | None

open MsBox.Avalonia // <PackageReference Include="MessageBox.Avalonia" Version="3.2.0" />

open System
open System.Diagnostics
open System.Runtime.InteropServices
open Avalonia.Controls
open MsBox.Avalonia.Dto
open MsBox.Avalonia.Models

module Browse =
    let hyperLinkAction = Action(fun () ->
        let url = "https://docs.avaloniaui.net/"
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
            raise (Exception("invalid url: " + url))
        )

[<RequireQualifiedAccess>]
type MessageBox =

    static member Show(
        _host: Avalonia.Controls.Window,
        message: string,
        title: string,
        _button: MessageBoxButton,
        _image: MessageBoxImage,
        _defaultResult: MessageBoxResult,
        _options: MessageBoxOptions,
        cont:MessageBoxResult -> unit) : unit =

            let opt =
                new MessageBoxCustomParams(
                    ButtonDefinitions = [
                        new ButtonDefinition(Name = "Yes")
                        new ButtonDefinition(Name = "No")
                        new ButtonDefinition(Name = "Cancel")
                    ],
                    ContentTitle = title,
                    ContentMessage = message,
                    Icon = MsBox.Avalonia.Enums.Icon.Warning,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    MaxWidth = 800,
                    MaxHeight = 700,
                    SizeToContent = SizeToContent.WidthAndHeight,
                    ShowInCenter = true,
                    Topmost = false,
                    HyperLinkParams = new HyperLinkParams(
                        Text = "https://docs.avaloniaui.net/",
                        Action = Browse.hyperLinkAction
                    )
                )


            let box = MessageBoxManager.GetMessageBoxCustom(opt)
            task{
                let! res = box.ShowWindowDialogAsync(_host)
                let result =
                    match res with
                    | "Yes"     -> MessageBoxResult.Yes
                    | "No"      -> MessageBoxResult.No
                    | "Cancel"  -> MessageBoxResult.Cancel
                    | _ -> MessageBoxResult.No // does not exist in the enum, so return No
                cont result
            }
            |> ignore




            // let box =
            //     MessageBoxManager.GetMessageBoxStandard(
            //         title,
            //         message,
            //         Enums.ButtonEnum.YesNo)
            //         // .ShowWindowDialogAsync(_host)
            //         // .ShowAsync()
            //         // .GetAwaiter()
            //         // .GetResult()

            // task{
            //     let! res = box.ShowWindowDialogAsync(_host)
            //     let result =
            //         match res with
            //         | Enums.ButtonResult.Ok -> MessageBoxResult.OK //= 0
            //         | Enums.ButtonResult.Yes -> MessageBoxResult.Yes //= 1
            //         | Enums.ButtonResult.No -> MessageBoxResult.No //= 2
            //         | Enums.ButtonResult.Abort -> MessageBoxResult.No //= 3
            //         | Enums.ButtonResult.Cancel -> MessageBoxResult.Cancel //= 4
            //         | Enums.ButtonResult.None -> MessageBoxResult.No //= 5
            //         | _ -> MessageBoxResult.No // does not exist in the enum, so return No
            //     return result
            // }
            //  |> ignore

            // MessageBoxResult.No //= 5


            // async{
            //     do! Async.SwitchToContext Avalonia.Threading.AvaloniaSynchronizationContext.Current
            //     let! res = Async.AwaitTask <| box.ShowAsync()
            //     let result =
            //         match res with
            //         |  Enums.ButtonResult.Ok -> MessageBoxResult.Yes //= 0
            //         |  Enums.ButtonResult.Yes -> MessageBoxResult.Yes //= 1
            //         |  Enums.ButtonResult.No -> MessageBoxResult.No //= 2
            //         |  Enums.ButtonResult.Abort -> MessageBoxResult.No //= 3
            //         |  Enums.ButtonResult.Cancel -> MessageBoxResult.No //= 4
            //         |  Enums.ButtonResult.None -> MessageBoxResult.No //= 5
            //         |  _ -> MessageBoxResult.No // does not exist in the enum, so return No

            //     return result
            // }
            // |> Async.RunSynchronously



