# Publish MergeDesk to GitHub

This folder is the repository root. Open it in GitHub Desktop, or use Git here. The outer workspace, local SDK/cache and existing downloads are not part of the repository.

1. Create an empty GitHub repository named MergeDesk. Do not initialise it with a README, licence or gitignore: those are already included here.
2. Review the files and make the initial commit using your Git identity.
3. Add the new repository URL as origin and push main. In GitHub Desktop, use Publish repository and choose the desired visibility.

Command-line equivalent (replace OWNER with your GitHub account):

```powershell
git add .
git commit -m "Initial MergeDesk application"
git remote add origin https://github.com/OWNER/MergeDesk.git
git push -u origin main
```

The Windows build workflow runs the console-based test suites rather than `dotnet test`. No Outlook login or real mail sending occurs. The manually triggered Package portable application workflow creates a downloadable build artifact; it does not publish a public release. GitHub-hosted workflows have not yet been run.

To make a release, run the package workflow, download its ZIP, and attach it to a GitHub Release. For an installer, run scripts/Publish.ps1 locally, then open installer/MergeDesk.iss in Inno Setup and compile it, or run installer/Build-Installer.ps1. Keep the stable AppId for future upgrades. The installer is unsigned unless you separately configure code signing.

MIT licence: copyright (c) 2026 Paul Woodhouse. Third-party notices are included. Repository source contains the original help video (about 7 MB); generated installer EXEs and portable downloads belong in Releases, not Git history.
