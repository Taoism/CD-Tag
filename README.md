# CD-Tag

Audio file tagging and renaming utility (WPF).

This is a fork of [judwhite/CD-Tag](https://github.com/judwhite/CD-Tag), which stopped in 2011–2016 as
an unfinished project. The fork brings it up to date:

- **.NET 10** (`net10.0-windows`) with SDK-style projects and `CDTag.slnx`.
- **Builds standalone**: the original author's tagging library, [IdSharp](https://github.com/judwhite/IdSharp),
  is vendored as a git subtree under `External/IdSharp`.
- **Dark mode**: *View → Theme* offers *Use Windows setting* (default), *Light* and *Dark*, switchable
  at runtime. Dark title bars and, on Windows 11, a Mica backdrop.
- **Tag Album** in the CD-Tag 2.x style: release page, lookup (MusicBrainz, CUETools DB), track preview,
  NFO/M3U/SFV creation, renaming and FLAC/MP3 tagging on *Finish*.
- **Convert Audio**: WAV/FLAC to MP3 with LAME (`-V0` or `-b 320`), FLAC/MP3 to WAV.
- **Profiles**: edit, import from and export to CD-Tag 2.x (`.cfg` + NFO template).
- **Scene-style tagging** (optional, off by default): checks against the scene rules, bit depth / kHz and fix
  tags in release names.

## Running a release

Unzip and run `cdtag.exe`. The release folder looks like this:

```
cdtag.exe       starter: installs the .NET 10 Desktop Runtime on first run if it is missing, then starts CD-Tag
options.json    options (see below)
bin\            CD-Tag itself and its files
```

If the .NET 10 Desktop Runtime is missing, `cdtag.exe` downloads Microsoft's installer and runs it (Windows asks
for permission). If that is declined, a private copy is unpacked to `%LOCALAPPDATA%\CD-Tag\dotnet` instead.

## Encoders

**LAME and FLAC are not included.** Convert Audio and the FLAC-to-MP3 encoding need them:

- **LAME 3.100** (`lame.exe`, 64-bit), for MP3 encoding and MP3 decoding.
- **FLAC** (`flac.exe`), for decoding FLAC before MP3 encoding.

Put them in an `Encoders` folder, or choose them in *Tools → Options → Encoders*:

```
bin\Encoders\lame\lame.exe        (release build)
bin\Encoders\flac\flac.exe
```

When building from source, put them in `Encoders\lame\` and `Encoders\flac\` at the root of the repository;
the Debug build copies them next to `cdtag.exe`. That folder is in `.gitignore`.

The scene rules want MP3s made with LAME 3.100: `-V0` for CD, vinyl and other sources, `-b 320` for lossless
WEB sources. *Finish* reads the LAME header of each MP3 and reports other encoders or settings.

## options.json

A release has `options.json` next to `cdtag.exe`; CD-Tag reads and saves its options there. Without that file,
options are kept in `%LOCALAPPDATA%\CD-Tag\options.json`.

```json
    "EncodePreset": "LameV0",
    "enabled": "false"
```

`"enabled"` switches scene-style tagging: `"true"` turns on the scene rules check on *Finish*, bit depth / kHz
and fix tags in release names, the *Bit/kHz* and *Fix* boxes in Tag Album, scene genre names, and a check for
updated rules on [scenerules.org](https://scenerules.org) at startup (the rules are downloaded to
`%LOCALAPPDATA%\CD-Tag\Rules`, never included here). `"false"` (the default here) turns all of that off.

## NFO template

`bin\Defaults\blank.nfo` is a plain NFO template with every field. On first run it is copied to
`%LOCALAPPDATA%\CD-Tag\Profiles\blank.nfo`; choose it on a profile's *NFO* page, or use it as a starting point
for your own. Profiles and NFO templates are not part of this repository.

## Building

Requirements: Windows, .NET 10 SDK (or Visual Studio 2026).

```
dotnet build CDTag.slnx
dotnet test CDTag.slnx
dotnet run --project CDTag
```

Release build (the layout above, in `publish\CD-Tag`):

```
dotnet publish CDTag\CDTag.csproj -c Release -o publish\CD-Tag
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
