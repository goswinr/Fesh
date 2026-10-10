<#
Checks the completion window after typing a dot, using driver.ps1 against a running Fesh (run 'driver.ps1 launch' first).
For each case the code is set without the dot, then the dot is typed as a real key press.
Expected: an item that must be among the visible items of the completion window, or "none" for no window.
Exit code 1 if a case fails.
#>
$driver = Join-Path $PSScriptRoot "driver.ps1"
$pre = "let s = `"abc`"`nlet xs = [| `"a`" |]`n"
$cases = @( # the leading comma keeps each case an array, without it PowerShell flattens them all into one list
    ,@("after )",                    "let t = System.Guid.NewGuid()",                         "ToByteArray")
    ,@("after ) and space",          "let t = System.Guid.NewGuid() ",                        "ToByteArray")
    ,@("after ]",                    "let t = xs[0]",                                         "Length")
    ,@("after method call",          "let t = s.Substring(1)",                                "Length")
    ,@("new line, deeper indent",    "let t =`n    System.Guid.NewGuid()`n        ",          "ToByteArray")
    ,@("new line after let line",    "let t = System.Guid.NewGuid()`n            ",           "ToByteArray")
    ,@("chain, second dot",          "let t =`n    s`n        .Substring(1)`n        ",       "Length")
    ,@("name on line above",         "let t = s`n          ",                                 "Length")
    ,@("name and space",             "let t = s ",                                            "Length")
    ,@("blank line between",         "let t =`n    s`n`n        ",                            "Length")
    ,@("comment on line above",      "let t =`n    s // comment`n        ",                   "Length")
    ,@("bad indent: same column",    "let t =`n    System.Guid.NewGuid()`n    ",              "none")
    ,@("bad indent: column 0",       "let t =`n    System.Guid.NewGuid()`n",                  "none")
    ,@("bad indent: left of expr",   "let t = System.Guid.NewGuid()`n    ",                   "none")
    ,@("after keyword then",         "let t = if true then`n            ",                    "none")
    ,@("name",                       "let t = s",                                             "Length")
    ,@("namespace",                  "let t = System.Guid",                                   "NewGuid")
    ,@("number",                     "let t = 1",                                             "none")
)
$results = foreach ($c in $cases) {
    $name, $code, $expect = $c
    & $driver esc
    & $driver settext ($pre + $code) | Out-Null
    & $driver keys "."
    $items = @(& $driver popup)
    $shown = $items[0] -ne "no completion window"
    $pass = if ($expect -eq "none") { -not $shown } else { $items -contains $expect }
    [pscustomobject]@{ Pass = $pass; Case = $name; Popup = if ($shown) { ($items | Select-Object -First 4) -join ", " } else { "-" } }
}
& $driver esc
$results | Format-Table -AutoSize | Out-String -Width 200
if ($results.Pass -contains $false) { exit 1 }
