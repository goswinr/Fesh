namespace Fittings

[<RequireQualifiedAccess>]
type MessageBoxButton =
    | OK
    | YesNo
    | YesAlwaysNo
    | YesNoCancel

[<RequireQualifiedAccess>]
type MessageBoxImage =
    | Exclamation
    | Question
    | Error

[<RequireQualifiedAccess>]
type MessageBoxResult =
    | OK
    | Yes
    | Always
    | No
    | Cancel

module MsgBxLiterals =
    let [<Literal>] ok = "OK"
    let [<Literal>] yes = "Yes"
    let [<Literal>] yesAlways = "Yes, always"
    let [<Literal>] no = "No"
    let [<Literal>] cancel = "Cancel"



open MsBox.Avalonia // <PackageReference Include="MessageBox.Avalonia" Version="3.2.0" />
open Avalonia.Controls
open MsBox.Avalonia.Dto
open MsBox.Avalonia.Models
open System.Threading.Tasks


[<RequireQualifiedAccess>]
type MessageBox =

    // https://github.com/AvaloniaCommunity/MessageBox.Avalonia

    static member Show(
        host: Avalonia.Controls.Window,
        message: string,
        title: string,
        button: MessageBoxButton,
        image: MessageBoxImage,
        defaultResult: MessageBoxResult) : Task<MessageBoxResult> =
            let buttons =
                match button with
                | MessageBoxButton.OK -> [
                    new ButtonDefinition(Name = MsgBxLiterals.ok, IsDefault = true, IsCancel = true)
                    ]
                | MessageBoxButton.YesNo -> [
                    new ButtonDefinition(Name = MsgBxLiterals.yes, IsDefault = defaultResult.IsYes)
                    new ButtonDefinition(Name = MsgBxLiterals.no , IsDefault = defaultResult.IsNo)
                    ]
                | MessageBoxButton.YesAlwaysNo -> [
                    new ButtonDefinition(Name = MsgBxLiterals.yes, IsDefault = defaultResult.IsYes)
                    new ButtonDefinition(Name = MsgBxLiterals.yesAlways, IsDefault = defaultResult.IsAlways)
                    new ButtonDefinition(Name = MsgBxLiterals.no, IsDefault = defaultResult.IsNo) //A user can activate the Cancel button by pressing the ESC key.
                    ]
                | MessageBoxButton.YesNoCancel -> [
                    new ButtonDefinition(Name = MsgBxLiterals.yes, IsDefault = defaultResult.IsYes)
                    new ButtonDefinition(Name = MsgBxLiterals.no , IsDefault = defaultResult.IsNo)
                    new ButtonDefinition(Name = MsgBxLiterals.cancel, IsDefault = defaultResult.IsCancel, IsCancel = true) //A user can activate the Cancel button by pressing the ESC key.
                    ]

            let icon =
                match image with
                | MessageBoxImage.Exclamation -> MsBox.Avalonia.Enums.Icon.Warning
                | MessageBoxImage.Question -> MsBox.Avalonia.Enums.Icon.Question
                | MessageBoxImage.Error -> MsBox.Avalonia.Enums.Icon.Error
            let opt =
                new MessageBoxCustomParams(
                    ButtonDefinitions = buttons,
                    ContentTitle = title,
                    ContentMessage = message,
                    Icon = icon,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    MaxWidth = 800,
                    MaxHeight = 700,
                    SizeToContent = SizeToContent.WidthAndHeight,
                    ShowInCenter = true,
                    Topmost = false
                    // HyperLinkParams = new HyperLinkParams( Text = "https://docs.avaloniaui.net/", Action = Browse.hyperLinkAction
                    )
            // opt.SystemDecorations <- SystemDecorations.BorderOnly


            let box = MessageBoxManager.GetMessageBoxCustom(opt)
            task {
                let! res = box.ShowWindowDialogAsync host
                return
                    match res with
                    | MsgBxLiterals.ok      -> MessageBoxResult.OK
                    | MsgBxLiterals.yes     -> MessageBoxResult.Yes
                    | MsgBxLiterals.yesAlways -> MessageBoxResult.Always
                    | MsgBxLiterals.no      -> MessageBoxResult.No
                    | MsgBxLiterals.cancel  -> MessageBoxResult.Cancel
                    | x         ->
                        eprintfn $"Result from MessageBox '{x}' is not recognized, returning 'Cancel'."
                        MessageBoxResult.Cancel
                }


    static member ShowOK (
        host: Avalonia.Controls.Window,
        message: string,
        title: string,
        image: MessageBoxImage) : unit  =
            task{
                let! _ = MessageBox.Show(host, message, title, MessageBoxButton.OK, image, MessageBoxResult.OK)
                ()
            }
            |> ignore<Task<unit>>




