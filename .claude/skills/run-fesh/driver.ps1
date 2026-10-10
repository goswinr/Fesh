<#
Drives the Fesh standalone app (WPF) via Windows UI Automation and SendKeys.
Each call is one command, the state between calls (process id, settings backup) is kept in $env:TEMP\fesh-run\state.json.

  pwsh -File .claude/skills/run-fesh/driver.ps1 <command> [arg] [-Framework net10.0-windows|net48] [-WaitMs 4000] [-DelayMs 40]

  launch [code]   back up the Settings folder, open a scratch .fsx with [code] in a new Fesh process, wait for the first type check
  settext <code>  replace the whole editor text (one change, does not trigger completion), caret goes to the end, waits for the type check
  gettext         print the editor text
  keys <keys>     send keys in SendKeys syntax to the editor, e.g. "^{END}" or "." ; escape + ^ % ~ ( ) { } [ ] as {(}
  type <text>     type literal text one key at a time, -DelayMs between keys (Fesh auto-closes brackets and quotes!)
  popup           wait up to -WaitMs for the completion window, print its visible items or "no completion window"
  esc             close the completion window if open
  status          print the status bar texts (type check result, FSI state)
  log             print the text of the log panel
  shot [name]     screenshot of the Fesh window (and completion window), prints the png path
  stop            kill Fesh, restore the Settings folder from the backup taken by 'launch'
#>
param(
    [Parameter(Position=0)][string]$Command = "help",
    [Parameter(Position=1)][string]$Arg = "",
    [ValidateSet("net10.0-windows", "net48")][string]$Framework = "net10.0-windows",
    [int]$WaitMs = 4000,
    [int]$DelayMs = 40
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
if (-not ("FeshW32" -as [type])) {
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class FeshW32 {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
}
"@
}
$A  = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]
function Cond($prop, $val) { New-Object System.Windows.Automation.PropertyCondition($prop, $val) }

$repo      = (Resolve-Path (Join-Path $PSScriptRoot "../../..")).Path
$work      = Join-Path $env:TEMP "fesh-run"
$stateFile = Join-Path $work "state.json"
$checkingTxt = "Checking for Errors ..." # from Src/Views/StatusBar.fs

function Get-State {
    if (-not (Test-Path $stateFile)) { throw "Fesh is not launched by this driver, run 'launch' first" }
    $s = Get-Content $stateFile -Raw | ConvertFrom-Json
    if (-not (Get-Process -Id $s.pid -ErrorAction SilentlyContinue)) { throw "Fesh process $($s.pid) is gone, run 'stop' to restore the settings" }
    $s
}

function Get-Main([int]$procId) { # the pid from the json state is an Int64, UI Automation needs an Int32
    $wins = $A::RootElement.FindAll($TS::Children, (Cond $A::ProcessIdProperty $procId))
    foreach ($w in $wins) { if ($w.Current.Name -like "*  -  Fesh  -  *") { return $w } }
    $null
}

# the editor of the current tab; the log panel is also a Document, but not inside the TabControl
function Get-Editor($main) {
    $tabs = $main.FindFirst($TS::Descendants, (Cond $A::ControlTypeProperty $CT::Tab))
    if (-not $tabs) { return $null }
    $tabs.FindFirst($TS::Descendants, (Cond $A::ControlTypeProperty $CT::Document))
}

function Get-Value($el) { $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }

# owned windows like the completion window are children of the main window in the UI Automation tree, not of the desktop
function Get-ComplWin($main) {
    foreach ($w in $main.FindAll($TS::Children, (Cond $A::ControlTypeProperty $CT::Window))) {
        if ($w.FindFirst($TS::Descendants, (Cond $A::ControlTypeProperty $CT::List))) { return $w }
    }
    $null
}

function Get-StatusTexts($main) {
    $bar = $main.FindFirst($TS::Descendants, (Cond $A::ControlTypeProperty $CT::StatusBar))
    @($bar.FindAll($TS::Descendants, (Cond $A::ControlTypeProperty $CT::Text)) | ForEach-Object { $_.Current.Name.Trim() } | Where-Object { $_ })
}

# the status bar shows the checking text only if the check takes longer than 200 ms
function Wait-Check($main, [int]$timeoutMs) {
    Start-Sleep -Milliseconds 400
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ((Get-StatusTexts $main) -contains $checkingTxt) {
        if ($sw.ElapsedMilliseconds -gt $timeoutMs) { Write-Warning "type check still running after $timeoutMs ms"; return }
        Start-Sleep -Milliseconds 200
    }
}

function Set-FeshForeground($s, $main) {
    $h = [IntPtr]$main.Current.NativeWindowHandle
    [uint32]$fpid = 0; [FeshW32]::GetWindowThreadProcessId([FeshW32]::GetForegroundWindow(), [ref]$fpid) | Out-Null
    if ($fpid -ne $s.pid) {
        [FeshW32]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero); [FeshW32]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero) # Alt, so that SetForegroundWindow is allowed
        [FeshW32]::SetForegroundWindow($h) | Out-Null
        Start-Sleep -Milliseconds 200
    }
    $ed = Get-Editor $main
    if (-not (Get-ComplWin $main)) { try { $ed.SetFocus() } catch {} } # SetFocus would close an open completion window
}

