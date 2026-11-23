# CLAUDE.md

Guidance for Claude (and other contributors) working in this repository. Read this before making
changes. User-facing documentation is in `README.md`; this file covers how the code is put together,
why, and the traps.

---

## What Island is

A small floating productivity widget for **Windows 10 and 11**, written in **.NET 8, WPF and C#**,
using MVVM via `CommunityToolkit.Mvvm`.

- **The island.** A borderless, transparent, always-on-top circle. You can drag it anywhere, and on
  release it snaps to the nearest corner of the monitor's work area. It never takes focus and isn't
  shown in the taskbar or Alt+Tab.
- **The menu.** Clicking the island expands it into a panel of buttons. The panel opens *away from*
  the island's corner. Group buttons open sub-panels.
- **Actions.** Each button has exactly one action:

  | Action | What it does |
  |---|---|
  | `LaunchApp` | Starts an app, shortcut or file |
  | `OpenFolder` | Opens a folder in Explorer |
  | `OpenFile` | Opens a file with its default app |
  | `OpenUrl` | Opens a URL; with no URL, opens the browser's home page |
  | `Group` | Opens a sub-panel |
  | `ClaudeCodeHere` | Opens Windows Terminal in the current Explorer folder and runs `claude` |
  | `Screenshot` | Snip, full screen or active window |
  | `OpenSettings` | Opens the settings window |
  | `RunScript` | Runs a user script in any language, with a JSON reply protocol |

- **The Settings window.** A normal window that can take focus. Its Buttons tab has a button tree
  with drag & drop, an editor, an icon picker and a live preview. It also has a General tab, a
  script run log, import/export, and the global hotkey and start-with-Windows settings.
- **The rest:** tray icon, single instance, themes, toasts, a config file that reloads live, and a
  self-contained single-file publish.

The original product spec is the source of truth for intended behavior. Its content is reflected in
`README.md` and summarized below.

---

## Repository layout

```
Island.sln
Directory.Build.props        LangVersion latest, Nullable enable, ImplicitUsings disabled
src/Island.Core/             net8.0. Platform-neutral logic: NO WPF, NO Win32. Unit-tested anywhere.
  Models/                    IslandConfig, GeneralSettings, ButtonItem, IconSpec, IslandState, Actions.cs
  Config/                    ConfigSerializer (+ConfigMigrations), ConfigStore, DefaultConfig,
                             ButtonValidator, ButtonTree
  Layout/                    Geometry (PixelRect, Corner, MonitorInfo, Easing), SnapCalculator, PanelPlacement
  Scripts/                   PlaceholderExpander, InterpreterResolver, ScriptOutputParser, ProcessRunner,
                             RunLog, CommandLine (Windows quoting/splitting)
  Util/                      ExplorerPath, TerminalCommandBuilder, HotkeyGesture, DropFactory
src/Island/                  net8.0-windows10.0.17763.0 WPF app (UseWPF + UseWindowsForms)
  App.xaml(.cs)              Composition root: single instance, services, tray icon, error handling
  IslandController.cs        Expand/collapse, Esc + click-outside, hotkeys, island context menu,
                             implements IIslandHost (settings / run log / popups / hide-for-capture)
  Views/                     IslandWindow, PanelWindow, SettingsWindow, IconPickerWindow, ToastWindow,
                             OutputPopupWindow, RunLogWindow + RunLogView (shared UserControl)
  ViewModels/                PanelViewModel (TileViewModel, PanelLevel), SettingsViewModel
  Controls/                  IconView (renders IconSpec), Spinner, value converters
  Services/                  ConfigService, ThemeService, ToastService, HotkeyService, IconService (+GlyphCatalog),
                             ForegroundTracker, ExplorerFolderResolver (+CurrentFolderService), MonitorService,
                             ScreenshotService, StartupService, ShellHelpers (ShellLauncher, ClipboardHelper),
                             ActionRunner (+AppServices, ActionContext, IActionHandler), Handlers/
  Interop/                   NativeMethods (user32, shell32, shcore, dwmapi, gdi32, kernel32), WindowHelper
  Themes/Controls.xaml       Theme-aware control styles + fallback palette
  Assets/                    island.ico / island.png (generated), glyphs.json (curated glyph list, embedded)
  Properties/PublishProfiles/SingleFile.pubxml
  app.manifest               PerMonitorV2 DPI awareness, Win10 supportedOS, asInvoker
tests/Island.Tests/          xUnit tests for Island.Core (net8.0)
samples/scripts/             folder-stats.ps1, word_count.py, timestamp.js (JSON-protocol examples)
README.md                    User documentation (usage, actions, script protocol, config, layout)
```

