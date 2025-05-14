namespace AvaloniaLog

open AvaloniaLog.Util
open AvaloniaLog.ImmBrush
open System
open System.IO
open System.Threading
open AvaloniaEdit
open Avalonia.Media // for color brushes
open Avalonia.Media.Immutable // for IImmutableSolidColorBrush
open System.Text
open System.Diagnostics
open Avalonia.Controls
open AvaloniaEdit.Document
open Fittings
open Avalonia.Controls.Primitives

/// <summary>A ReadOnly text AvalonEdit Editor that provides colored appending via printfn like functions. </summary>
/// <remarks>Use the hidden member AvalonEdit if you need to access the underlying TextEditor class from AvalonEdit for styling.
/// Don't append or change the AvalonEdit.Text property directly. This will mess up the coloring.
/// Only use the printfn and Append functions of this class.</remarks>
type AvaloniaLog () =
    inherit ContentControl()  // the most simple and generic type of UIelement container, like a <div> in html

    /// Stores all the locations where a new color starts.
    /// Will be searched via binary search in colorizing transformers
    let offsetColors = ResizeArray<NewColor> [ {off = -1 ; brush=null} ]    // null is console out // null check done in  this.ColorizeLine(line:AvalonEdit.Document.DocumentLine) ..

    /// Same as default foreground in underlying AvalonEdit.
    /// Will be changed if AvalonEdit foreground brush changes
    let mutable defaultBrush  = ImmutableSolidColorBrush Colors.Black  // should be same as default foreground. Will be set on foreground color changes

    /// Used for printing with custom rgb values
    let mutable customBrush =  ImmutableSolidColorBrush Colors.Black   // will be changed anyway on first call

    let setCustomBrush(red,green,blue) =
        customBrush <- ImmBrush.ofRGB red green blue

    let log =  new TextEditor()

    let color = new ColorizingTransformer(log, offsetColors, defaultBrush ) // to implement the actual colors from colored printing

    let searchPanel = Search.SearchPanel.Install(log) //, enableReplace = false)  // disable replace via search replace dialog

    let mutable isAlive = true

    do
        base.Content <- log  //nest Avalonedit inside a simple ContentControl to hide most of its functionality

        log.FontFamily <- FontFamily "Cascadia Code" // default font
        log.FontSize <- 14.0
        log.IsReadOnly <- true
        log.Encoding <- Encoding.Default // = UTF-16
        log.ShowLineNumbers  <- true
        log.Options.EnableHyperlinks <- true
        log.TextArea.SelectionCornerRadius <- 0.0
        log.TextArea.SelectionBorder <- null
        log.TextArea.TextView.LinkTextForegroundBrush <- Brushes.Blue  //Hyper-links color

        log.TextArea.TextView.LineTransformers.Add color // to actually draw colored text
        log.TextArea.SelectionChanged.Add color.SelectionChangedDelegate // to exclude selected text from being colored

        // match log.TextArea.LeftMargins.[0]  with  // the line number margin
        // | :? Editing.LineNumberMargin as lm -> lm.HighlightCurrentLineNumber <- false // disable highlighting of current line number
        // | _ -> () // TODO reenable in AvaloniaEditB


    let printCallsCounter = ref 0L
    let mutable prevMsgBrush = null //null is no color for console // null check done in  this.ColorizeLine(line:AvalonEdit.Document.DocumentLine) ..
    let stopWatch = Stopwatch.StartNew()
    let buffer =  new StringBuilder()
    let mutable docLength = 0  //to be able to have the doc length async
    let mutable maxCharsInLog = 1024_000 // about 10k lines with 100 chars each
    let mutable stillLessThanMaxChars = true
    let mutable doNotPrintJustBuffer = false // for use in this.Clear() to make sure a print after a clear does not get swallowed

    let mutable printInterval : int64 = 50L //100L

    let mutable lastPrintDelay : int = 30 //70

    //-----------------------------------------------------------------------------------
    // The below functions are trying to work around double UI update in printfn for better UI performance,
    // and the poor performance of log.ScrollToEnd().
    // https://github.com/dotnet/fsharp/issues/3712
    // https://github.com/icsharpcode/AvalonEdit/issues/226
    //-----------------------------------------------------------------------------------

    let getBufferText () =
        let txt = buffer.ToString()
        buffer.Clear()  |> ignore<StringBuilder>
        txt

    /// must be called in sync
    let printToLog() =
        let txt = lock buffer getBufferText //lock for safe access
        if txt.Length > 0 then //might be empty from calls during don't PrintJustBuffer = true
            log.AppendText(txt)     // TODO is it possible that avalonedit skips adding some escape ANSI characters to document?? then docLength could be out of sync !! TODO
            log.ScrollToEnd()
            if log.WordWrap then log.ScrollToEnd() //this is needed a second time. see  https://github.com/dotnet/fsharp/issues/3712
            stopWatch.Restart()

    let newLine = Environment.NewLine

    // let debugFile =
    //     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "AvaloniaLogDebug.txt")
    //     |>  fun p -> IO.File.AppendAllText(p, "AvaloniaLogDebug.txt created" + Environment.NewLine); p



    /// Adds string on UI thread  every 150ms then scrolls to end after 300ms.
    /// Optionally adds new line at end.
    /// Sets line color on LineColors dictionary for DocumentColorizingTransformer.
    /// printOrBuffer (txt:string, addNewLine:bool, typ:ImmutableSolidColorBrush)
    let printOrBuffer (txt:string, addNewLine:bool, brush:ImmutableSolidColorBrush) = // TODO check for escape sequence characters and don't print or count them, how many are skipped by ava-edit during Text.Append??
        // IO.File.AppendAllText(debugFile, txt + (if addNewLine then Environment.NewLine else "") + "£")
        if stillLessThanMaxChars && (txt.Length <> 0 || addNewLine) && isAlive then
            lock buffer (fun () ->  // or rwl.EnterWriteLock() //https://stackoverflow.com/questions/23661863/f-synchronized-access-to-list
                // Change color if needed:
                if prevMsgBrush <> brush then
                    offsetColors.Add { off = docLength; brush = brush } // TODO filter out ANSI escape chars first or just keep them in the doc but not in the visual line ??
                    prevMsgBrush <- brush

                // add to buffer
                if addNewLine then
                    buffer.AppendLine(txt)  |> ignore<StringBuilder>
                    docLength <- docLength + txt.Length + newLine.Length
                else
                    buffer.Append(txt)  |> ignore<StringBuilder>
                    docLength <- docLength + txt.Length
                )

            // check if total text in log  is already to big , print it and then stop printing
            if docLength > maxCharsInLog && isAlive then // needed when log gets piled up with exception messages form Avalonedit rendering pipeline.
                stillLessThanMaxChars <- false
                SyncContext.printAvaloniaLog printToLog
                let itsOverTxt = sprintf "%s%s  **** STOP OF LOGGING **** Log has more than %d characters! Clear Log view first %s%s%s%s " newLine newLine maxCharsInLog  newLine newLine  newLine newLine
                lock buffer (fun () ->
                     offsetColors.Add { off = docLength; brush = Brushes.Red } // TODO filter out ANSI escape chars first or just keep them in the doc but not in the visual line ??
                     buffer.AppendLine itsOverTxt  |> ignore<StringBuilder>
                     docLength <- docLength + itsOverTxt.Length
                    )
                SyncContext.printAvaloniaLog printToLog

                //previous version: (suffers from race condition where Async.SwitchToContext SyncAvaloniaLog.context does not work)
                //async {
                //    do! Async.SwitchToContext SyncAvaloniaLog.context
                //    printToLog()// runs with a lock too
                //    log.AppendText(sprintf "%s%s  *** STOP OF LOGGING *** Log has more than %d characters! clear Log view first" newLine newLine maxCharsInLog)
                //    log.ScrollToEnd()
                //    log.ScrollToEnd() // call twice because of https://github.com/icsharpcode/AvalonEdit/issues/226
                //    } |> Async.StartImmediate

            // check if we are in the process of clearing the view
            elif doNotPrintJustBuffer then // wait really long before printing
                async {
                    let k = Interlocked.Increment printCallsCounter
                    do! Async.Sleep 50
                    while doNotPrintJustBuffer do // wait till don't PrintJustBuffer is set true from end of this.Clear() call
                        do! Async.Sleep 50
                    if printCallsCounter.Value = k && isAlive then //it is the last call for 100 ms
                        // on why using Invoke: https://stackoverflow.com/a/19009579/969070
                        SyncContext.printAvaloniaLog printToLog
                    } |> Async.StartImmediate

            // normal case:
            else
                // check the two criteria for actually printing
                // PRINT CASE 1: since the last printing call more than 100 ms have elapsed. this case is used if a lot of print calls arrive at the log for a more than printInterval (100 ms.)
                // PRINT CASE 2, wait 70 ms and print if nothing else has been added to the buffer during the last lastPrintDelay (70 ms)

                if stopWatch.ElapsedMilliseconds > printInterval && isAlive  then // PRINT CASE 1: only add to document every printInterval (100ms)
                    // printToLog() will also reset stopwatch.
                    // on why using Invoke: https://stackoverflow.com/a/19009579/969070
                    //log.Dispatcher.Invoke( printToLog) // TODO a bit faster probably and less verbose than async here but would this propagate exceptions too ?
                    SyncContext.printAvaloniaLog printToLog

                else
                    /// do timing as low level as possible: see Async.Sleep in  https://github.com/dotnet/fsharp/blob/main/src/fsharp/FSharp.Core/async.fs#L1587
                    let mutable timer :option<Timer> = None
                    let k = Interlocked.Increment printCallsCounter
                    let action =  TimerCallback(fun _ ->
                        if printCallsCounter.Value = k && isAlive then //PRINT CASE 2, it is the last call for 70 ms, there has been no other Increment to printCallsCounter
                            SyncContext.printAvaloniaLog printToLog // without  isAlive  check this can throw a TaskCanceledException while some errors print to stdout during host shutdown (Fesh.Revit 2025)
                        if timer.IsSome then
                            timer.Value.Dispose() // dispose inside callback, like in Async.Sleep in FSharp.Core
                        )
                    timer <- Some (new Threading.Timer(action, null, dueTime = lastPrintDelay , period = -1))

                    // previous version:
                    //async {
                    //    let k = Interlocked.Increment printCallsCounter
                    //    do! Async.Sleep 100
                    //    if !printCallsCounter = k  then //PRINT CASE 2, it is the last call for 100 ms
                    //        SyncContext.printAvaloniaLog printToLog
                    //    } |> Async.StartImmediate

    let print (br:ImmutableSolidColorBrush, s) =
        customBrush <- br
        printOrBuffer (s, true, customBrush)




    //-----------------------------------------------------------
    //----------------------exposed AvalonEdit members:----------
    //-----------------------------------------------------------

    /// if not alive all calls to Dispatcher.Invoke will be cancelled
    /// because they can throw a TaskCanceledException while some errors print to stdout during host shutdown (Fesh.Revit 2025)
    member _.IsAlive
        with get() = isAlive
        and set v  = isAlive <- v

    member  _.VerticalScrollBarVisibility   with get() = log.VerticalScrollBarVisibility     and set v = log.VerticalScrollBarVisibility <- v
    member  _.HorizontalScrollBarVisibility with get() = log.HorizontalScrollBarVisibility   and set v = log.HorizontalScrollBarVisibility <- v
    member  _.FontFamily       with get() = log.FontFamily                  and set v = log.FontFamily <- v
    member  _.FontSize         with get() = log.FontSize                    and set v = log.FontSize  <- v
    //member  _.Encoding         with get() = log.Encoding                    and set v = log.Encoding <- v
    member  _.ShowLineNumbers  with get() = log.ShowLineNumbers             and set v = log.ShowLineNumbers <- v
    member  _.EnableHyperlinks with get() = log.Options.EnableHyperlinks    and set v = log.Options.EnableHyperlinks  <- v

    /// the delay in milliseconds after the last print call before the log is printed to the screen
    /// should be less than the printInterval
    member _.LastPrintDelay
        with get() = lastPrintDelay
        and set v = lastPrintDelay <- v

    /// the time in milliseconds between two print calls to the log
    /// any print calls arriving during this time will be buffered and printed in one go after the printInterval
    member _.PrintInterval
        with get() = printInterval
        and set v = printInterval <- v

    /// Get all text in this AvaloniaLog
    member _.Text() = log.Text

    /// Get all text in Segment AvaloniaLog
    member _.Text(seg:ISegment) = log.Document.GetText(seg)

    /// Get the current Selection
    member _.Selection = log.TextArea.Selection

    /// The SearchPanel from AvaloniaEdit
    member _.SearchPanel = searchPanel

    /// Use true to enable Line Wrap.
    /// setting false will enable Horizontal ScrollBar Visibility
    /// setting true will disable Horizontal ScrollBar Visibility
    member _.WordWrap
        with get() = log.WordWrap
        and set v =
            if v then
                log.WordWrap         <- true
                log.HorizontalScrollBarVisibility <- ScrollBarVisibility.Disabled
            else
                log.WordWrap         <- false
                log.HorizontalScrollBarVisibility <- ScrollBarVisibility.Auto

    //-----------------------------------------------------------
    //----------------------AvaloniaLog specific members:----------
    //------------------------------------------------------------

    /// The maximum amount of characters this AvaloniaLog can display.
    /// By default this about one Million characters
    /// This is to avoid freezing the UI when the AvaloniaLog is flooded with text.
    /// When the maximum is reached a message will be printed at the end, then the printing stops until the content is cleared.
    member _.MaximumCharacterAllowance
        with get () = maxCharsInLog
        and  set v  = maxCharsInLog <- v


    /// To access the underlying  AvalonEdit TextEditor class
    /// Don't append , clear or modify the Text property directly!
    /// This will mess up the coloring.
    /// Only use the printfn family of functions to add text to AvaloniaLog
    /// Use this member only for styling changes
    /// use #nowarn "44" to disable the obsolete warning
    //[<Obsolete("It is not actually obsolete, but normally not used, so hidden from editor tools. In F# use #nowarn \"44\" to disable the obsolete warning")>]
    member _.AvaloniaEdit = log

    /// Clear all Text. (thread-safe)
    /// The Color of the last print will still be remembered
    /// e.g. for log.AppendWithLastColor(..)
    member _.Clear() :unit =
        lock buffer (fun () ->
            doNotPrintJustBuffer <- true
            buffer.Clear() |>  ignore<StringBuilder>
            docLength <- 0
            prevMsgBrush <- null
            stillLessThanMaxChars <- true
            printCallsCounter.Value <- 0L // reset the print calls counter
            )

        // log.Dispatcher.Invoke needed.
        // If this would be done via async{ and do! Async.SwitchToContext a subsequent call via Dispatcher.Invoke ( like print to log) would still come before.
        // It starts faster than async with SwitchToContext
        // log.Dispatcher.Invoke( fun () ->
        SyncContext.send(fun () ->
            log.Clear()
            offsetColors.Clear() // this should be done after log.clear() to avoid race condition where log tries to redraw but offsetColors is already empty
            offsetColors.Add {off = -1 ; brush=null}  // null check done in  this.ColorizeLine(line:AvalonEdit.Document.DocumentLine) ..
            //log.SelectionLength <- 0
            //log.SelectionStart <- 0
            //defaultBrush <- (log.Foreground.Clone() :?> ImmutableSolidColorBrush |> Brush.freeze)   // TODO or remember custom brush ?
            stopWatch.Restart() // works async too
            doNotPrintJustBuffer <- false // this is important to release pending prints stuck in while loop in printOrBuffer()
            )


    /// Returns a thread-safe TextWriter that prints to AvaloniaLog in Color
    /// for use as use System.Console.SetOut(textWriter)
    /// or System.Console.SetError(textWriter)
    member _.GetTextWriter(red, green, blue) =
        let br = ImmBrush.ofRGB red green blue
        new LogTextWriter   (fun s -> printOrBuffer (s, false, br)
                            ,fun s -> printOrBuffer (s, true , br)
                            )

    /// Returns a thread-safe TextWriter that prints to AvaloniaLog in Color
    /// for use as use System.Console.SetOut(textWriter)
    /// or System.Console.SetError(textWriter)
    member _.GetTextWriter(br:ImmutableSolidColorBrush) =
        new LogTextWriter   (fun s -> printOrBuffer (s, false, br)
                            ,fun s -> printOrBuffer (s, true , br)
                            )

    /// Returns a thread-safe TextWriter that only prints to AvaloniaLog
    /// if the predicate returns true for the string sent to the text writer.
    /// The provide Color will be used.
    member _.GetConditionalTextWriter(predicate:string->bool, br:ImmutableSolidColorBrush) =
        new LogTextWriter   (fun s -> if predicate s then printOrBuffer (s, false, br)
                            ,fun s -> if predicate s then printOrBuffer (s, true , br)
                            )


    /// Returns a thread-safe TextWriter that only prints to AvaloniaLog
    /// if the predicate returns true for the string sent to the text writer.
    /// The predicate can also be used for other side effects before printing.
    /// The provided red, green and blue Color values will be used will be used.
    /// Integers will be clamped to be between 0 and 255
    member _.GetConditionalTextWriter(predicate:string->bool, red, green, blue) =
        let br = ImmBrush.ofRGB red green blue
        new LogTextWriter   (fun s -> if predicate s then printOrBuffer (s, false, br)
                            ,fun s -> if predicate s then printOrBuffer (s, true , br)
                            )

    (*
    part of trying to enable ANSI Control sequences for https://github.com/spectreconsole/spectre.console
    https://stackoverflow.com/a/34078058/969070

    member _.GetStreamWriter(br:ImmutableSolidColorBrush) =
        let fbr = br
        new LogStreamWriter (new MemoryStream()
                            ,fun s -> printOrBuffer (s, false, fbr)
                            ,fun s -> printOrBuffer (s, true , fbr)
                            )

    member _.GetStreamWriter(red, green, blue) =
        let br = Brush.ofRGB red green blue
        new LogStreamWriter (new MemoryStream()
                            ,fun s -> printOrBuffer (s, false, br)
                            ,fun s -> printOrBuffer (s, true , br)
                            )
    *)

    //--------------------------------------
    //--------- Append string: -------------
    //--------------------------------------

    /// Print string using default color (Black)
    member _.Append (s) =
        printOrBuffer (s, false, defaultBrush )

    /// Print string using red, green and blue color values (each between 0 and 255).
    /// (without adding a new line at the end).
    member _.AppendWithColor (red, green, blue, s) =
        setCustomBrush (red,green,blue)
        printOrBuffer (s, false, customBrush )

    /// Print string using the Brush provided.
    /// (without adding a new line at the end).
    member _.AppendWithBrush (br:ImmutableSolidColorBrush, s) =
        customBrush <- br
        printOrBuffer (s, false, customBrush )

    /// Print string using the last Brush or color provided.
    /// (without adding a new line at the end
    member _.AppendWithLastColor (s) =
        printOrBuffer (s, false, customBrush)

    //--------------------------------------
    //--------- AppendLine string:----------
    //--------------------------------------

    /// Print string using default color (Black)
    /// Adds a new line at the end
    member _.AppendLine (s) =
        printOrBuffer (s, true, defaultBrush )

    /// Print string using red, green and blue color values (each between 0 and 255).
    /// Adds a new line at the end
    member _.AppendLineWithColor (red, green, blue, s) =
        setCustomBrush (red,green,blue)
        printOrBuffer (s, true, customBrush )

    /// Print string using the Brush provided.
    /// Adds a new line at the end.
    member _.AppendLineWithBrush (br:ImmutableSolidColorBrush, s) =
        customBrush <- br
        printOrBuffer (s, true, customBrush)

    /// Print string using the last Brush or color provided.
    /// Adds a new line at the end
    member _.AppendLineWithLastColor (s) =
        printOrBuffer (s, true, customBrush)

   //--------------------------------------
   //--- with F# string formatting:--------
   //--------------------------------------


    /// F# printf formatting using the Brush provided.
    /// (without adding a new line at the end).
    member _.printfBrush (br:ImmutableSolidColorBrush) s =
        customBrush <- br
        Printf.kprintf (fun s -> printOrBuffer (s, false, customBrush))  s

    /// F# printfn formatting using the Brush provided.
    /// Adds a new line at the end.
    member _.printfnBrush (br:ImmutableSolidColorBrush) s =
        customBrush <- br
        Printf.kprintf (fun s -> printOrBuffer (s, true, customBrush))  s

    /// F# printf formatting using red, green and blue color values (each between 0 and 255).
    /// (without adding a new line at the end)
    member _.printfColor red green blue msg =
        setCustomBrush (red,green,blue)
        Printf.kprintf (fun s -> printOrBuffer (s,false, customBrush ))  msg

    /// F# printfn formatting using red, green and blue color values (each between 0 and 255).
    /// Adds a new line at the end
    member _.printfnColor red green blue msg =
        setCustomBrush (red,green,blue)
        Printf.kprintf (fun s -> printOrBuffer (s,true, customBrush ))  msg

    /// F# printf formatting using the last Brush or color provided.
    /// (without adding a new line at the end
    member _.printfLastColor msg =
        Printf.kprintf (fun s -> printOrBuffer (s, false, customBrush))  msg

    /// F# printfn formatting using the last Brush or color provided.
    /// Adds a new line at the end
    member _.printfnLastColor msg =
        Printf.kprintf (fun s -> printOrBuffer (s, true, customBrush))  msg


    override _.StyleKeyOverride = typeof<ContentControl> // see https://github.com/AvaloniaUI/Avalonia/discussions/18697

