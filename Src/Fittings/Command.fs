namespace Fittings

open System
open System.Windows.Input

/// A module to Create Avalonia.Input.ICommand instances
module Command =

    // WPF:
    // A command that optionally hooks into CommandManager.RequerySuggested to
    // automatically trigger CanExecuteChanged whenever the CommandManager detects
    // conditions that might change the output of canExecute. It's necessary to use
    // this feature for command bindings where the CommandParameter is bound to
    // another UI control (e.g. a ListView.SelectedItem).

    // type Command(actionToExecute, canExecute, autoRequery:bool ) as this =
    type Command(actionToExecute, canExecute) as this =
        let canExecuteChanged = Event<EventHandler,EventArgs>()
        let handler = EventHandler(fun _ _ -> this.RaiseCanExecuteChanged())

        // do // TODO: was in WPF, check if this is needed in Avalonia,
        //     if autoRequery then
        //         CommandManager.RequerySuggested.AddHandler(handler) // doe not exist in Avalonia, maybe https://www.nuget.org/packages/Avalonia.Labs.CommandManager/ ?

        member private this._Handler = handler // CommandManager only keeps a weak reference to the event handler, so a strong handler must be maintained

        member this.RaiseCanExecuteChanged () = canExecuteChanged.Trigger(this , EventArgs.Empty)

        //needed ? interface is implemented as members and as interface members( since in F# interfaces are explicit, not implicit like in C#):
        [<CLIEvent>]
        member this.CanExecuteChanged = canExecuteChanged.Publish
        member this.CanExecute p = canExecute p
        member this.Execute p = actionToExecute p

        interface ICommand with
            [<CLIEvent>]
            member this.CanExecuteChanged = this.CanExecuteChanged
            member this.CanExecute p =      this.CanExecute p
            member this.Execute p =         this.Execute p

    /// creates a ICommand.
    /// provide function for canExecute and actionToExecute
    /// hooks into CommandManager.RequerySuggested to
    /// automatically trigger CanExecuteChanged whenever the CommandManager detects
    /// conditions that might change the output of canExecute.
    let mkCmd canExecute actionToExecute =
        // new Command(actionToExecute, canExecute, true) //:> ICommand
        new Command(actionToExecute, canExecute) :> ICommand

    /// creates a ICommand, CanExecute is always true
    let mkCmdSimple action =
        let ev = Event<_ , _>()
        { new ICommand with
                [<CLIEvent>]
                member this.CanExecuteChanged = ev.Publish
                member this.CanExecute(obj) = true
                member this.Execute(obj) = action(obj)
                }