---

## Commands

```bash
dotnet build Island.sln
dotnet test tests/Island.Tests                       # 130 tests, run on any OS
dotnet run --project src/Island                      # Windows only
dotnet publish src/Island/Island.csproj -p:PublishProfile=SingleFile
#   -> publish/win-x64/Island.exe (self-contained, compressed, ~77 MB)
```

### Building on Linux (cloud sessions)

The WPF project sets `EnableWindowsTargeting=true`, so it **compiles** on Linux, including XAML
markup compilation. It **cannot run** there.

Ubuntu's `dotnet-sdk-8.0` apt package is source-built and lacks the
`Microsoft.NET.Sdk.WindowsDesktop` targets. Without them you get:
`MSB4019: ... Microsoft.NET.Sdk.WindowsDesktop.targets was not found`.

What worked in a previous session:

1. `apt-get install -y dotnet-sdk-8.0`. `dot.net` and `builds.dotnet.microsoft.com` were blocked;
   `packages.microsoft.com` and `api.nuget.org` were reachable.
2. Download Microsoft's SDK package from
   `https://packages.microsoft.com/ubuntu/22.04/prod/pool/main/d/dotnet-sdk-8.0/dotnet-sdk-8.0_8.0.409-1_amd64.deb`.
   Extract it with `dpkg -x`, without installing.
3. Copy `usr/share/dotnet/sdk/8.0.409/Sdks/Microsoft.NET.Sdk.WindowsDesktop` into
   `/usr/lib/dotnet/sdk/<installed version>/Sdks/`.

After that, the WindowsDesktop targeting and runtime packs come from NuGet automatically, and the
win-x64 single-file publish works on Linux too.

### Verifying UI changes

- **No real screenshots on Linux.** Real screenshots need a Windows machine, for example one
  attached via Remote Control.
- **Mockups instead.** Design mockups of all main views were rendered in a browser from the XAML
  values, using Fluent UI SVG icons and the Inter font. If you produce more, **label them as
  mockups**; never present them as screenshots.
- **Review the XAML by hand.** Many WPF bugs only show at runtime: resource lookup order, layout
  feedback loops, binding errors.

---

## Architecture and key decisions

### Island.Core vs. Island

All logic that can be pure lives in `Island.Core` and is unit-tested: config serialization, snap
math, placeholder expansion, script output parsing, interpreter resolution, the process runner, tree
moves, validation, hotkey parsing and Explorer URL conversion. The WPF project holds only Windows,
UI and interop code.

When you add logic, put it in Core if it doesn't need WPF or Win32, and add tests for it.

Core code must behave identically on every OS. Don't use `Path.GetFileName` and friends on Windows
paths in Core: Linux doesn't treat `\` as a separator. Use the string helpers instead, as in
`DropFactory.FileNameWithoutExtension` and `ExplorerPath.FromLocationUrl`.
`Path.GetExtension` is fine.

### Config model and JSON

- **Observable models.** Models are `ObservableObject`s with `[ObservableProperty]` fields, so the
  settings UI binds directly to them.
