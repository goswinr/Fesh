namespace Seff.Editor

open System
open System.Windows
open System.Windows.Input

open Seff.Model

module Keys =
    
    type ModKey = Ctrl | Alt | Shift
    
    type AltKeyCombo = 
        | AltUp
        | AltRight
        | AltDown
        | AltLeft

    let inline isUp k = 
        match k with 
        | Ctrl  ->  Keyboard.IsKeyUp Key.LeftCtrl  && Keyboard.IsKeyUp Key.RightCtrl
        | Alt   ->  Keyboard.IsKeyUp Key.LeftAlt   && Keyboard.IsKeyUp Key.RightAlt
        | Shift ->  Keyboard.IsKeyUp Key.LeftShift && Keyboard.IsKeyUp Key.RightShift
    
    let inline isDown k = 
        match k with 
        | Ctrl  ->  Keyboard.IsKeyDown Key.LeftCtrl  || Keyboard.IsKeyDown Key.RightCtrl
        | Alt   ->  Keyboard.IsKeyDown Key.LeftAlt   || Keyboard.IsKeyDown Key.RightAlt
        | Shift ->  Keyboard.IsKeyDown Key.LeftShift || Keyboard.IsKeyDown Key.RightShift

    /// because Alt key actually returns Key.System 
    let inline realKey(e:KeyEventArgs) =
        match e.Key with //https://stackoverflow.com/questions/39696219/how-can-i-handle-a-customizable-hotkey-setting
        | Key.System             -> e.SystemKey
        | Key.ImeProcessed       -> e.ImeProcessedKey
        | Key.DeadCharProcessed  -> e.DeadCharProcessedKey
        | k                      -> k

