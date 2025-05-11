namespace AvaloniaLog

/// Shadows the ignore function to only accept structs
/// This is to prevent accidentally ignoring partially applied functions that would return struct
module internal Util =

    /// Shadows the original 'ignore' function
    /// This is to prevent accidentally ignoring partially applied functions
    [<RequiresExplicitTypeArguments>]
    let inline ignore<'T> (x:'T) = ignore x

    /// The same as 'not isNull'
    let inline notNull x = match x with null -> false | _ -> true