- **UI-only state.** `ButtonItem.IsSelected`, `IsExpanded` and `Warning` are marked
  `[property: JsonIgnore]`. Note the attribute syntax: it has to be a **separate** attribute list
  from `[ObservableProperty]`, or the build fails.
- **Computed properties.** Every get-only computed property on a serialized model must have
  `[JsonIgnore]`. Examples: `ButtonItem.IsGroup` and `DisplayTooltip`, and `RunLogEntry.Summary`,
  `DurationText` and `FullText`. Otherwise it gets serialized.
- **Action polymorphism.** `[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]` plus
  `[JsonDerivedType(..., "LaunchApp")]` and so on, on `ActionBase`.
  - **Never add a serializable property named `Type` to actions.** It caused a "duplicate 'type'
    metadata property" error. `ActionBase.Type` is non-virtual and `[JsonIgnore]`, and is backed by
    `protected abstract GetActionType()`.
- **Group children.** Children live on `ButtonItem.Children`, not on `GroupAction`.
  `ButtonItem.OnActionChanged` creates the children collection when an action becomes a group.
- **Hand-edit-friendly options** (`ConfigSerializer.Options`):
  - camelCase names, string enums, indented output, nulls omitted
  - comments and trailing commas allowed, case-insensitive properties, unknown members skipped
  - `AllowOutOfOrderMetadataProperties = true`, so `"type"` can appear anywhere in an action object.
    This option comes from the `System.Text.Json` 9.0.0 package, referenced on net8.
- **`ConfigSerializer.Deserialize`:**
  1. Parses to a `JsonNode`.
  2. Rejects a future `schemaVersion`.
  3. Runs `ConfigMigrations.Migrate` on the raw JSON.
  4. Binds the JSON to the model.
  5. Calls `Normalize`, which fixes null collections and duplicate or empty IDs and clamps values.

  Every failure is thrown as `ConfigException`.
- **Schema migrations.** Bump `IslandConfig.CurrentSchemaVersion` and add a step in
  `ConfigMigrations`.

### Config files (`%APPDATA%\Island\`)

