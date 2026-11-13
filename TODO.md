# CD-Tag TODO

- [x] Profiles live in the root directory of wherever `cdtag.exe` is started from (load and save there, like 2.x), instead of `%LOCALAPPDATA%\CD-Tag\Profiles`.
- [x] Finish moves files out of CD1/CD2/... (and cd01 ... cd10) folders into the album folder (101, 201, ...) and deletes those folders. Released in 3.2.2; still to be tested on real albums.
- [ ] Linux builds for Debian and Ubuntu. **Decision (2026-11-08): Rust rewrite** (native, no .NET runtime).
  Rough plan, 6-10 weeks: core (profiles, naming, NFO, scene rules, checksums, EAC log, CUE) -> tags + Finish ->
  lookup + Convert Audio -> ripping (needs the real drive) -> interface -> .deb/.exe packaging + real-album tests.
  - Option A, Rust rewrite (chosen): one codebase that builds for Windows and Linux. Everything is rewritten
    (~30k lines of C# + ~5.5k lines of XAML today, plus the IdSharp tag library), so the existing tests are
    the spec to check the port against.
    - Interface: a cross-platform toolkit (egui, iced, Slint or Tauri).
    - Tags: lofty; FLAC: claxon/metaflac; disc ID: discid; MusicBrainz: musicbrainz_rs.
    - CD drive: libcdio/cdparanoia on Linux, IOCTLs (or libcdio) on Windows.
    - Encoders: system lame/flac on Linux (apt), bundled on Windows.
    - File names: Linux is case-sensitive (CD01 vs cd01, DNR.cfg vs dnr.cfg); match without case on purpose.
    - Settings in ~/.config/cd-tag; EAC registry settings become options on Linux.
    - Packages: cargo-deb for Debian/Ubuntu, .exe for Windows.
  - Option B, Avalonia: keep the C# models/view models and tests, redo the windows in Avalonia XAML,
    plus the same Linux work for CD drive, encoders, file names and settings. Self-contained .deb.

## Security (review 2026-11-09)

- [x] **Updater doesn't check the download (medium).** `Launcher/Updater.cs` unpacks whatever zip the `--update` URL
  gives and copies it over the program folder, as administrator when the folder needs it. Only HTTPS protects it.
  - Check the zip's SHA-256 against the `digest` field GitHub's release API gives for each asset (`UpdateChecker.Parse`),
    and pass the hash to the updater.
  - In the updater, refuse any URL that isn't `https://github.com/Taoism/CD-Tag/releases/download/...`, since the
    URL arrives on the command line of an elevated process.
- [x] **Elevated updater runs from %TEMP% (low-medium).** The `runas` copy of cdtag.exe starts in a user-writable temp folder
  and unpacks there before copying into Program Files. A process running as the same user could plant a DLL next to
  it or swap files between unpack and copy, and so get admin rights. When elevated, unpack to a folder only admins can
  write (or check the hashes again just before copying), and start the updater with `SetDefaultDllDirectories`.
- [x] **.NET runtime installer runs elevated without a signature check (low).** `Launcher/Program.cs InstallRuntime`
  downloads `windowsdesktop-runtime-win-x64.exe` and runs it with `runas`. Check its Authenticode signature
  (`WinVerifyTrust`, signer Microsoft Corporation) first.
- [x] **CTDB and AccurateRip over plain HTTP (low).** `CueToolsDbClient` (`http://db.cuetools.net`) and `AccurateRip`
  (`http://www.accuraterip.com`) can be tampered with on the network, giving wrong metadata or "accurate" results that
  aren't. Use `https://db.cuetools.net` (check it answers). AccurateRip does answer over HTTPS (checked 2026-11-12:
  same DriveOffsets.bin and dBAR files), so it moves to https too.
- [x] **Discogs token stored as plain text (low).** `options.json` next to cdtag.exe holds `DiscogsToken` in the clear.
  Encrypt it with DPAPI (`ProtectedData`, CurrentUser) and keep reading old plain values.
- [x] **Bundled IdSharp trusts sizes in the file (low, crash only).** `ID3v2Utils.DecompressFrame` allocates
  `compressedSize` bytes from the frame header (up to 256 MB) and inflates with no limit. A crafted MP3 can use up
  memory or crash Tag/Show Tags. Limit frame sizes to what is left in the file and cap the inflated size.
- Done in 3.2.8: the update zip is checked against GitHub's SHA-256 and must come from Taoism/CD-Tag releases. As
  administrator, the starter runs in place (it renames itself to cdtag.exe.old) and unpacks into an admin-only folder.
  The .NET installer must be signed by Microsoft. The Discogs token is stored as `DiscogsTokenProtected` (DPAPI).
  IdSharp limits compressed frames.
- Done in 3.2.9: CTDB goes over HTTPS to db.cue.tools (db.cuetools.net's certificate only covers cue.tools); CTDB cover links are upgraded to https; rip verify asks with metadata=none (the metadata=fast lookup was giving 502s). AccurateRip stayed HTTP then.
- Next release: AccurateRip over HTTPS too (www.accuraterip.com serves the same files there).
