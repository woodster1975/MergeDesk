# Contributing

Use Windows and the .NET 8 SDK. Run `./scripts/Test.ps1` before submitting a pull request. Tests use simulated mail providers; do not send real mail as part of automated tests. Keep UI changes in the WPF app, merge logic in Core, and provider/storage changes in Infrastructure.

Describe the problem, resulting behaviour and validation in pull requests. Use example.com addresses and synthetic recipient data. Do not commit recipient files, mailbox credentials, saved projects, databases or logs. Contributions are distributed under the project MIT licence.