| File / folder | Purpose |
|---|---|
| `config.json` | Buttons and settings |
| `config.backup.json` | Previous good config |
| `config.broken-<timestamp>.json` | Preserved copy of a config that failed to load |
| `state.json` | Position and visibility |
| `runlog.json` | Last 50 script runs |
| `scripts\<buttonId:N><ext>` | Inline scripts |
| `icons\` | Imported images |
| `error.log` | Unexpected errors |

`ConfigStore` handles loading and saving:

- **Save.** Copies the current `config.json` to the backup, but only if it parses: a broken file
  must never replace a good backup. Then it writes atomically (temp file + move) and records
  `LastWrittenText`.
- **Load failure.** Copies the broken file to `config.broken-*.json`, then loads the backup, or the
  defaults. It never silently overwrites the broken `config.json`. `App` shows a MessageBox
  explaining what happened.
- **First run.** Writes the default config. The defaults are: Fav apps (Explorer, Notepad,
  Calculator), Claude Code here, Screenshot (Snip), Browser, Settings.

`ConfigService` (WPF):

- Watches `config.json` with a `FileSystemWatcher`, debounced to 400 ms.
- Ignores its own writes by comparing the file text with `LastWrittenText`.
- Raises `ConfigChanged` after a successful reload, or `ReloadFailed`, in which case the old config
  stays active.
- On save, writes inline scripts to disk.

**Position is in `state.json`, not `config.json`.** This was a deliberate deviation from the spec:
dragging the island never touches the config or its backup.

### Coordinates, DPI, windows

- **Physical pixels everywhere.** All positioning uses physical pixels and Win32 (`SetWindowPos`,
  `GetWindowRect`). WPF `Left`/`Top` in DIPs are unreliable across monitors with different DPI.
  The process is PerMonitorV2 via `app.manifest`.
  - `WFAC010` is suppressed. The WinForms analyzer complains about DPI settings in the manifest,
    which is intended here.
- **Floating windows.** Island windows (island, panel, toasts) call
  `WindowHelper.MakeFloating` in `SourceInitialized`:
  - extended styles `WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE`, with `WS_EX_APPWINDOW`
    removed
  - a hook that returns `MA_NOACTIVATE` for `WM_MOUSEACTIVATE`

  **The island must never take focus.** `ClaudeCodeHere` and active-window screenshots depend on
  knowing the previously active window.
- **Shadow padding.** The island window is `IslandSize + 2*ShadowPad` (10 DIP) so the drop shadow
  isn't clipped. Snap math takes an `inset` (the padding, in physical px) so margins are measured
  from the visible pill. The panel (`ShadowPad` 12) and toasts (12) work the same way.
- **Drag vs click.** Implemented by hand in `IslandWindow`, not with `DragMove`:
  - mouse down: capture the mouse and record the cursor position from `GetCursorPos`
  - move further than 5 DIP: it's a drag; `DragStarted` collapses the panel
  - release after a drag: `SnapCalculator.ResolveDrop`, then a 150 ms cubic ease-out animation
    driven by a `DispatcherTimer` and `SetWindowPos`
  - release without moving: it's a click
  - after the animation, `RestorePosition()` re-settles in case the DPI (and so the window size)
    changed mid-move
- **Saved vs effective corner.** `RestorePosition()` positions from the saved state but does **not**
  overwrite the saved monitor or corner. If the saved monitor is missing, the island falls back to
  the primary monitor's bottom-right and returns when the monitor reappears. `_effectiveCorner`
  tracks where it actually is. State is only updated after a drag.
- **Display changes and topmost.** `WM_DISPLAYCHANGE`, `WM_SETTINGCHANGE/SPI_SETWORKAREA` and
  `SystemEvents.DisplaySettingsChanged` trigger a debounced restore. A 3 s timer re-asserts topmost.
- **Panel placement.** `PanelPlacement.Compute`:
  - Row layout opens sideways (left for right corners) and aligns to the island's vertical edge.
  - Column and Grid layouts open vertically (up for bottom corners).
  - The result is clamped to the work area.

  `PanelViewModel` keeps the first button nearest the island: tiles are reversed for Row/right and
  Column/bottom. Opened group levels stack away from the island, and
  `LevelHorizontalAlignment`/`LevelVerticalAlignment` keep sub-panels on the island's side.
- **Hover labels.** Tooltips in the panel are a **caption strip** (`PanelViewModel.Caption`), not
  WPF ToolTips. WPF tooltips may not show in never-activated windows. The caption's `Width` is bound
  to the levels host's `ActualWidth`, and the host is `HorizontalAlignment="Left"` with padding
  rather than margin. Changing that can create a layout feedback loop that keeps growing.
- **Collapse triggers** while the panel is open (`IslandController.InstallCollapseTriggers`):
  - Esc, via a temporary global `RegisterHotKey`. The island has no keyboard focus, so it can't
    listen for keys any other way.
  - Clicks outside, via a `WH_MOUSE_LL` hook that checks whether the clicked root window belongs to
    our process. The delegate is kept in a field.

  The panel also collapses after an action runs, if that setting is on, and on another island
  click. Both triggers are removed when the panel collapses.

### Actions

- **Handlers.** `ActionRunner` maps action types to `IActionHandler`. Subclass
  `ActionHandler<T>` for a typed handler.
  - `RunAsync` never throws. `ActionException` means an expected user-facing error and becomes an
    error toast; any other exception becomes an error toast too.
  - It tracks running buttons, each with a `CancellationTokenSource`, and raises `RunningChanged`.
  - Clicking a running tile cancels it. The island shows a busy ring while anything runs.
- **`AppServices`** is the service bag passed to handlers. `IIslandHost` is the UI surface handlers
  can call.
- **Adding an action type:**
  1. Add the class, a `[JsonDerivedType]` attribute, an `ActionType` enum value and
     `ActionFactory` cases (create, display name, default glyph) in `Island.Core/Models/Actions.cs`.
  2. Add a handler in `Services/Handlers/` and register it in the `ActionRunner` constructor.
  3. Add a `DataTemplate` with `DataType` set to the new class in `SettingsWindow.xaml`.
  4. Add validation in `ButtonValidator`, and tests.
- **Foreground tracking.** `ForegroundTracker` uses `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`,
  out-of-context, skipping our own process. It ignores our own windows and remembers
  `LastExternalWindow`.
- **Current folder.** `ExplorerFolderResolver` works on a **dedicated STA thread with a 3 s
  timeout** so a hung Explorer can't freeze the UI:
  1. Uses `Shell.Application.Windows()` through `dynamic` COM and matches each window's `HWND`.
  2. On Windows 11, tabs share one HWND, so it picks the active tab: it finds the
     `ShellTabWindowClass` child and compares it with `IServiceProvider → IShellBrowser.GetWindow`.
  3. Converts `LocationURL` to a path with `ExplorerPath.FromLocationUrl`.
  4. Falls back to `Document.Folder.Self.Path`. Special folders return null.

  `CurrentFolderService` adds the desktop (Progman/WorkerW, which maps to the Desktop folder) and
  the configured fallback folder (default `%USERPROFILE%`).
- **ClaudeCodeHere** launches `wt.exe -d "<folder>" <command>` via `TerminalCommandBuilder`:
  - semicolons are escaped for `wt`
  - if the command's first token resolves on PATH to a `.cmd`/`.bat` shim (npm installs `claude`
    that way), it's wrapped in `cmd /k`, because `wt` can't start `.cmd` files directly
  - `wt.exe` is looked up on PATH and in `%LOCALAPPDATA%\Microsoft\WindowsApps`
  - without `wt`, it falls back to
    `powershell.exe -NoExit -Command "Set-Location -LiteralPath '<folder>'; <command>"`
    (or `pwsh` if preferred and installed)
- **Screenshot modes:**
  - **Snip** opens `ms-screenclip:`.
  - **FullScreen** captures the virtual screen with GDI `CopyFromScreen`.
  - **ActiveWindow** captures `LastExternalWindow`, using DWM extended frame bounds.

  `IIslandHost.RunHiddenAsync` sets the island to opacity 0 for about 180 ms while capturing. The
  file is saved as `Screenshot yyyy-MM-dd HH-mm-ss.png`, optionally copied to the clipboard, and a
  toast offers Open and Show in folder.
- **Shell helpers.** `ShellLauncher.Open` uses `UseShellExecute=true`. `runas` gives elevation, and
  a declined UAC prompt (Win32 error 1223) is ignored. With no URL set, the browser's home page is
  opened by launching the default browser exe from the https/http `UserChoice` ProgId.

### RunScript

- **Source.** A file, or an inline script. Inline scripts are written to
  `scripts\<id:N><ext>` on save *and* before every run (`ScriptFiles.EnsureInlineFile`). `.ps1`
  files get a UTF-8 BOM so PowerShell 5 reads them correctly.
- **Interpreters** (`InterpreterResolver`):

  | Extension | Command |
  |---|---|
  | `.ps1` | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File "{script}"` (`pwsh.exe` if preferred and installed) |
  | `.py` / `.pyw` | `GeneralSettings.PythonCommand`, e.g. `py -3` |
  | `.js` / `.mjs` / `.cjs` | `NodeCommand` |
  | `.bat` / `.cmd` | `cmd.exe /d /s /c ""{script}" args"` |
  | `.exe` | runs directly |
  | Custom | executable plus a template with `{script}` and `{args}` |

