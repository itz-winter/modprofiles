# ModProfileSwitcher

A Windows WinForms application (built with .NET 8) to manage Minecraft mod profiles by moving mod JARs between the root `mods/` folder and profile subfolders. Supports downloading from Modrinth (including collections), resource/shader pack deployment, and produces both a portable ZIP and an Inno Setup installer.

Quick build & run

1. Build:

```powershell
cd src
dotnet build -c Release
```

2. Run from Visual Studio or:

```powershell
cd src
dotnet run -c Release
```

Publish (self-contained single-file exe for Windows x64):

```powershell
cd src
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Installer & portable packages

- The project includes `installer/build-installer.ps1` to build the Inno Setup installer (requires Inno Setup and optionally the GitHub CLI for releases).
- The built artifacts are placed in `installer/output/`.

License

This project is distributed under the MIT license — see `LICENSE`.

Notes

- If you want me to create a GitHub repository and push the code for you, make sure you are signed in to the GitHub CLI (`gh auth login`) or provide the remote URL; I will try to use `gh` automatically.
