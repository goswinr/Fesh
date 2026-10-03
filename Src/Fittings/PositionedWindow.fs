namespace Fittings

// open System
open Avalonia
open Avalonia.Controls
open Avalonia.Platform


/// A class holding a re-sizable Window that remembers its position even after restarting.
/// The path in settingsFile will be used to persist the position of this window in a txt file.
/// The errorLogger function will be called if persisting the window size does not work.
type PositionedWindow (settingsFile:System.IO.FileInfo, errorLogger:string->unit) as this =
    inherit Window()

    // The ErrorLogger function will be called if the previous Window position could not restore.
    // The window be positioned in the screen center with a size of 600 x 600.

    let settings = new PersistentSettings(settingsFile, errorLogger)

    // the owning window
    // let mutable owner = IntPtr.Zero //https://github.com/AvaloniaUI/Avalonia/discussions/12845#discussioncomment-6961600

    let mutable setMaxAfterLoading = false

    let mutable isMinOrMax = false

    do
        //this.ResizeMode  <- ResizeMode.CanResize // only WPF

        //-------------------------------------------------------------------------
        //----  all below code is for load and safe window location and size ------
        //-------------------------------------------------------------------------

        // (1) first restore normal size
        let winTop    = settings.GetInt ("WindowTop"    , 100  )
        let winLeft   = settings.GetInt ("WindowLeft"   , 100  )
        let winHeight = settings.GetInt ("WindowHeight" , 800  )
        let winWidth  = settings.GetInt ("WindowWidth"  , 1000  )
        let desiredPosition = PixelRect(winLeft, winTop, winWidth, winHeight)

        let minimumVisiblePixelsOfWindowTitleBar  = 50

        let contains (outer: PixelRect, inner: PixelRect, tol) =
            let xIsOK =
                outer.X + tol               < inner.X + inner.Width ||
                outer.X + outer.Width - tol > inner.X
            let yIsOK =
                outer.Y - 2 < inner.Y  || // title bar attached to top of screen
                outer.Y + outer.Height - tol > inner.Y // title bar just on to of bottom of screen
            xIsOK && yIsOK

        // only now set the maximize flag or correct position if off the screen
        if settings.GetBool ("WindowIsMax", false) then
            //this.WindowState <- WindowState.Maximized // always puts it on first screen, do in loaded event instead
            setMaxAfterLoading <- true
            isMinOrMax  <- true

        elif base.Screens.All |> Seq.exists ( fun (s:Screen) -> contains(s.WorkingArea, desiredPosition, minimumVisiblePixelsOfWindowTitleBar)) then
            // the window is on the screen, so set it to the desired position
            base.WindowStartupLocation <- WindowStartupLocation.Manual
            base.Position <- PixelPoint(desiredPosition.X, desiredPosition.Y)
            base.Height   <- int desiredPosition.Height
            base.Width    <- int desiredPosition.Width

        else
            // the window is off the screen, so set it to the center of the screen
            base.WindowStartupLocation <- WindowStartupLocation.CenterScreen
            base.Height <- 600.0
            base.Width  <- 600.0


        //Turns out that we cannot maximize the window until it's loaded.
        //http://mostlytech.blogspot.com/2008/01/maximizing-wpf-window-to-second-monitor.html
        this.Loaded.Add (fun _ -> if setMaxAfterLoading then this.WindowState <- WindowState.Maximized)

        this.PositionChanged.Add (fun _ -> // occurs for every pixel moved
            async{
                // normally the state change event comes after the location change event but before size changed. async sleep in LocationChanged prevents this
                do! Async.Sleep 200 // so that StateChanged event comes first
                if this.WindowState = WindowState.Normal &&  not isMinOrMax then
                    //if base.Top > -500. && base.Left > -500. then // to not save on minimizing on minimized: Top=-32000 Left=-32000
                        settings.SetIntDelayed ("WindowTop"  ,this.Position.Y , 100) // get float in state change Maximized needs to access this before 350 ms pass
                        settings.SetIntDelayed ("WindowLeft" ,this.Position.X , 100)
                        settings.SaveWithDelay ()
                }
                |> Async.StartImmediate
            )



        //base.StateChanged.Add (fun _ -> WPF only
        // this.SizeChanged.Add(fun _ ->
        //     match this.WindowState with
        //     | WindowState.Normal ->
        //         // because when Window is hosted in other App the restore from Maximized does not remember the previous position automatically
        //         this.Position <- PixelPoint(
        //                              settings.GetInt ("WindowLeft"   , 100 ),
        //                              settings.GetInt ("WindowTop"    , 100 ))
        //         this.Height <-  int (settings.GetInt ("WindowHeight" , 800 ))
        //         this.Width  <-  int (settings.GetInt ("WindowWidth"  , 800 ))
        //         settings.SetBool  ("WindowIsMax", false)  |> ignore
        //         isMinOrMax <- false
        //         settings.SaveWithDelay ()

        //     | WindowState.Maximized ->
        //         // normally the state change event comes after the location change event but before size changed. async sleep in LocationChanged prevents this
        //         isMinOrMax  <- true
        //         settings.SetBool ("WindowIsMax", true) |> ignore
        //         settings.SaveWithDelay  ()

        //     |WindowState.Minimized ->
        //         isMinOrMax  <- true

        //     | WindowState.FullScreen -> () // TODO don't save ?
        //     | _ -> () // never happens
        //     )

        this.SizeChanged.Add (fun _ -> // does no get trigger on maximizing
            if this.WindowState = WindowState.Normal &&  not isMinOrMax  then
                settings.SetIntDelayed ("WindowHeight", int this.Height, 100 )
                settings.SetIntDelayed ("WindowWidth" , int this.Width , 100 )
                settings.SaveWithDelay ()
            )

    /// Create from application name only
    /// Settings will be saved in LocalApplicationData folder
    /// In a subfolder called 'applicationName'.
    /// The file itself will be called 'Fittings.PositionedWindow.Settings.txt'.
    /// The ErrorLogger function will be called if the previous Window position could not restore.
    /// The window be positioned in the screen center with a size of 600 x 600.
    new (applicationName:string, errorLogger:string->unit) =
        let appName =
           let mutable n = applicationName
           for c in System.IO.Path.GetInvalidFileNameChars() do  n <- n.Replace(c, '_')
           n
        let appData = System.Environment.GetFolderPath System.Environment.SpecialFolder.LocalApplicationData
        let p = System.IO.Path.Combine(appData, appName)
        System.IO.Directory.CreateDirectory(p) |> ignore
        let f = System.IO.Path.Combine(p, "Fittings.PositionedWindow.Settings.txt")
        PositionedWindow(System.IO.FileInfo(f), errorLogger)


    /// Indicating if the Window is in Full-screen mode or minimized mode (not normal mode)
    member this.IsMinOrMax = isMinOrMax


    (*
    // https://github.com/AvaloniaUI/Avalonia/discussions/12845#discussioncomment-6961600

    /// Get or Set the native Window Handle that owns this window.
    /// Use if this Window is hosted in another native app  (via IntPtr).
    /// So that this window opens and closes at the same time as the main host window.
    member this.OwnerHandle
        with get () = owner
        and set ptr =
            if ptr <> IntPtr.Zero then
                owner <- ptr
                Interop.WindowInteropHelper(this).Owner <- ptr
    *)

    member this.Settings = settings

    override _.StyleKeyOverride = typeof<Window> // see https://github.com/AvaloniaUI/Avalonia/discussions/18697