- **Placeholders** (`PlaceholderExpander`): `{currentFolder}`, `{clipboard}`, `{date}`
  (yyyy-MM-dd), `{time}` (HH-mm-ss) and `{islandDir}`. They're case-insensitive and lazy. `{{` and
  `}}` escape braces, and unknown names are left as they are. They apply to Arguments, Working
  directory and custom templates.
- **Environment variables.** Scripts also get `ISLAND_CURRENT_FOLDER`, `ISLAND_CLIPBOARD`
  (capped at 30,000 characters), `ISLAND_DIR`, `ISLAND_BUTTON`, `PYTHONIOENCODING=utf-8` and
  `PYTHONUTF8=1`. The environment variables are safer than `{clipboard}` arguments, because quotes
  and newlines survive. The current folder is resolved off the UI thread; the clipboard is read on
  the UI thread.
- **`ProcessRunner`** (Core):
  - async, captures stdout and stderr as UTF-8, up to 1M characters each
  - a timeout (default 60 s, 0 = none) or cancellation kills the whole process tree
  - outcomes: `Completed`, `TimedOut`, `Cancelled`, `FailedToStart`
  - `ShowConsole` runs the script without redirection, so its output isn't captured
- **Results:**
  - A non-zero exit shows an error toast with the exit code and the first stderr line, plus
    **View details**.
  - The last 50 runs are kept in `RunLog`, with output capped at 64 KB per stream.
