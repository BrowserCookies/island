# Island

A small floating productivity widget for Windows 10 and 11. Island is a circle that stays on top of
everything. You can drag it anywhere and it snaps to the nearest screen corner. Click it and it opens
into a menu of buttons that launch apps, open folders and URLs, take screenshots, start Claude Code
in the folder you're working in, or run your own scripts in any language.

Built with .NET 8, WPF and C# (MVVM via CommunityToolkit.Mvvm). It has no web stack and no cloud services.

---

## Contents

- [Getting started](#getting-started)
- [Using the island](#using-the-island)
- [Search bar](#search-bar)
- [Built-in actions](#built-in-actions)
- [Running your own scripts](#running-your-own-scripts)
  - [JSON protocol](#json-protocol)
  - [Example scripts](#example-scripts)
- [Settings](#settings)
- [Config file](#config-file)
- [Project layout](#project-layout)
- [Development](#development)
- [Known limitations](#known-limitations)

---

## Getting started

### Install

Run `Island-Setup-1.0.0.exe`. It installs for the current user only, so there's no admin prompt:

- The app goes into `%LOCALAPPDATA%\Programs\Island`.
- It adds an **Island** Start Menu shortcut, plus an optional desktop shortcut.
- It adds an entry in **Settings → Apps** for uninstalling.

The installer bundles the .NET runtime, so nothing else needs to be installed. It isn't code-signed,
so Windows SmartScreen may say "Windows protected your PC". Click **More info → Run anyway**.

Use **Settings → General → Start Island with Windows** to start it at sign-in. Uninstalling removes
the app and that startup shortcut but keeps your buttons and settings in `%APPDATA%\Island`. Running
a newer installer upgrades in place; it closes a running Island first.

### Run from source

Requirements: Windows 10 (1809+) or 11 and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
git clone <this repo>
cd island
dotnet run --project src/Island
```

### Build the `.exe` and the installer

```powershell
powershell -ExecutionPolicy Bypass -File installer\build.ps1
```

This produces two files:

- `publish/win-x64/Island.exe`: a self-contained single-file executable (about 75 MB) that needs no
  .NET install. You can also copy it anywhere and run it as is.
- `publish/installer/Island-Setup-<version>.exe`: the installer described above.

The version comes from `<Version>` in `src/Island/Island.csproj`; pass `-Version 1.2.3` to override
it. The installer step needs [Inno Setup 6](https://jrsoftware.org/isinfo.php)
(`winget install JRSoftware.InnoSetup`). To build only the exe:

```powershell
dotnet publish src/Island/Island.csproj -p:PublishProfile=SingleFile
```

Only one Island runs at a time. Launching it again makes the existing island pulse so you can find it.

---

## Using the island

| Do this | What happens |
|---|---|
| **Click** the island | Opens the menu. Click again to close it. |
| **Drag** the island (move more than ~5 px) | Moves it. On release it glides (150 ms) to the nearest corner of that monitor's work area, so it never sits on the taskbar. |
| **Right-click** the island | Settings, script run log, reset position, hide, exit. |
| **Esc**, clicking anywhere else, or running an action | Closes the menu. Closing after an action can be turned off. |
| **Ctrl+Alt+Space** (default) | Opens and closes the menu, or shows and hides the island (configurable). |
| **Ctrl+Alt+Shift+Space** | Opens Settings. |
| **Win+Shift+F** (default) | Opens the [search bar](#search-bar). |
| Tray icon | Show/Hide island, Reset position, Settings, Script run log, Exit. Double-click opens Settings. |

- The menu always opens **away from the corner** the island is in. For example, from the bottom-right
  corner it opens upward and to the left, so it never goes off-screen.
- **Groups** (like *Fav apps*) open a sub-panel next to the main row. Click the group again to close it.
- Hover over a button to see its label or tooltip in the caption strip at the edge of the menu.
- A button whose action is still running (for example a script) shows a spinner, and the island shows
  a progress ring. **Click a running button to cancel it.**
- Island **never takes focus** when you click it. The app you were using stays active, which is how
  *Claude Code here* and *Active window* screenshots know which window you mean.
- It isn't shown in the taskbar or Alt+Tab, and it stays on top even after display changes.
- Island remembers its position as *monitor + corner*. If that monitor is disconnected, Island moves to
  the primary monitor's bottom-right corner and returns when the monitor comes back. With snapping
  off, it stays exactly where you drop it (kept on-screen).
- Island is per-monitor DPI aware, so it works across monitors with different scaling.

### Default buttons

1. **Fav apps** (group): File Explorer, Notepad, Calculator
2. **Claude Code here**
3. **Screenshot** (Snip)
4. **Browser** (opens your default browser's home page)
5. **Settings**

---

## Search bar

Press **Win+Shift+F** (change it under **Settings → General**) and a pill-shaped search bar opens in
the middle of the screen you're on, on top of every app, ready for typing. Results update as you type.

| Type | You get |
|---|---|
| An app name (`calc`, `vsc`, `ntpd`) | Installed apps, including Store apps. Prefixes, word starts and initials all match. |
| A button's name | Your Island buttons and groups (a group opens the island with it expanded). |
| `settings`, `screenshot`, `hide` | Island commands: Settings, Show/Hide island, Script run log, Screenshot (snip or full screen). |
| A file or folder name | Matches from the Windows Search index. |
| A sum (`12*7+3`, `2^10`, `15%*80`, `3,5×2`) | `= 87` as the top result; Enter copies it. |
| A song (when Spotify is connected) | Tracks; Enter plays the track in Spotify. |

| Key | Does |
|---|---|
| ↑ / ↓, PgUp / PgDn | Move through results |
| Enter | Run the selected result |
| Ctrl+Enter or Shift+Enter | Show a file or folder in Explorer |
| Esc, the shortcut again, or clicking elsewhere | Close |

Things you pick often move up for the same query (stored in `search-history.json`). Files come from
the index Windows already keeps, so Island holds no file list in memory: only folders Windows indexes
are found (by default your user folders; change it in Windows' *Indexing Options*). Build and cache
folders like `node_modules`, `.git`, `obj` and `AppData` are left out.

### Spotify

Under **Settings → Services → Spotify**, follow the five setup steps (create a free Spotify developer
app, add the redirect URI `http://127.0.0.1:43819/callback`, tick *Web API*, paste the Client ID) and
click **Sign in**. Signing in happens on Spotify's own page in your browser. Requirements and notes:

- Spotify only lets apps start playback for **Premium** accounts, and its developer mode needs the
  app owner to have Premium.
- If nothing is playing anywhere, Island starts the Spotify app and plays on it.
- The sign-in is stored encrypted for your Windows account in `%APPDATA%\Island\secrets\spotify.bin`,
  never in `config.json`, so exporting your settings doesn't share it.

---

## Built-in actions

| Action | Options | Behavior |
|---|---|---|
| **Launch app** | path, arguments, working dir, run as admin | Starts an `.exe`, `.lnk` or any file through the shell. Bare names such as `notepad` or `code` are found on PATH. `%ENV%` variables work. Store apps work as `shell:AppsFolder\<app id>`. |
| **Open folder** | path | Opens the folder in Explorer. |
| **Open file** | path | Opens the file with its default app. |
| **Open URL** | URL (optional) | Opens the URL in your default browser. If empty, opens the browser's home page. `mailto:`, `ms-settings:` and other schemes also work. |
| **Group** | child buttons | Opens a sub-panel. Groups can be nested. |
| **Claude Code here** | command, fallback folder | See below. |
| **Screenshot** | mode, save folder, copy to clipboard | **Snip** opens the Windows snipping overlay (`ms-screenclip:`). **Full screen** captures every monitor. **Active window** captures the window you were in before clicking Island. Captures are saved as `Screenshot yyyy-MM-dd HH-mm-ss.png`, optionally copied to the clipboard, and confirmed with a toast. Island hides itself while capturing. |
| **Open settings** | none | Opens the settings window. |
| **Run script** | see [below](#running-your-own-scripts) | Runs your own script. |

### Claude Code here

1. Island keeps track of the last window you used that isn't Island itself.
2. If that window was **File Explorer**, Island asks Explorer which folder it shows, including the
   active tab on Windows 11. If it was the **desktop**, Island uses your Desktop folder.
3. Otherwise, or for special locations like *This PC*, Island uses the **fallback folder** (default: your
   user profile).
4. Island opens **Windows Terminal** with `wt.exe -d "<folder>" <command>`. If Windows Terminal
   isn't installed, it runs
   `powershell.exe -NoExit -Command "Set-Location '<folder>'; <command>"` instead.

The command defaults to `claude` and can be set per button. You can add variants such as
`claude --continue` or `claude --model opus` as separate buttons. If the command is an npm-style
`.cmd` shim, Island runs it through `cmd /k` because Windows Terminal can't start `.cmd` files directly.
Semicolons in the command are escaped for `wt`.

---

## Running your own scripts

A **Run script** button runs a script you write yourself, either a file on disk or an inline script you
type in the editor. Inline scripts are saved as `%APPDATA%\Island\scripts\<button id><ext>`.

### Interpreters

The interpreter is picked from the file extension, and you can override it with a custom executable
and argument template:

| Extension | Runs as |
|---|---|
| `.ps1` | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File "{script}"`. Uses `pwsh.exe` instead if you choose PowerShell 7 in Settings and it's installed. |
| `.py` | `python "{script}"`. The command is configurable, for example `py -3`. |
| `.js` / `.mjs` | `node "{script}"` |
| `.bat` / `.cmd` | `cmd.exe /d /s /c ""{script}" args"` |
| `.exe` | runs directly |
| anything else | set a **custom interpreter**, e.g. `ruby.exe` with template `"{script}" {args}` |

### Arguments and placeholders

These placeholders are replaced in **Arguments**, **Working directory** and custom argument templates:

| Placeholder | Value |
|---|---|
| `{currentFolder}` | The folder of the Explorer window you were using (same logic as *Claude Code here*) |
| `{clipboard}` | Clipboard text |
| `{date}` | `yyyy-MM-dd` |
| `{time}` | `HH-mm-ss` (safe in file names) |
| `{islandDir}` | `%APPDATA%\Island` |

Write `{{` and `}}` for literal braces. Quote placeholders that may contain spaces, for example `"{currentFolder}"`.

Scripts also get these **environment variables**:
`ISLAND_CURRENT_FOLDER`, `ISLAND_CLIPBOARD` (capped at 30,000 characters), `ISLAND_DIR` and
`ISLAND_BUTTON`. For arbitrary clipboard text, `ISLAND_CLIPBOARD` is more robust than an argument,
because quotes and newlines arrive intact. Island also sets `PYTHONIOENCODING=utf-8` for Python scripts.

### Execution

- Scripts always run **in the background**. The island never freezes.
- stdout and stderr are both captured as UTF-8. In PowerShell, set
  `[Console]::OutputEncoding = [Text.Encoding]::UTF8` if you print non-ASCII text.
- **Timeout**: 60 s by default (0 = none). When the timeout runs out, the script and any child
  processes are killed. Click the spinning button to cancel a run early.
- A **non-zero exit code** shows an error toast with the exit code and the first line of stderr.
  **View details** in the toast shows the full output.
- **Show console window** runs the script in a visible console. Output then goes to that console and
  isn't captured.
- The **last 50 runs** (time, button, exit code, duration, output) are kept in the **Script runs** tab
  in Settings, which you can also open from the tray menu.
- Scripts run with **your user permissions**. When you import a config that contains scripts, Island
  lists them and asks whether to enable them. If you don't, they're imported *disabled*.

### Output handling

| Mode | What Island does with stdout |
|---|---|
| `Ignore` | Nothing. |
| `Toast` | Shows the last non-empty line as a toast. |
| `CopyToClipboard` | Copies all of stdout to the clipboard. |
| `ShowInPopup` | Shows stdout in a scrollable text window. |
| `JsonProtocol` | Reads the last line as JSON. See below. |

### JSON protocol

With `JsonProtocol`, Island parses the **last non-empty line** of stdout as a JSON object. Earlier
lines are just log output and appear in the run log. Island acts on any of these optional fields:

```json
{
  "notify": "Text for a toast",
  "clipboard": "Text to copy",
  "openUrl": "https://...",
  "openPath": "C:\\some\\file-or-folder",
  "popup": "Longer text to show in the popup",
  "error": "Error message to show as an error toast"
}
```

Unknown fields are ignored, and field names are case-insensitive. If the last line isn't a JSON
object, Island falls back to `Toast` and shows that line.

### Example scripts

These three examples are also in [`samples/scripts`](samples/scripts). To use one, create a **Run
script** button, choose **Script file**, and set **Output handling** to **JsonProtocol**. You can
also drag the file from Explorer onto the button list in Settings.

**PowerShell**: summarize the folder open in Explorer (Arguments: `"{currentFolder}"`)

```powershell
param([string]$Folder = (Get-Location).Path)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$files  = Get-ChildItem -LiteralPath $Folder -File -Recurse -ErrorAction SilentlyContinue
$sizeMb = [math]::Round((($files | Measure-Object Length -Sum).Sum) / 1MB, 1)

Write-Output "Scanned $($files.Count) files"          # log line, ignored
@{
    notify    = "$($files.Count) files, $sizeMb MB in $(Split-Path $Folder -Leaf)"
    clipboard = $Folder
} | ConvertTo-Json -Compress                            # last line = the reply
```

**Python**: count words in the clipboard and copy a cleaned-up version back

```python
import json, os, re

text = os.environ.get("ISLAND_CLIPBOARD", "")
if not text.strip():
    print(json.dumps({"error": "The clipboard is empty."}))
else:
    words = re.findall(r"\b\w+\b", text)
    print(json.dumps({
        "notify": f"{len(words)} words, {len(text)} characters",
        "clipboard": re.sub(r"[ \t]+", " ", text).strip(),
    }))
```

**Node.js**: copy a UTC timestamp, and optionally open a URL passed as an argument

```js
const stamp = new Date().toISOString().replace("T", " ").slice(0, 19) + "Z";
const reply = { notify: `Copied ${stamp}`, clipboard: stamp };
if (process.argv[2]) reply.openUrl = process.argv[2];
console.log(JSON.stringify(reply));
```

---

## Settings

Open Settings from the island's **Settings** button, the tray icon, the right-click menu, or the hotkey
with **Shift**.

### Buttons tab

- **Left:** all buttons in island order. Groups are expandable.
  - **Drag** to reorder, including into and out of groups. A line shows where the button will go, and a
    frame means *into this group*.
  - **Add ▾** creates a button of the chosen type below the selection.
  - **Duplicate** (Ctrl+D) and **Delete** (Del). Delete shows an **Undo** bar instead of asking for
    confirmation.
  - **Move up / down** (Alt+↑ / Alt+↓). Moving past the top or bottom of a group moves the button out
    of the group.
  - **Drop files from Explorer:** `.exe` and `.lnk` become *Launch app* buttons with the app's icon and
    name, folders become *Open folder*, `.ps1` `.py` `.js` `.bat` and `.cmd` become *Run script*,
    `.url` becomes *Open URL*, and any other file becomes *Open file*.
- **Right:** the editor for the selected button. It has the label, tooltip, icon, icon tint and
  background color, the action type and that action's options. Paths have *Browse…* buttons, and
  inline scripts have a monospace editor.
  - **Validation** runs as you type. It catches missing paths, files that don't exist, invalid URLs
    and empty groups. Invalid buttons still save but show a ⚠ badge.
  - **Test** runs the action immediately, including unsaved edits.
- **Icon picker:**
  - *Glyphs*: a searchable set of about 145 Segoe Fluent Icons. Windows 10 uses Segoe MDL2 Assets.
    You can also type any code point.
  - *From app*: the icon of an exe, shortcut or file. Filled in automatically for Launch app buttons.
  - *Image file*: a PNG or ICO, copied to `%APPDATA%\Island\icons`.
  - *Emoji*.
- **Preview:** shows the menu as it will look with your unsaved changes.

### General tab

Settings for start with Windows, the global hotkey (with conflict detection) and what the hotkey
does, the search shortcut, snapping and snap margin, island size, idle opacity, hover effect, menu layout (row, column,
grid with N columns), labels under icons, collapse after action, theme (System, Dark or Light) and
accent color. It also sets defaults: Claude command, fallback folder, screenshot folder, Windows
PowerShell or PowerShell 7, and the Python and Node commands.

### Services tab

Connections to outside services. Currently Spotify (see [Search bar → Spotify](#spotify)).

### Saving

**Save** (Ctrl+S) applies everything to the island immediately, without a restart. **Cancel**
discards changes and closes the window. If you close the window with unsaved changes, Island asks
whether to save them. **Import… / Export…** read and write the whole config as `.json`.
**Reset to defaults** replaces the config, but nothing is written until you save.

---

## Config file

Everything is stored in `%APPDATA%\Island\`:

| File / folder | Purpose |
|---|---|
| `config.json` | Buttons and settings (has a `schemaVersion` for migrations) |
| `config.backup.json` | The previous good `config.json`, written on every save |
| `config.broken-*.json` | A copy of a config that failed to load (see below) |
| `state.json` | Island position (monitor + corner, or exact position) and visibility |
| `runlog.json` | The last 50 script runs |
| `scripts\` | Inline scripts (`<button id>.<ext>`) |
| `icons\` | Imported icon images |
| `search-history.json` | How often search results were picked (ranking only) |
| `secrets\spotify.bin` | Spotify sign-in, encrypted for your Windows account |
| `error.log` | Unexpected errors, if any |

- **Hand edits apply live.** The file is watched, and changes are picked up about 0.4 s after you
  save. If your edit doesn't parse, Island keeps the current config and shows an error toast.
- **Island never silently overwrites a broken config.** If `config.json` can't be read at startup,
  Island shows an error, keeps a copy as `config.broken-<timestamp>.json`, and starts from the backup
  or the defaults. `config.json` is only replaced when you save.
- JSON comments and trailing commas are allowed. Enums are written as strings.

Example (abridged):

```jsonc
{
  "schemaVersion": 1,
  "general": { "snapEnabled": true, "snapMargin": 16, "islandSize": 56, "layout": "Row",
               "theme": "System", "hotkey": "Ctrl+Alt+Space", "claudeCommand": "claude" },
  "buttons": [
    {
      "id": "6f0c…", "label": "Fav apps",
      "icon": { "kind": "Glyph", "value": "E734" },
      "action": { "type": "Group" },
      "children": [
        { "id": "…", "label": "Notepad", "icon": { "kind": "AppIcon", "value": "%WINDIR%\\System32\\notepad.exe" },
          "action": { "type": "LaunchApp", "path": "%WINDIR%\\System32\\notepad.exe" } }
      ]
    },
    { "id": "…", "label": "Continue in Claude", "icon": { "kind": "Glyph", "value": "E756" },
      "action": { "type": "ClaudeCodeHere", "command": "claude --continue" } },
    { "id": "…", "label": "Word count", "icon": { "kind": "Emoji", "value": "🔢" },
      "action": { "type": "RunScript", "source": "File", "scriptPath": "C:\\scripts\\word_count.py",
                  "output": "JsonProtocol", "timeoutSeconds": 30 } }
  ]
}
```

The action `type` values are `LaunchApp`, `OpenFolder`, `OpenFile`, `OpenUrl`, `Group`,
`ClaudeCodeHere`, `Screenshot`, `OpenSettings` and `RunScript`.

---

## Project layout

```
Island.sln
src/Island.Core/        Platform-neutral logic (net8.0), unit-tested on any OS
  Models/               Config model: IslandConfig, ButtonItem, IconSpec, actions ([JsonPolymorphic])
  Config/               ConfigSerializer (+migrations), ConfigStore (backup / broken-file handling),
                        DefaultConfig, ButtonValidator, ButtonTree (reorder / group moves)
  Layout/               SnapCalculator, PanelPlacement, easing (all in physical pixels)
  Search/               Calculator, SearchMatcher, SearchHistory, WindowsSearchQuery, SpotifyProtocol (PKCE, JSON)
  Scripts/              PlaceholderExpander, InterpreterResolver, ScriptOutputParser (JSON protocol),
                        ProcessRunner (timeout / cancel / kill tree), RunLog, CommandLine quoting
  Util/                 ExplorerPath (LocationURL → path), TerminalCommandBuilder, HotkeyGesture, DropFactory
src/Island/             WPF app (net8.0-windows)
  App.xaml(.cs)         Single instance, startup, tray icon
  IslandController.cs   Expand/collapse, Esc + click-outside, hotkeys, host for secondary windows
  Views/                IslandWindow, PanelWindow, SettingsWindow, IconPickerWindow, ToastWindow,
                        OutputPopupWindow, RunLogWindow/RunLogView
  ViewModels/           PanelViewModel, SettingsViewModel
  Search/               SearchWindow + SearchViewModel + SearchController, AppCatalog (Apps folder),
                        FileSearch (Windows Search via OLE DB), SpotifyService
  Services/             ConfigService (live reload), ForegroundTracker, ExplorerFolderResolver,
                        ActionRunner + Handlers/ (one IActionHandler per action type),
                        ScreenshotService, HotkeyService, IconService, ToastService,
                        ThemeService, StartupService, MonitorService
  Interop/              P/Invoke (user32, shell32, shcore, dwmapi, gdi32) and window helpers
  Themes/Controls.xaml  Theme-aware control styles (palette is generated by ThemeService)
  Assets/               App icon, curated glyph list
tests/Island.Tests/     xUnit tests for Island.Core
samples/scripts/        Example scripts (PowerShell, Python, Node)
installer/              Inno Setup script (Island.iss) and build.ps1 (publish + installer)
```

### Adding a new action type

1. Add a class deriving from `ActionBase` in `Island.Core/Models/Actions.cs`, and register it with a
   `[JsonDerivedType(typeof(MyAction), "MyAction")]` attribute plus an `ActionType` entry and a
   factory case.
2. Implement `ActionHandler<MyAction>` in `src/Island/Services/Handlers/` and register it in the
   `ActionRunner` constructor.
3. Add a `DataTemplate` for it in `SettingsWindow.xaml` to get an editor form.

---

## Development

```powershell
dotnet build Island.sln
dotnet test                  # runs the xUnit suite for Island.Core
dotnet run --project src/Island
```

The tests cover config serialization (polymorphism, hand-edited JSON, migrations, normalization),
the config store (backup, broken-file preservation), snap and panel placement math, placeholder
expansion, interpreter resolution, JSON protocol parsing, process timeout and cancellation, the run
log, hotkey parsing, tree moves, validation and Explorer drop handling, plus the search bar's
calculator, name matching and ranking history, Windows Search query building, and the Spotify
sign-in (PKCE) and response parsing.

`Island.Core` targets plain `net8.0`, so its tests run on Windows, Linux or macOS. The WPF project
sets `EnableWindowsTargeting`, so it also compiles on non-Windows CI. It only runs on Windows.

---

## Known limitations

- **Emoji icons are monochrome.** WPF can't render color fonts.
- **Shift is reserved for the settings shortcut.** If your hotkey already includes Shift, there is no
  separate settings shortcut.
- **Esc closes the open menu globally.** While the menu is open, Island registers Esc as a hotkey
  (the island never takes focus, so it can't listen for keys any other way). Esc reaches other apps
  again as soon as the menu closes.
- **Hand edits can be lost if Settings is open.** If you edit `config.json` by hand while Settings is
  open, saving from Settings overwrites those edits.
