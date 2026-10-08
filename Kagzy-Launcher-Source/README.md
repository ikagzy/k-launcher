# K-Launcher

A lightweight, fast, and customizable desktop launcher for Windows, built for smooth performance and global accessibility.

## Key Features

- **Lightweight:** Designed for a low memory footprint; actual usage varies by system and workload.
- **40 Languages:** Includes localization for 40 languages.
- **Customizable Themes:** Choose from built-in themes to match your setup.
- **Fast, Minimal UI:** A clean interface for launching games and applications.
- **Automatic Detection:** Scans common install locations and Windows app registration data for supported launchers and apps. Detection can vary by installation and system configuration.

## Downloads

Download the compiled Windows executable from itch.io:

- [K-Launcher on itch.io](https://itzkagzy.itch.io/k-launcher)

The executable is a self-contained Windows x64 build. On first launch, it copies itself to the current user's local application data folder and starts the installed copy.

## Build from Source

### Requirements

- Windows
- .NET 10 SDK

### Build

```powershell
dotnet restore .\KLauncher.csproj
dotnet build .\KLauncher.csproj -c Release
```

### Publish a self-contained Windows x64 executable

```powershell
dotnet publish .\KLauncher.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeAllContentForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=None `
  -o .\dist
```

The published executable will be at `dist\KLauncher.exe`.

## Built With

- **Language/UI:** C# and WPF
- **Framework:** .NET 10
- **Structure:** Modular services, models, and view models

## License

This project is licensed under the **GNU General Public License v3.0 (GPL-3.0)**. 
Developed by **itzkagzy**. Free to use, modify, and distribute as long as all derivative works remain open-source under the same license. See [LICENSE](./LICENSE) for the full license text.