- **Output handling:**

  | Mode | Behavior |
  |---|---|
  | `Ignore` | Nothing |
  | `Toast` | Shows the last non-empty line |
  | `CopyToClipboard` | Copies all of stdout |
  | `ShowInPopup` | Shows stdout in a scrollable popup |
  | `JsonProtocol` | Parses the last non-empty line as a JSON object; if that fails, falls back to `Toast` |

- **JSON protocol fields** (all optional, case-insensitive, unknown fields ignored): `notify`,
  `clipboard`, `openUrl`, `openPath`, `popup`, `error`.
- **Imports.** Importing a config with scripts lists them and asks Yes, No or Cancel. Yes enables
  the scripts. No imports them with `RunScriptAction.Disabled = true`, and disabled scripts refuse
  to run.

### UI: themes and styles

- **Palette.** `ThemeService` builds the palette in code: dark or light (System follows the
  `AppsUseLightTheme` registry value) and the accent (setting, else the DWM `AccentColor`, else
  `#0078D4`). It also overrides `SystemColors` keys for stock controls.
  - **The palette dictionary is *appended* to `Application.Resources.MergedDictionaries`.** Later
    merged dictionaries win; inserting it at index 0 let the fallbacks in `Controls.xaml` override
    the theme. This was a real bug.
- **Brush keys.** Each brush key `XBrush` also has a matching `XBrushColor` color resource, used by
  `DropShadowEffect`:

  | Area | Keys |
  |---|---|
  | Accent | `AccentBrush`, `AccentForegroundBrush`, `AccentSoftBrush` |
  | Island | `IslandBackgroundBrush`, `IslandBorderBrush`, `IslandForegroundBrush` |
  | Panel tiles | `PanelBackgroundBrush`, `TileBackgroundBrush`, `TileHoverBrush`, `TilePressedBrush` |
  | Window surfaces | `WindowBackgroundBrush`, `SurfaceBrush` |
  | Controls | `ControlBackgroundBrush`, `ControlHoverBrush`, `ControlBorderBrush` |
  | Text | `ForegroundBrush`, `SubtleForegroundBrush`, `DisabledForegroundBrush` |
  | Selection, toasts, status | `SelectionBrush`, `ToastBackgroundBrush`, `ErrorBrush`, `WarningBrush`, `SuccessBrush` |
  | Shadow | `ShadowColorBrush` (`ShadowColorBrushColor`) |

  Always use `DynamicResource` for these so theme switches apply live.