// module ILoggingColors =
//     let mutable trace = Brushes.Gray
//     let mutable debug  = Brushes.Teal

//     let mutable information = Brushes.Blue

//     let mutable warning = Brushes.Orange

//     let mutable error = Brushes.Red

//     let mutable critical = Brushes.DarkRed



// let iLogger = { // ILogger interface as F# object expression

//     new  ILogger with

//         member this.Log(logLevel, _eventId, state, except, formatter) =
//             let message = formatter.Invoke(state, except)
//             match logLevel with
//             | LogLevel.None         -> ()
//             | LogLevel.Trace        -> print (ILoggingColors.trace, message)
//             | LogLevel.Debug        -> print (ILoggingColors.debug, message)
//             | LogLevel.Information  -> print (ILoggingColors.information, message)
//             | LogLevel.Warning      -> print (ILoggingColors.warning, message)
//             | LogLevel.Error        -> print (ILoggingColors.error, message)
//             | LogLevel.Critical     -> print (ILoggingColors.critical, message)
//             | _                     -> print (Brushes.Black, message)

//         member _.IsEnabled(_logLevel) =
//             isAlive

//         member _.BeginScope(_state) =
//             { new IDisposable with
//                 member _.Dispose() = () }
//     }

    // The ILogger interface for logging:
    // Trace -> Gray
    // Debug -> Teal
    // Information -> Blue
    // Warning -> Orange
    // Error -> Red
    // Critical -> DarkRed
    // None -> no logging
    // member _.ILogger = iLogger
