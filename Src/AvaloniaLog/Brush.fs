namespace AvaloniaLog

open System
open Avalonia.Media // for ImmutableSolidColorBrush
open Avalonia.Media.Immutable // for IImmutableSolidColorBrush


/// Utility functions for Avalonia.Media.Immutable.ImmutableSolidColorBrush
module ImmBrush =

    let inline clampToByte (i:int) =
        if   i <=   0 then 0uy
        elif i >= 255 then 255uy
        else byte i

    /// Get a frozen brush of red, green and blue values.
    /// int gets clamped to 0-255
    let inline ofRGB r g b =
        ImmutableSolidColorBrush(Color.FromArgb(255uy, clampToByte r, clampToByte g, clampToByte b))

    /// Get a transparent frozen brush of alpha, red, green and blue values.
    /// int gets clamped to 0-255
    let inline ofARGB a r g b =
        ImmutableSolidColorBrush(Color.FromArgb(clampToByte a, clampToByte r, clampToByte g, clampToByte b))

    /// Adds bytes to each color channel to increase brightness, negative values to make darker.
    /// Result will be clamped between 0 and 255
    let inline changeLuminance (amount:int) (col:Color)=
        let r = int col.R + amount |> clampToByte
        let g = int col.G + amount |> clampToByte
        let b = int col.B + amount |> clampToByte
        Color.FromArgb(col.A, r,g,b)

    /// Adds bytes to each color channel to increase brightness
    /// result will be clamped between 0 and 255
    let brighter (amount:int) (br:IImmutableSolidColorBrush) =
        ImmutableSolidColorBrush(changeLuminance amount br.Color)

    /// Removes bytes from each color channel to increase darkness,
    /// result will be clamped between 0 and 255
    let darker (amount:int) (br:IImmutableSolidColorBrush) =
        ImmutableSolidColorBrush(changeLuminance -amount br.Color)


/// Utility functions for Avalonia.Media.Pen
module Pen =
    open System
    open Avalonia.Media // for Pen

    /// Make it thread-safe and faster
    let inline freeze(pen:Pen) =
        pen.ToImmutable()
