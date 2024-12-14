# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).


## [Unreleased]

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

[Unreleased]: https://github.com/goswinr/Fesh/compare/0.16.0...HEAD
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

<!-- use to get tag dates:
git log --tags --simplify-by-decoration --pretty="format:%ci %d"
-->
