# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.31.0] - 2025-12-19
### Fixed
- Show error line numbers for exceptions in hosted net8 context by using multiemit

## [0.30.1] - 2025-12-19
### Changed
- net472, net8, net9 nuget with suffix, net10 nuget without suffix
- don't load nuget packages while typing them to avoid loading unwanted packages

## [0.29.3] - 2025-12-15
### Changed
- Build NET core nuget targeting F# 8 and .Net 8 to try fix assembly load issues in Rhino and Revit

## [0.29.2] - 2025-12-14
### Fixed
- updating velopack channels

## [0.29.0] - 2025-12-14
### Changed
- support F# 10 and .Net 10
- fix bug in bracket highlighting

## [0.28.1] - 2025-10-04
### Fixed
- app expiry

## [0.28.0] - 2025-06-23
### Fixed
- in completions only add `()` to the end of a `unit -> unit` function name if it is at line start.

## [0.27.0] - 2025-05-25
### Added
- canRunAsync field to FeshHostSettings

## [0.26.3] - 2025-05-14
### Fixed
- fix net7 nuget build

## [0.26.2] - 2025-05-14
### Changed
- use latest F# 9.3

## [0.26.1] - 2025-04-20
### Changed
- publish net472 and net7 nuget separately due to build errors in Fesh.Rhino and Fesh.Revit

## [0.26.0] - 2025-04-16
### Added
- improve completion edge cases
- debounce selection highlighting

## [0.25.0] - 2025-03-19
### Fixed
- use AvalonLog 0.20.0 to fix Velopack missing method exception when hosting in Revit

## [0.24.1] - 2025-03-18
### Fixed
- use AvalonLog 0.19.0 to fix type load exception when hosting in Revit

## [0.24.0] - 2025-03-16
### Fixed
- allow completion for `_.`
- no completions on decimal period
- adjust cursor for  `#r ""` completion
- re-extend segment on completion when typing inside

## [0.23.0] - 2025-02-16
### Fixed
- include FSharp.Core.xml

## [0.22.0] - 2025-02-15
### Fixed
- Completions never replace next word too eagerly
### Changed
- keep the settings folder in local AppData, even when host is somewhere else
- don't install an update automatically if more than one instance of Fesh is running.

## [0.21.0] - 2025-01-20
### Fixed
- keep the settings folder in local AppData, even when host is somewhere else
- don't install an update automatically if more than one instance of Fesh is running.

## [0.20.0] - 2025-01-18
### Changed
- for portable version the location of SettingsFolder is inside the app folder now
- update to latest FSharp.Compiler.Service
- improved Readme and error messages

## [0.19.0] - 2025-01-12
### Changed
- create a Velopack installer
- enable auto updating via Velopack
- changed install Folder for Velopack to `.\AppData\Local\Fesh.net48\` for .Net 4.8
- changed Settings Folder to `.\AppData\Local\Fesh.Settings\` or `.\AppData\Local\Fesh.{host}.Settings\` if used via Nuget.
- line wrap off by default
### Added
- syntax highlighting for numeric suffixes

## [0.16.0] - 2024-12-14
### Added
- command for opening a file in VS Code
- command for closing and deleting a file
- command for renaming files
### Changed
- location of SettingsFolder
- simplify type signature of C# style optional args in tooltips

## [0.15.0] - 2024-11-22
### Added
- Support for F# 9.0

## [0.14.3] - 2024-11-10
### Added
- Release via Github Actions

## [0.14.2] - 2024-11-10
### Removed
- remove false warning about missing cancellation token

## [0.14.0] - 2024-11-03
### Added
- add FSI.Shutdown command for hosting

## [0.13.0] - 2024-10-20
### Fixed
- fix crash on assembly load conflict
- faster completion window
- optional tracking of evaluated code via settings

## [0.12.0] - 2024-10-13
### Added
- add check for updates on startup
- fix version number in Title
- enable cancellation of running code in net48 and net8

## [0.11.1] - 2024-10-06
### Fixed
- fix expiry date

## [0.11.0] - 2024-10-06
### Fixed
- fix VisualLine not collapsed crash

## [0.10.0] - 2024-08-25
### Added
- enable DefaultCode in host settings

## [0.9.0] - 2024-07-05
### Added
- first public release

[Unreleased]: https://github.com/goswinr/Fesh/compare/0.31.0...HEAD
[0.31.0]: https://github.com/goswinr/Fesh/compare/0.30.1...0.31.0
[0.30.1]: https://github.com/goswinr/Fesh/compare/0.29.3...0.30.1
[0.29.3]: https://github.com/goswinr/Fesh/compare/0.29.2...0.29.3
[0.29.2]: https://github.com/goswinr/Fesh/compare/0.29.0...0.29.2
[0.29.0]: https://github.com/goswinr/Fesh/compare/0.28.1...0.29.0
[0.28.1]: https://github.com/goswinr/Fesh/compare/0.28.0...0.28.1
[0.28.0]: https://github.com/goswinr/Fesh/compare/0.27.0...0.28.0
[0.27.0]: https://github.com/goswinr/Fesh/compare/0.26.3...0.27.0
[0.26.3]: https://github.com/goswinr/Fesh/compare/0.26.2...0.26.3
[0.26.2]: https://github.com/goswinr/Fesh/compare/0.26.1...0.26.2
[0.26.1]: https://github.com/goswinr/Fesh/compare/0.26.0...0.26.1
[0.26.0]: https://github.com/goswinr/Fesh/compare/0.25.0...0.26.0
[0.25.0]: https://github.com/goswinr/Fesh/compare/0.24.1...0.25.0
[0.24.1]: https://github.com/goswinr/Fesh/compare/0.24.0...0.24.1
[0.24.0]: https://github.com/goswinr/Fesh/compare/0.23.0...0.24.0
[0.23.0]: https://github.com/goswinr/Fesh/compare/0.22.0...0.23.0
[0.22.0]: https://github.com/goswinr/Fesh/compare/0.21.0...0.22.0
[0.21.0]: https://github.com/goswinr/Fesh/compare/0.20.0...0.21.0
[0.20.0]: https://github.com/goswinr/Fesh/compare/0.19.0...0.20.0
[0.19.0]: https://github.com/goswinr/Fesh/compare/0.16.0...0.19.0
[0.16.0]: https://github.com/goswinr/Fesh/compare/0.15.0...0.16.0
[0.15.0]: https://github.com/goswinr/Fesh/compare/0.14.3...0.15.0
[0.14.3]: https://github.com/goswinr/Fesh/compare/0.14.2...0.14.3
[0.14.2]: https://github.com/goswinr/Fesh/compare/0.14.0...0.14.2
[0.14.0]: https://github.com/goswinr/Fesh/compare/0.13.0...0.14.0
[0.13.0]: https://github.com/goswinr/Fesh/compare/0.12.0...0.13.0
[0.12.0]: https://github.com/goswinr/Fesh/compare/0.11.1...0.12.0
[0.11.1]: https://github.com/goswinr/Fesh/compare/0.11.0...0.11.1
[0.11.0]: https://github.com/goswinr/Fesh/compare/0.10.0...0.11.0
[0.10.0]: https://github.com/goswinr/Fesh/compare/0.9.0...0.10.0
[0.9.0]: https://github.com/goswinr/Fesh/releases/tag/0.9.0