- **`Themes/Controls.xaml`** holds custom templates for Button and ToggleButton, TextBox, CheckBox,
  RadioButton (segmented look), ComboBox and ComboBoxItem, TabControl and TabItem, ScrollBar (thin)
  and Slider, plus styles for lists, menus and ToolTip. Named styles: `BaseButtonStyle`,
  `AccentButtonStyle`, `IconButtonStyle`, `LinkButtonStyle`, `LabelText`, `HintText`,
  `SectionHeader`, `CodeTextBox`. Fonts: `IconFont` (Segoe Fluent Icons, falling back to Segoe MDL2
  Assets) and `MonoFont` (Cascadia Mono, Consolas).
- **Title bars.** Settings, run log, popup and icon picker windows get a dark title bar via
  `DWMWA_USE_IMMERSIVE_DARK_MODE` (`WindowHelper.SetDarkTitleBar`).
- **Icons.** `IconView` renders an `IconSpec`:
  - **Glyph:** a hex code point in `IconFont`.
  - **AppIcon:** `IconService.GetFileIcon`, using the 48 px system image list (`SHGetImageList` /
    `IImageList`), falling back to `SHGetFileInfo` and then `ExtractAssociatedIcon`. Results are
    cached.
  - **Image:** a file loaded with `OnLoad` caching so it isn't locked. With a tint, the tint is
    painted through the image's alpha.
  - **Emoji:** Segoe UI Emoji. WPF renders it monochrome.

  The view re-renders when the spec's properties change.
- **Glyph catalog.** `Assets/glyphs.json` is an embedded resource (`Island.Assets.glyphs.json`)
  with 145 curated entries of the form `{ code, name, tags }`. Only add code points you're sure
  of. Any code point can also be typed into the picker.
- **Toasts.** `ToastWindow` instances are non-activating and stack away from the island's screen
  edge (`ToastService.Relayout`, physical pixels, DPI-scaled padding). At most 4 are shown, and
  hovering pauses the auto-dismiss timer. These are custom windows, not Windows notifications, so no
  AUMID or package identity is needed.

### Settings window (`SettingsViewModel`)

- **Working copy.** Edits apply to a deep clone of the live config (`Working`); nothing reaches
  the island until Save. The window binds directly to the observable models.
- **Refresh timer.** A 400 ms timer recomputes `IsDirty`, by comparing the serialized JSON with a
  baseline, and validation, setting `ButtonItem.Warning` and `SelectedErrors`. Invalid buttons
  still save and show a ⚠ badge.
- **Buttons and shortcuts:**
  - **Save** (Ctrl+S) saves and keeps the window open.
  - **Cancel** discards changes and closes.
  - Closing with unsaved changes asks Yes, No or Cancel.
  - **Reset to defaults** and **Import** only replace the working copy; nothing is written until
    Save.
- **Tree.** The tree is a `TreeView` with a `HierarchicalDataTemplate` over `Children`.
  `IsSelected` and `IsExpanded` are bound two-way to the models.
  - **Drag & drop** is custom: a `DropIndicatorAdorner` draws a line for before/after and a frame
    for into-group. Zones are measured against `PART_Header`. Moving a group into itself is
    rejected (`ButtonTree.Move`).
  - **Explorer file drops** go through `DropFactory`.
  - **Keys:** Alt+↑/↓ move, Del deletes, Ctrl+D duplicates, F2 renames.
  - **Delete** shows an undo snackbar for 8 s.
- **Changing action type.** Moving a group to another type moves its children out, after
  confirmation. A `LaunchApp`, `OpenFile` or `OpenFolder` path auto-fills the app icon and an empty
  label.
- **Hotkey capture.** Uses `PreviewKeyDown` and checks for conflicts with
  `HotkeyService.IsAvailable`. The current hotkey counts as available, since we hold it ourselves.
  The settings hotkey is the main hotkey + Shift; there's none if the main hotkey already has
  Shift.
- **Run log refresh.** `RunLogView` must (re)subscribe in `Loaded`: `TabControl` unloads tab
  content when you switch tabs. This was a real bug.

