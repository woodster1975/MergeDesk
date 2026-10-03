# MergeDesk

<img src="src/MergeDesk.App/Assets/MergeDesk-logo-v2.png" width="96" alt="MergeDesk logo">

Free Windows mail merge for Windows 10 and 11. Import CSV or Excel recipients, personalise messages with merge fields, and create Outlook drafts or send through classic Outlook or Microsoft 365 / Outlook.com.

## Features

- Visual email editor with fonts, formatting and inline pictures; plain-text email support.
- To, CC, BCC and Subject merge fields, column mapping and saved templates/signatures.
- Global attachments and per-recipient filename patterns.
- Validation, recipient selection, individual preview and local dry-run export.
- Outlook drafts, confirmed sending, test messages and duplicate-send protection.
- SQLite history, recovery, searchable help and a captioned walkthrough video.

## Use the application

Download an installer or portable ZIP from this repository's Releases once the maintainer publishes one. Extract the complete portable ZIP before opening MergeDesk.App.exe. The packaged app includes its .NET runtime.

See [User guide](USER-GUIDE.md) for instructions, and [Microsoft mailbox setup](IMPLEMENTATION-NOTES.md#microsoft-365--outlookcom-new-or-classic-outlook) for configuration. Full provider setup instructions are in [Implementation notes](IMPLEMENTATION-NOTES.md).

## Build and run

Requires Windows and the .NET 8 SDK (or Visual Studio 2022 with .NET desktop development).

```powershell
dotnet restore MergeDesk.sln
dotnet build MergeDesk.sln -c Release
dotnet run --project src/MergeDesk.App -c Release
```

Run all simulated checks with `./scripts/Test.ps1`. Build a self-contained portable package with `./scripts/Publish.ps1`. Build an installer using `installer/MergeDesk.iss` in Inno Setup after publishing.

## Structure

- src/MergeDesk.App: WPF / MVVM interface, editor and Outlook connections.
- src/MergeDesk.Core: merge engine, validation and delivery interfaces.
- src/MergeDesk.Infrastructure: imports, SQLite, persistence and mail providers.
- tests: console-based core, WPF and simulated provider checks.
- installer: Inno Setup project, logo and build helper.

Live Outlook behaviour and installation require manual testing; automated tests never send real messages. See [Validation](VALIDATION.md), [Contributing](CONTRIBUTING.md) and [GitHub setup](GITHUB-SETUP.md).

## Licence

[MIT](LICENSE.txt) — Copyright (c) 2026 Paul Woodhouse. Free for personal and commercial use, modification and redistribution with the licence notice preserved. Bundled libraries retain their [third-party licences](THIRD-PARTY-NOTICES.md).
