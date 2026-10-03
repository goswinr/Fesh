namespace Fittings


open System
open Avalonia
open Avalonia.Data
open System.Globalization
open System.ComponentModel


/// A module to provide a ViewModelBase class
module ViewModel =

    /// A base class for a ViewModel implementing INotifyPropertyChanged
    type ViewModelBase() =
        // alternative: http://www.fssnip.net/4Q/title/F-Quotations-with-INotifyPropertyChanged
        let ev = new Event<_, _>()

        interface INotifyPropertyChanged with
            [<CLIEvent>]
            member x.PropertyChanged = ev.Publish

        /// Use nameof operator on members to provide the string required
        /// member x.Val
        ///    with get()  = val
        ///    and set(v)  = val <- v; x.OnPropertyChanged(nameof x.Val)
        member x.OnPropertyChanged(propertyName : string) =
            ev.Trigger(x, new PropertyChangedEventArgs(propertyName))

