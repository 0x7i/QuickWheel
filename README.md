# Radial Command Wheel

Radial Command Wheel is a native Windows 11 launcher. Hold a configurable global shortcut to open a polished radial menu at the pointer, then click an application, file, folder, command, or managed group.

Version 1.1 adds cleaner automatic names, icon-derived accent colors, adaptive wedge geometry, immediate post-selection dismissal, whole-row group navigation, portable configuration transfer, and complete English/Arabic switching with RTL layout.

## Quick start

The packaged x64 executable is:

`dist/RadialCommandWheel.exe`

It is self-contained and does not require a separate .NET installation. Launch it normally to open management, or launch it with `--background` to remain in the notification area.

The default activation shortcut is **Ctrl + Alt + Space**.

1. Press and hold the activation shortcut.
2. Move the pointer over a sector.
3. Click while the shortcut is still held.
4. The wheel closes immediately after an application, file, folder, or command is selected.
5. Release the shortcut, press **Esc**, click the backdrop, or right-click to cancel without running anything.
6. A group selection keeps the wheel open and replaces its contents. Use the center **Back** control to return.

Hover never executes an item. A command runs only after a mouse-up on the same sector where the press began. After committing an item, the still-held shortcut is latched until every required key has been released; a fresh press is required before the wheel can open again.

## Wheel management

- Add and edit applications, files, folders, commands, and managed groups.
- Each wheel supports 12 items.
- Groups can contain launch items but never other groups.
- Drag items onto one another to reorder them.
- Choose automatic target icons, the built-in icon library, or validated PNG/JPG/ICO/BMP/SVG files.
- Set a per-item name, label visibility, and accent color.
- The target picker uses full-width cards for the main wheel and every group. The name, row background, and arrow all open that editing target; the list also supports keyboard navigation.

### Automatic display names

An item with no explicit display name derives a clean caption from its target:

- `Call of Duty Black Ops III.url` → `Call of Duty Black Ops III`
- `MyGame.lnk` → `MyGame`
- `My Notes.txt` → `My Notes`

The target path and extension on disk never change. Manually entered display names are preserved exactly, including dots and extensions. Unknown dotted suffixes such as `release.v2` are left intact when the suffix is not a recognized technical extension.

### Automatic icon colors

The item editor can analyze the resolved target/custom icon and choose an accent that coordinates with the active theme. The analyzer:

- Samples up to a 32×32 rendering of the icon.
- Ignores transparent, near-black, near-white, and low-saturation pixels.
- Scores significant hues by saturation and usable luminance.
- Adjusts saturation and lightness for contrast against the selected theme background.
- Caches results by icon identity, file timestamp, and theme colors.

The editor clearly indicates automatic versus manual color mode. Manual selection or hex entry switches the item to manual color and is never replaced in the background. **New suggestion** requests a fresh result, and the automatic checkbox can be enabled again at any time.

### Adaptive geometry

The preview and live overlay share one geometry model. Sector angles distribute evenly, inner and outer radii adapt to the item count, and separator gaps shrink for dense wheels.

A one-item wheel uses a dedicated complete outer ring rather than a narrow wedge. Automated rendering and hit tests cover 1, 2, 3, 4, and 12 items, including center Back selection inside groups.

## Appearance and themes

The application includes four built-in themes and supports custom theme creation. Users can adjust:

- Accent, sector, text, and backdrop colors.
- Wheel opacity and backdrop dimming.
- Wheel size, animation enablement, and duration.
- Active saved theme.

The management preview does not own a launcher reference and cannot execute commands.

## Language and layout

General Settings includes a live **Language** selector:

- English
- العربية

The entire management interface, overlay captions, tooltips, dialogs, validation messages, empty states, status messages, and notification-area menu switch immediately. Arabic uses right-to-left layout, while file paths and technical values remain left-to-right. User-defined item names, commands, filenames, and paths are never translated.

The language is stored in configuration and restored on the next launch. Missing Arabic translations fall back to English.

## Export and import

General Settings provides standard Windows **Export configuration** and **Import configuration** actions.

Exports contain only the portable configuration: wheels, groups, ordering, custom names, targets, colors and color modes, themes, shortcut, language, and global settings. Logs, diagnostics, processes, and unrelated files are excluded.

Import works in two stages:

1. The complete file is parsed and validated.
2. A localized preview shows item/group/theme counts, language, shortcut, and any missing local targets.

Nothing is applied until **Replace current configuration** is explicitly chosen. Cancellation or validation failure leaves the active configuration untouched. A successful import first copies the previous configuration to a timestamped `config.json.pre-import-*` recovery file, then atomically writes the imported configuration.