# SendKeys goes to whatever window is in the foreground, so never send unless it is Fesh
function Send-Guarded($s, [string]$keys) {
    [uint32]$fpid = 0; [FeshW32]::GetWindowThreadProcessId([FeshW32]::GetForegroundWindow(), [ref]$fpid) | Out-Null
    if ($fpid -ne $s.pid) { throw "Fesh is not in the foreground (process $fpid is), not sending keys" }
    [System.Windows.Forms.SendKeys]::SendWait($keys)
}

function ConvertTo-SendKeys([char]$c) {
    switch ($c) {
        { "+^%~(){}[]".Contains($c) } { return "{$c}" }
        "`n" { return "{ENTER}" }
        "`r" { return "" }
        default { return [string]$c }
    }
}

function ConvertTo-CrLf([string]$code) { $code.Replace("`r`n", "`n").Replace("`n", "`r`n") }

switch ($Command) {
"launch" {
    if (Test-Path $stateFile) { throw "a previous run was not stopped, run 'stop' first (it restores the settings from that run's backup)" }
    if (Get-Process Fesh -ErrorAction SilentlyContinue) { throw "a Fesh process is already running, close it first: it shares the Settings folder" }
    $exe = if ($Framework -eq "net48") { "bin\Debug\net48\win-x64\Fesh.exe" } else { "bin\Debug\net10.0-windows\win-x64\Fesh.exe" }
    $exe = Join-Path $repo $exe
    if (-not (Test-Path $exe)) { throw "$exe not found, build first: dotnet build FeshStandalone.fsproj -f $Framework" }
    $settingsDir = Join-Path $env:APPDATA $(if ($Framework -eq "net48") { "Fesh.net48\Settings" } else { "Fesh\Settings" })
    $backupDir = Join-Path $work "SettingsBackup"
    New-Item -ItemType Directory -Force $work | Out-Null
    if (Test-Path $backupDir) { Remove-Item -Recurse -Force $backupDir }
    New-Item -ItemType Directory -Force $backupDir | Out-Null
    if (Test-Path $settingsDir) { Copy-Item (Join-Path $settingsDir "*") $backupDir -Recurse -Force }
    $scratch = Join-Path $work "Scratch.fsx"
    Set-Content -Path $scratch -Value (ConvertTo-CrLf $Arg) -NoNewline -Encoding utf8
    # with a file as argument Fesh opens only that file, not the previously open tabs
    $proc = Start-Process $exe -ArgumentList "`"$scratch`"" -PassThru
    @{ pid = $proc.Id; framework = $Framework; settingsDir = $settingsDir; backupDir = $backupDir; scratch = $scratch } | ConvertTo-Json | Set-Content $stateFile
    $main = $null; $ed = $null
    for ($i = 0; $i -lt 120 -and -not $ed; $i++) {
        Start-Sleep -Milliseconds 500
        if (-not $main) { $main = Get-Main $proc.Id }
        if ($main) { $ed = Get-Editor $main }
    }
    if (-not $ed) { throw "no editor found after 60 s, process $($proc.Id) still running, run 'stop'" }
    Wait-Check $main 60000 # the first check after startup takes a few seconds
    $s = Get-State
    Set-FeshForeground $s $main
    Send-Guarded $s "^{END}" # the caret starts at the beginning of the file
    "launched Fesh $Framework, pid $($proc.Id), scratch file $scratch"
    (Get-StatusTexts $main) -join " | "
}
"settext" {
    $s = Get-State; $main = Get-Main $s.pid
    $ed = Get-Editor $main
    $ed.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue((ConvertTo-CrLf $Arg))
    Set-FeshForeground $s $main
    Send-Guarded $s "^{END}"
    Wait-Check $main $WaitMs
    (Get-StatusTexts $main) -join " | "
}
"gettext" {
    $s = Get-State; Get-Value (Get-Editor (Get-Main $s.pid))
}
"keys" {
    $s = Get-State; $main = Get-Main $s.pid
    Set-FeshForeground $s $main
    Send-Guarded $s $Arg
}
"type" {
    $s = Get-State; $main = Get-Main $s.pid
    Set-FeshForeground $s $main
    foreach ($c in $Arg.ToCharArray()) {
        $k = ConvertTo-SendKeys $c
        if ($k) { Send-Guarded $s $k; Start-Sleep -Milliseconds $DelayMs }
    }
}
"popup" {
    $s = Get-State; $main = Get-Main $s.pid
    $w = $null; $sw = [Diagnostics.Stopwatch]::StartNew()
    while (-not $w -and $sw.ElapsedMilliseconds -lt $WaitMs) { Start-Sleep -Milliseconds 200; $w = Get-ComplWin $main }
    if (-not $w) { "no completion window" }
    else {
        # the list is virtualized: only the visible items (about 25) exist in the UI Automation tree
        $w.FindAll($TS::Descendants, (Cond $A::ControlTypeProperty $CT::Text)) | ForEach-Object { $_.Current.Name } | Where-Object { $_ }
    }
}
"esc" {
    $s = Get-State; $main = Get-Main $s.pid
    for ($i = 0; $i -lt 3 -and (Get-ComplWin $main); $i++) { Set-FeshForeground $s $main; Send-Guarded $s "{ESC}"; Start-Sleep -Milliseconds 300 }
}
"status" {
    $s = Get-State; Get-StatusTexts (Get-Main $s.pid)
}
"log" {
    $s = Get-State; $main = Get-Main $s.pid
    $docs = $main.FindAll($TS::Descendants, (Cond $A::ControlTypeProperty $CT::Document))
    $edText = Get-Value (Get-Editor $main)
    foreach ($d in $docs) { $v = Get-Value $d; if ($v -ne $edText) { $v; break } }
}
"shot" {
    $s = Get-State; $main = Get-Main $s.pid
    $name = if ($Arg) { $Arg } else { Get-Date -Format "HHmmss" }
    $r = $main.Current.BoundingRectangle
    $x1 = $r.Left; $y1 = $r.Top; $x2 = $r.Right; $y2 = $r.Bottom
    $w = Get-ComplWin $main
    if ($w) { $q = $w.Current.BoundingRectangle; $x1 = [Math]::Min($x1,$q.Left); $y1 = [Math]::Min($y1,$q.Top); $x2 = [Math]::Max($x2,$q.Right); $y2 = [Math]::Max($y2,$q.Bottom) }
    $dir = Join-Path $work "shots"; New-Item -ItemType Directory -Force $dir | Out-Null
    $path = Join-Path $dir "$name.png"
    $bmp = New-Object System.Drawing.Bitmap ([int]($x2-$x1)), ([int]($y2-$y1))
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([int]$x1, [int]$y1, 0, 0, $bmp.Size) # from the screen: Fesh must not be covered by other windows
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    $path
}
"stop" {
    if (-not (Test-Path $stateFile)) { "nothing to stop"; return }
    $s = Get-Content $stateFile -Raw | ConvertFrom-Json
    # kill instead of closing the window: closing asks to save the changed scratch file
    if (Get-Process -Id $s.pid -ErrorAction SilentlyContinue) { Stop-Process -Id $s.pid -Force; Start-Sleep -Milliseconds 500 }
    foreach ($f in Get-ChildItem $s.backupDir) {
        $t = Join-Path $s.settingsDir $f.Name
        if (-not (Test-Path $t) -or (Get-FileHash $t).Hash -ne (Get-FileHash $f.FullName).Hash) { Copy-Item $f.FullName $t -Force; "restored $($f.Name)" }
    }
    # 'launch' refuses to start next to another Fesh, so new files are from this run, e.g. an empty NuGet-Statistic.txt
    foreach ($f in Get-ChildItem $s.settingsDir -File) {
        if (-not (Test-Path (Join-Path $s.backupDir $f.Name))) { Remove-Item $f.FullName; "removed $($f.Name), it was created during the run" }
    }
    Remove-Item $stateFile
    "stopped"
}
default { Get-Content $PSCommandPath -TotalCount 19 | Select-Object -Skip 1 }
}