module KeyboardNative =
    // TODO this global key hook might cause th app to be flagged as spyware/ keylogger ??

    // all of this module only exists to be able to use Alt and Up in Rhino too , not just standalone.
    // see bug https://discourse.mcneel.com/t/using-alt-up-key-in-plugin-does-not-work/105740/3

    open System.Runtime.InteropServices    
    open Keys 
    
    let private altKeyComboEv = new Event<AltKeyCombo>()
    
    // adapted from https://www.codeproject.com/Articles/19004/A-Simple-C-Global-Low-Level-Keyboard-Hook
    // and http://www.dylansweb.com/2014/10/low-level-global-keyboard-hook-sink-in-c-net/
    // https://gist.github.com/Ciantic/471698
    
    let private  WH_KEYBOARD_LL = 13
    let private  WM_KEYDOWN     = 0x100
    let private  WM_KEYUP       = 0x101
    let private  WM_SYSKEYDOWN  = 0x104
    let private  WM_SYSKEYUP    = 0x105
    
    [<Struct>]
    [<StructLayout(LayoutKind.Sequential, Pack=0)>]
    type KeyboardHookStruct =
        // or uint ? https://docs.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-kbdllhookstruct
        val vkCode      :int
        val scanCode    :int
        val flags       :int
        val time        :int
        val dwExtraInfo :int     
    
    
    /// <summary>Defines the callback type for the hook
    /// param "code" The hook code, if it isn't >= 0, the function shouldn't do anyting
    /// param "wParam" The event type
    /// param "lParam" The keyhook event information</summary>
    type KeyboardHookProc =  
        delegate of int*int*byref<KeyboardHookStruct> -> int //delegate int keyboardHookProc(int code, int wParam, ref keyboardHookStruct lParam);
    
    // https://christoph.ruegg.name/blog/loading-native-dlls-in-fsharp-interactive.html
    // https://stackoverflow.com/a/63841009/969070    
    
    /// <summary>Sets the windows hook, do the desired event, one of hInstance or threadId must be non-null.</summary>
    /// <param name="idHook">The id of the event you want to hook</param>
    /// <param name="callback">The callback.</param>
    /// <param name="hInstance">The handle you want to attach the event to, can be null</param>
    /// <param name="threadId">The thread you want to attach the event to, can be null</param>
    /// <returns>a handle to the desired hook</returns>
    [<DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)>]
    extern IntPtr SetWindowsHookEx(int idHook, KeyboardHookProc callback, IntPtr hInstance, uint threadId);
    
    /// <summary>Unhooks the windows hook.</summary>
    /// <param name="hInstance">The hook handle that was returned from SetWindowsHookEx</param>
    /// <returns>True if successful, false otherwise</returns>
    [<DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)>]
    extern bool UnhookWindowsHookEx(IntPtr hInstance);
    
    /// <summary>Calls the next hook. Calling the CallNextHookEx function to chain to the next hook procedure is optional, 
    /// but it is highly recommended; otherwise, other applications that have installed hooks will not receive 
    /// hook notifications and may behave incorrectly as a result. You should call CallNextHookEx unless you absolutely 
    /// need to prevent the notification from being seen by other applications.</summary>
    /// <param name="idHook">The hook id</param>
    /// <param name="nCode">The hook code</param>
    /// <param name="wParam">The wparam.</param>
    /// <param name="lParam">The lparam.</param>
    [<DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)>]
    extern int CallNextHookEx(IntPtr idHook, int nCode, int wParam, KeyboardHookStruct& lParam);  //'ref keyboardHookStruct' turns into 'keyboardHookStruct&'
    
    // <summary>Loads the library.</summary>
    // <param name="lpFileName">Name of the library</param>
    // <returns>A handle to the library</returns>
    //[<DllImport("kernel32.dll")>]
    //extern IntPtr LoadLibrary(string lpFileName); 
 
    [<DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)>]
    extern IntPtr GetModuleHandle(string lpModuleName);    
    
    let mutable private hhook = IntPtr.Zero    
    let mutable private window :Window = null    // so we can check if hook event happens while window is active 
  
    
    /// The CallWndProc hook procedure is an application-defined or library-defined callback
    /// function used with the SetWindowsHookEx function. The HOOKPROC type defines a pointer
    /// to this callback function. CallWndProc is a placeholder for the application-defined
    /// or library-defined function name.     
    /// param "nCode"
    ///     [in] Specifies whether the hook procedure must process the message.
    ///     If nCode is HC_ACTION, the hook procedure must process the message.
    ///     If nCode is less than zero, the hook procedure must pass the message to the
    ///     CallNextHookEx function without further processing and must return the
    ///     value returned by CallNextHookEx.
    /// param "wParam"
    ///     [in] Specifies whether the message was sent by the current thread.
    ///     If the message was sent by the current thread, it is nonzero; otherwise, it is zero.
    /// param "lParam"
    ///     [in] Pointer to a CWPSTRUCT structure that contains details about the message.     
    /// Returns:
    ///     If nCode is less than zero, the hook procedure must return the value returned by CallNextHookEx.
    ///     If nCode is greater than or equal to zero, it is highly recommended that you call CallNextHookEx
    ///     and return the value it returns; otherwise, other applications that have installed WH_CALLWNDPROC
    ///     hooks will not receive hook notifications and may behave incorrectly as a result. If the hook
    ///     procedure does not call CallNextHookEx, the return value should be zero.    
    ///     http://msdn.microsoft.com/library/default.asp?url=/library/en-us/winui/winui/windowsuserinterface/windowing/hooks/hookreference/hookfunctions/callwndproc.asp 
    let private callBackForAltKeyCombos =    
        // https://github.com/topstarai/WindowsHook/blob/master/WindowsHook/WinApi/HookProcedure.cs
        KeyboardHookProc( fun (nCode:int) (wParam:int) (lParam: byref<KeyboardHookStruct> ) ->
            //let key = lParam.vkCode // same ints as in System.Windows.Forms.Keys   
            //ISeffLog.log.PrintfnColor 0 99 0  " window.IsFocused %b, window.IsEnabled %b, window.IsActive %b," window.IsFocused window.IsEnabled window.IsActive 
            //if    wParam = WM_KEYDOWN      then ISeffLog.log.PrintfnColor 255 0 0   "down key    : %d , flags %d, dwExtraInfo %d, code %d,"  lParam.vkCode lParam.flags lParam.dwExtraInfo code //lParam.time
            //elif  wParam = WM_SYSKEYDOWN   then ISeffLog.log.PrintfnColor 155 40 40 "down sys key: %d , flags %d, dwExtraInfo %d, code %d,"  lParam.vkCode lParam.flags lParam.dwExtraInfo code //lParam.time
            //elif  wParam = WM_KEYUP        then ISeffLog.log.PrintfnColor 0 0 255   "up key      : %d , flags %d, dwExtraInfo %d, code %d,"  lParam.vkCode lParam.flags lParam.dwExtraInfo code //lParam.time
            //elif  wParam = WM_SYSKEYUP     then ISeffLog.log.PrintfnColor 40 40 150 "up sys key  : %d , flags %d, dwExtraInfo %d, code %d,"  lParam.vkCode lParam.flags lParam.dwExtraInfo code //lParam.time
            //else ISeffLog.log.PrintfnColor 190 40 190 "not up nor down  key  : %d , flags %d, dwExtraInfo %d, code %d,"  lParam.vkCode lParam.flags lParam.dwExtraInfo code //lParam.time            
            if  nCode >= 0
                && wParam = WM_SYSKEYDOWN 
                && lParam.vkCode > 36 
                && lParam.vkCode < 41
                && window.IsActive   // because hook is global in the OS     
                && isUp Ctrl         
                && isUp Shift     // to keep avalon edit built in RectangleSelection.BoxSelectUpByLine working Alt + Shift + Up
                    then         
                        match lParam.vkCode with 
                        | 37  -> altKeyComboEv.Trigger(AltLeft   ) 
                        | 38  -> altKeyComboEv.Trigger(AltUp     )     // same ints as in System.Windows.Forms.Keys  
                        | 39  -> altKeyComboEv.Trigger(AltRight  ) 
                        | 40  -> altKeyComboEv.Trigger(AltDown   ) 
                        | _ -> () // never happens
                        // we are explicityl not calling CallNextHookEx here to not do a nudge in Rhino as well.
                        // see bug https://discourse.mcneel.com/t/using-alt-up-key-in-plugin-does-not-work/105740/3
                        // this will also disable any potential other global shortcut using Alt and arrow keys while Seff window is active. not nice but probaly ok.
                        0 // Since the hook procedure does not call CallNextHookEx, the return value should be zero.    
            else
                CallNextHookEx(hhook, nCode, wParam, &lParam)
            ) 
    
    /// To uninstalls the global hook on shutdown of window
    let unHookForAltKeys() =
        UnhookWindowsHookEx(hhook)

    /// Create a global low level keyboard hook
    let hookUpForAltKeys(win:Window) =
        window <- win // so we can check if hook event happens while window is active 
        use curProcess = Diagnostics.Process.GetCurrentProcess()
        use curModule = curProcess.MainModule
        let moduleHandle = GetModuleHandle(curModule.ModuleName) 
        // https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowshookexa
        //https://stackoverflow.com/questions/1811383/setwindowshookex-in-c-sharp    
        // a hook with WH_KEYBOARD_LL is always global!     
        // a hook with just WH_KEYBOARD does not work from .NET ?!
        hhook <- SetWindowsHookEx(WH_KEYBOARD_LL, callBackForAltKeyCombos, moduleHandle, 0u)  
        //ISeffLog.log.PrintfnDebugMsg  "moduleHandle at :%d" moduleHandle
        //ISeffLog.log.PrintfnDebugMsg  "hhook at:%d" hhook

    
    (*
    /// Create a global low level keyboard hook
    let hookUpUser32() =
        let hInstance = LoadLibrary("User32") // this makes the hook global
        hhook <- SetWindowsHookEx(WH_KEYBOARD_LL, hookProc, hInstance, 0u)
        ISeffLog.log.PrintfnDebugMsg  "User32 at :%d" hInstance 
        ISeffLog.log.PrintfnDebugMsg  "hhook at:%d" hhook

    /// Create a global low level keyboard hook DOESNT WORK
    let hookUpWin(win:Window) =
        //https://stackoverflow.com/questions/1811383/setwindowshookex-in-c-sharp
        let modu  = win.GetType().Module
        let handle = Runtime.InteropServices.Marshal.GetHINSTANCE(modu)
        hhook <- SetWindowsHookEx(WH_KEYBOARD_LL, hookProc, handle, 0u)        
        ISeffLog.log.PrintfnDebugMsg  "GetHINSTANCE at :%d" handle
        ISeffLog.log.PrintfnDebugMsg  "hhook at:%d" hhook
    *)
    
    /// This event occures when the Alt key and any of the four arrows are pressed down
    /// This occures before Preview key down.
    [<CLIEvent>] 
    let OnAltKeyCombo = altKeyComboEv.Publish