Imported commands remain inert during parsing, preview, and commit. The imported shortcut is passed through the existing Windows registration/conflict reporting path and is never silently changed.

See [Export/import format](docs/EXPORT_FORMAT.md) for the complete JSON contract.

## Configuration and compatibility

Local data remains in the normal per-user location:

- Configuration: `%LOCALAPPDATA%\RadialCommandWheel\config.json`
- Previous valid backup: `%LOCALAPPDATA%\RadialCommandWheel\config.json.bak`
- Pre-import recovery copies: `%LOCALAPPDATA%\RadialCommandWheel\config.json.pre-import-*`
- Diagnostic log: `%LOCALAPPDATA%\RadialCommandWheel\app.log`

The configuration schema is now 2. Existing version-1 files load without resets:

- Existing item colors remain **manual**.
- The interface defaults to English when no language was stored.
- Wheels, groups, custom icons, themes, ordering, and shortcuts are retained.
- The schema is upgraded on the next save.

Saves use a flushed temporary file and atomic replacement. Corrupt primary files are preserved, and the last valid backup is used when available.

## Background and tray behavior

Closing management hides it while the wheel stays active. The notification-area menu supports opening management, showing the wheel at the pointer, and exiting fully.

Windows controls whether the icon appears directly on the taskbar or in overflow. Use **Settings > Personalization > Taskbar > Other system tray icons** to move or pin it. Applications cannot force Windows to keep a tray icon hidden.

Explicit Exit saves settings, unregisters the hotkey and temporary Escape registration, closes the overlay, removes the tray icon, and terminates the process.

## Launch behavior and security

- Application items try to activate an existing matching top-level window before launching a new instance.
- Files and folders use normal Windows shell associations.
- Executable commands use separate process arguments rather than a concatenated shell string.
- Scripts use their standard Windows association.
- Windows may display its normal UAC prompt when required; the app never elevates itself or approves prompts.
- SVG icons are parsed as restricted geometry with DTDs disabled and active/external content rejected.
- Import and preview code never contains a launcher call.

## Build from source

Requirements:

- Windows 10 version 2004/build 19041 or newer, or Windows 11
- x64 target
- .NET 8 SDK or newer with Windows Desktop support
- Network access for first-time Microsoft runtime/reference-pack restore

Run:

```powershell
.\scripts\Build.ps1
```

The script restores, builds Release with zero-warning expectations, runs the automated suite, republishes the icon, creates a self-contained single-file executable, and prints its SHA-256. It automatically uses an SDK at `%LOCALAPPDATA%\Microsoft\dotnet` when present; set `DOTNET_EXE` to override.

Manual release commands:

```powershell
dotnet restore RadialCommandWheel.sln --configfile NuGet.Config
dotnet build RadialCommandWheel.sln -c Release --no-restore
dotnet run --project tests\RadialCommandWheel.Tests\RadialCommandWheel.Tests.csproj -c Release --no-build
dotnet publish src\RadialCommandWheel\RadialCommandWheel.csproj -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o dist
```

## Project layout

```text
src/RadialCommandWheel.Core/
  schema-2 models and legacy migration
  display-name and target validation
  versioned export/import envelope
  atomic persistence, recovery, undo, argument parsing

src/RadialCommandWheel/
  WPF management UI and RTL localization
  transparent overlay and adaptive radial renderer
  icon extraction, safe SVG, and automatic color suggestions
  global shortcut latch and watchdog
  launcher, tray, startup, import preview, and lifecycle

src/RadialCommandWheel.Tools/
  C# icon generator and hotkey-conflict test utility

tests/RadialCommandWheel.Tests/
  dependency-free automated regression/integration runner

docs/
  technical decisions, export format, tests, and limitations

scripts/Build.ps1
  reproducible Release build and publish workflow
```

## Troubleshooting

- **Shortcut does not open:** check registration status under Keyboard shortcut. Choose another modifier combination if another process owns it.
- **Tray icon seems missing:** inspect taskbar overflow and Windows taskbar icon settings.
- **A path is red after import:** the entry was preserved. Use Browse or edit the path for the destination PC.
- **An automatic color looks unsuitable:** choose a manual color, or theme the wheel and request a new suggestion.
- **Arabic text looks cramped:** use a window width of at least 1120 pixels; the supported minimum was raised to prevent editor clipping.
- **Startup command still points to an old location:** launch the moved executable once so its current-user startup entry is refreshed.
- **Configuration issue:** review `app.log` or open the configuration folder from General Settings.

Detailed evidence and unresolved platform limits are in [technical decisions](docs/TECHNICAL_DECISIONS.md) and [test plan and results](docs/TEST_PLAN_AND_RESULTS.md).

