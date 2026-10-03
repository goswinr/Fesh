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


[<RequireQualifiedAccess>]
type MessageBox =

    static member Show(
        _host: Avalonia.Controls.Window,
        message: string,
        title: string,
        _button: MessageBoxButton,
        _image: MessageBoxImage,
        _defaultResult: MessageBoxResult,
        _options: MessageBoxOptions) : MessageBoxResult =

            let result =
                MessageBoxManager.GetMessageBoxStandard(
                    title,
                    message,
                    Enums.ButtonEnum.YesNo)
                    .ShowAsync()
                    .GetAwaiter()
                    .GetResult()

            match result with
            |  Enums.ButtonResult.Ok -> MessageBoxResult.Yes //= 0
            |  Enums.ButtonResult.Yes -> MessageBoxResult.Yes //= 1
            |  Enums.ButtonResult.No -> MessageBoxResult.No //= 2
            |  Enums.ButtonResult.Abort -> MessageBoxResult.No //= 3
            |  Enums.ButtonResult.Cancel -> MessageBoxResult.No //= 4
            |  Enums.ButtonResult.None -> MessageBoxResult.No //= 5
            |  _ -> MessageBoxResult.No // does not exist in the enum, so return No