module KeyboardShortcuts =
    open Keys    
    open Selection
    open KeyboardNative

    // For alt and arrow keys only since they need a special keyboard hook to not get hijacked in Rhino
    let altKeyCombo(akey:AltKeyCombo) =
        // see bug https://discourse.mcneel.com/t/using-alt-up-key-in-plugin-does-not-work/105740/3
        match IEditor.current with 
        | None -> () //never happens ?
        | Some ed -> 
            match akey with 
            | AltUp    -> SwapLines.swapLinesUp(ed) 
            | AltDown  -> SwapLines.swapLinesDown(ed)
            | AltRight -> SwapWords.right(ed.AvaEdit)  |> ignore 
            | AltLeft  -> SwapWords.left(ed.AvaEdit) |> ignore 


    // gets attached to ech editor instance. via avaEdit.PreviewKeyDown.Add  
    // except for Alt and arrow keys
    let previewKeyDown (ed:IEditor, ke: Input.KeyEventArgs) =  
        //if not ed.IsComplWinOpen then  
         match realKey ke  with  
         
         |Input.Key.Back ->  
             /// TODO check for modifier keys like Alt or Ctrl ?
             match getSelType(ed.AvaEdit.TextArea) with 
             | NoSel ->     CursorBehaviour.backspace4Chars(ed,ke)
             | RectSel ->   RectangleSelection.backspaceKey(ed) ; ke.Handled <- true 
             | RegSel  ->   ()        
    
         |Input.Key.Delete ->                
             /// TODO check for modifier keys like Alt or Ctrl ?
             match getSelType(ed.AvaEdit.TextArea) with 
             | NoSel  ->   CursorBehaviour.deleteTillNonWhite(ed,ke)                
             | RectSel ->  RectangleSelection.deleteKey(ed) ; ke.Handled <- true 
             | RegSel ->   ()

         | Input.Key.Enter | Input.Key.Return ->
             if isUp Ctrl // if alt or ctrl is down this means sending to fsi ...         
             && isUp Alt 
             && isUp Shift then CursorBehaviour.addIndentation(ed,ke)  // add indent after do, for , ->, =  
     
         (*
         | Input.Key.Down -> 
             if isDown Ctrl && isUp Shift then
                 if isDown Alt then
                     RectangleSelection.expandDown(ed) // on Ctrl + Alt + down
                 else  
                     // also use Ctrl key for swaping since Alt key does not work in rhino, 
                     // swaping with alt+up is set up in commands.fs via key gesteures                                        
                     //SwapLines.swapLinesDown(ed) // on Ctrl + Down
                     //ke.Handled <- true
                     ()


         | Input.Key.Up -> 
             if isDown Ctrl && isUp Shift then
                 if isDown Alt then
                     RectangleSelection.expandUp(ed) // on Ctrl + Alt + Up
                 else 
                     //SwapLines.swapLinesUp(ed) // on Ctrl + Up
                     //ke.Handled <- true
                     ()
         
         
         | Input.Key.Left -> 
             if isDown Ctrl && isUp Shift then                
                 match getSelType(ed.AvaEdit.TextArea) with 
                 | RegSel ->   if SwapWords.left(ed.AvaEdit) then  ke.Handled <- true
                 | NoSel  | RectSel ->  ()
         
         | Input.Key.Right -> 
             if isDown Ctrl && isUp Shift then                
                 match getSelType(ed.AvaEdit.TextArea) with 
                 | RegSel ->   if SwapWords.right(ed.AvaEdit) then  ke.Handled <- true
                 | NoSel  | RectSel ->  ()
         *)

         | _ -> ()
 

