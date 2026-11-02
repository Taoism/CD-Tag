# CD-Tag

Audio file tagging and renaming utility (WPF).

This is a fork of [judwhite/CD-Tag](https://github.com/judwhite/CD-Tag), which stopped in 2011–2016 as
an unfinished project. The fork brings it up to date:

- **.NET 10** (`net10.0-windows`) with SDK-style projects and `CDTag.slnx`.
- **Builds standalone**: the original author's tagging library, [IdSharp](https://github.com/judwhite/IdSharp),
  is vendored as a git subtree under `External/IdSharp`.
- **Dark mode**: *View → Theme* offers *Use Windows setting* (default), *Light* and *Dark*, switchable
  at runtime. Dark title bars and, on Windows 11, a Mica backdrop.
- Error banner fixed (unhandled errors used to be swallowed silently).

## Project status

The file browser, ID3v1/ID3v2 editing, *Tag Album* and the profile wizard work. The *Split CUE*,
*Checksum*, *Verify EAC log*, *Encoding inspector*, *Mass tag* and *Options* windows are still empty
stubs from the original project.

## Building

Requirements: Windows, .NET 10 SDK (or Visual Studio 2026).

```
dotnet build CDTag.slnx
dotnet test CDTag.slnx
dotnet run --project CDTag
```

Settings are stored in `%LOCALAPPDATA%\CD-Tag` (`appearance.json`, `mainWindow.json`, `windows.json`, `Profiles\`).

## Theming

All colours live in two palettes, `CDTag/Themes/Palettes/Light.xaml` and `Dark.xaml`, which define the
same keys. Styles in `CDTag/Themes/Default` only reference those keys with `DynamicResource`;
`CDTag.Views/Theming/ThemeManager.cs` swaps the palette at runtime. To add a colour, add the key to both
palettes.

## Updating IdSharp

```
git subtree pull --prefix=External/IdSharp https://github.com/judwhite/IdSharp.git master --squash
```

## License

IdSharp is MIT licensed (see `External/IdSharp/LICENSE`).