### App startup (`App.xaml.cs`)

1. Takes a named per-user `Mutex`. A second instance signals a named `EventWaitHandle`, so the
   first one calls `Flash()` (a pulse animation), and then exits.
2. Sets up services: `ConfigService.Load`, `ThemeService.Apply`, `ToastService`,
   `ForegroundTracker`, `HotkeyService` (a message-only `HwndSource`), `AppServices`,
   `ActionRunner`, `IslandController.Start`.
3. Creates the tray icon with WinForms `NotifyIcon` (allowed by the spec). Menu: Hide/Show island,
   Reset position, Settings…, Script run log…, Exit. Double-click opens Settings.
4. Handles unhandled dispatcher exceptions by logging to `error.log` and showing a toast; the app
   doesn't crash.
5. Applies "Start with Windows" (`StartupService`): an `Island.lnk` in the Startup folder, created
   via `WScript.Shell`.

`ShutdownMode=OnExplicitShutdown`.

---

## Conventions

- C# `latest`, nullable enabled, **implicit usings disabled**. The app project's global usings are
  `System`, `System.Collections.Generic`, `System.IO`, `System.Linq`, `System.Threading` and
  `System.Threading.Tasks`.
- Because both WPF and WinForms are referenced, names like `Application`, `Clipboard`,
  `MessageBox`, `Path` and `IServiceProvider` can be ambiguous. Fully qualify them or alias them
  (`using WinForms = System.Windows.Forms;`).
- Each window uses its own physical-pixel geometry (`WindowHelper.GetRect`/`MoveTo`/`GetScale`).
  Don't mix that with WPF DIP `Left`/`Top` for placement.
- Keep the UI thread free: process work goes through `ProcessRunner` (async), and Explorer COM runs
  on its own STA thread. Clipboard access must happen on the UI (STA) thread, through
  `ClipboardHelper` (which retries).
- Expected failures inside handlers: throw `ActionException(message)`.
- Write tests in `tests/Island.Tests` for any new Core logic. Process tests run `/bin/sh` on Linux
  and `cmd.exe` on Windows.
- Commits: don't put model identifiers in commit messages or code. The cloud-session workflow
  develops on a `ccr-*` branch and pushes there.

---

## Deviations from the original spec (intentional)

- **Extra project.** `Island.Core` is a separate platform-neutral project, so the tests run on any
  OS.
- **Position storage.** Position is stored in `state.json`, not `config.json`.
- **Hover labels.** They appear in a caption strip in the panel, not as WPF ToolTips.
- **Glyph list.** It has about 145 entries, not ~200: only confident code points are included, and
  a custom code point entry is available.
- **Emoji icons are monochrome** (a WPF limitation).
- **Settings buttons.** Save keeps the window open; Cancel discards changes and closes.
- **Extra script inputs.** Scripts also get `ISLAND_*` environment variables.
- **`.cmd` shims.** `ClaudeCodeHere` wraps `.cmd`/`.bat` shims in `cmd /k`.
- **Esc while the menu is open.** Esc is taken globally via `RegisterHotKey`, because the island
  never has focus.
- **Toasts and tray.** Toasts are Island's own windows, and the tray uses WinForms `NotifyIcon`.

## Known limitations / ideas

- **Not verified on Windows.** At the time of writing the app had only been compiled and
  unit-tested, never run on Windows. First runs may surface runtime issues: binding errors,
  resource lookups, layout, COM behavior.
- **Settings vs hand edits.** Hand-editing `config.json` while Settings is open: saving from
  Settings overwrites the hand edits.
- **No settings shortcut with Shift.** If the main hotkey includes Shift, there's no separate
  settings shortcut.
- **Uneven grid rows.** In a Grid layout, a `UniformGrid` with fewer items than columns leaves
  empty slots.
- **Column layout alignment.** Tiles inside a level are top-aligned, which may look off for bottom
  corners.
