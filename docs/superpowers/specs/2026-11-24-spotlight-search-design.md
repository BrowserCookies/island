# Spotlight search — design

Date: 2026-11-24. Status: approved in conversation ("start working").

## Intent

A keyboard launcher for Island. A global shortcut pops a pill-shaped search bar in the middle of
the screen, on top of everything, with the cursor already in it. Typing finds apps, folders, files,
Island's own buttons and actions, evaluates quick calculations, and finds songs that play in Spotify.
It should feel almost instant, like the build-menu search in Satisfactory: results update on every
keystroke with no visible lag, Enter does the obvious thing.

## Goals

- **Shortcut:** `Win+Shift+F` by default, configurable in Settings → General next to the island
  hotkey. (Ctrl+Shift+F was rejected: it is "Find in files" in many editors, and a global hotkey
  would take it from them. Alt+Shift+… was rejected: Alt+Shift switches keyboard layouts.)
- **Instant:** the window is created and laid out at startup and kept hidden; the hotkey only
  shows it. Local results (apps, Island items, calculator) are computed synchronously per keystroke
  from in-memory data. Files/folders and songs arrive asynchronously and are merged in.
- **Look:** the same pill shape and theme brushes as the island (`IslandBackgroundBrush`,
  `IslandBorderBrush`, `AccentBrush`, drop shadow). Results appear in a rounded card under the pill.
- **Always on top, takes focus:** unlike the island, this window activates so the user can type.
- **Sources:**
  - Installed apps, including Store apps (the shell's Apps folder).
  - Island buttons (including group children) and built-in Island commands (Settings, Show/Hide
    island, Script run log, Screenshot).
  - Files and folders through the Windows Search index.
  - Calculator: `12*7+3` → `= 87`.
  - Spotify tracks, when signed in (the user has Premium).
- **Services tab:** a new Settings tab, "Services", with Spotify sign-in now and room for more.

## Non-goals

- No own file crawler or file list in memory (option 2 in the conversation; rejected for RAM and
  startup cost). Folders Windows doesn't index aren't found.
- No web search, no Spotify playlists/albums/podcasts (tracks only for now), no free-tier Spotify.
- No visual copy of Satisfactory; it is a reference for behavior and feel only.

## Anti-goals

- Typing lag, or a visible delay between the hotkey and the bar.
- Stealing a shortcut other apps rely on.
- Secrets (Spotify tokens) in `config.json`, which users export and share.
- The search bar staying around after it loses focus.

## Approaches considered

1. **Apps in memory + Windows Search index for files (chosen).** ~264 apps on the user's PC; names
   and launch paths are well under 1 MB; icons are loaded only for visible rows (~9 KB each).
   Files come from the index Windows already maintains.
2. **Island's own file list** (Everything-style). Finds unindexed folders, but costs tens to
   hundreds of MB and a long startup scan (listing three user folders took > 3 minutes).
3. **Walk the disk per keystroke.** Too slow; fails "instant".

## Design

### Core (platform-neutral, unit-tested) — `Island.Core/Search/`

- `Calculator.TryEvaluate(string, out double)`: recursive-descent parser. `+ - * / ^`, `×` `÷`,
  `x` between numbers, parentheses, unary minus, postfix `%` (percent, divides by 100), `.` or `,`
  as decimal separator. Only treated as a calculation if the input contains an operator or
  parentheses and at least one digit. Result formatting: up to 10 significant decimals, invariant
  culture, no exponent for normal magnitudes.
- `SearchMatcher.Score(query, text)`: case- and accent-insensitive. Exact > prefix > word-start
  prefix > initials (`vsc` → Visual Studio Code) > substring > in-order subsequence; 0 = no match.
- `SearchHistory`: launch counts and last-used time per result key, persisted to
  `%APPDATA%\Island\search-history.json` (capped at 500 entries). Gives a bounded boost so frequently
  used results rise to the top for the same query.
- `SearchResultKind` enum and `SearchCandidate` record (key, title, subtitle, kind).
- `SpotifyAuth` helpers: PKCE verifier/challenge generation, authorize URL building, callback
  query parsing. `SpotifyJson` parsing of search results and error reasons.
- `GeneralSettings.SearchHotkey` (`"Win+Shift+F"`), `IslandConfig.Services`
  (`ServicesSettings` → `SpotifySettings { ClientId }`). New properties default sensibly, so no
  schema migration is needed.

### WPF app — `Island/Search/`

- `AppCatalog`: enumerates `shell:AppsFolder` via `Shell.Application` COM on a dedicated STA thread
  at startup, refreshed in the background when the bar opens and the list is older than 10 minutes.
  Launch: `shell:AppsFolder\<parsing name>`. Icons via `IconService.GetFileIcon` (supports
  `shell:` paths since the Calculator icon fix).
- `FileSearch`: Windows Search via `System.Data.OleDb` (`Provider=Search.CollatorDSO`), off the UI
  thread, newest query wins (previous one cancelled), `TOP 8`, filename prefix match
  (`CONTAINS(System.FileName, '"q*"')`), folders and files, ordered by rank then date modified.
  Fails quietly (no results) if the Windows Search service is off.
- `SpotifyService`: Authorization Code + PKCE. Sign-in opens the browser; a `TcpListener` on
  `127.0.0.1:43819` receives `/callback` (no http.sys, no admin). Scopes:
  `user-read-playback-state user-modify-playback-state`. Refresh token stored with DPAPI
  (`ProtectedData`, current user) in `%APPDATA%\Island\secrets\spotify.bin`. Search: `GET /v1/search
  type=track limit=5`, debounced 250 ms, queries ≥ 2 characters. Play: `PUT /v1/me/player/play`;
  on `NO_ACTIVE_DEVICE`, use an available device, or start the Spotify app and wait up to ~10 s
  for a device, then play on it. Errors become toasts.
- `SearchWindow` + `SearchViewModel`: borderless, transparent, topmost, tool window (no taskbar,
  no Alt+Tab). Centered horizontally on the monitor under the mouse cursor, pill top at ~28% of the
  work area height, so results grow downward. Hides on Esc, on deactivation, and before running a
  result. Clears the query each time it opens. Keys: ↑/↓ select, Enter run, Ctrl+Enter / Shift+Enter
  open the containing folder for files and folders, Esc close. Max 8 visible rows.
- Ranking: calculator result first when present; then local results by score + history boost;
  file results and songs appended in their own blocks as they arrive (so rows the user is about to
  press don't jump).
- Running a result: apps/files/folders via `ShellLauncher`; Island buttons via
  `ActionRunner.RunAsync` (groups expand the island panel with that group open); built-in commands
  call the `IslandController`; calculator copies the result to the clipboard with a toast; songs
  play via `SpotifyService`.
- Hotkey: registered by `IslandController.RegisterHotkeys` alongside the island hotkey.

### Settings

- **General:** "Search shortcut" capture box with the same behavior as the island hotkey box
  (availability check, Clear).
- **Services (new tab):** Spotify card with step-by-step setup (create an app at
  developer.spotify.com, add redirect URI `http://127.0.0.1:43819/callback`, select Web API, paste
  the Client ID), Client ID box, Sign in / Sign out, status line ("Signed in as …").

## Constraints

- .NET 8 WPF, MVVM, project conventions in CLAUDE.md (physical pixels for positioning,
  `DynamicResource` brushes, Core has no WPF/Win32).
- Spotify Development Mode (Feb 2026 rules): owner needs Premium; search `limit` max 10; loopback
  redirect must be `127.0.0.1`, not `localhost`; quota counted per developer account.

## Left to the builder

Exact pixel sizes, animation (a short fade/scale like the panel), row layout, the list of built-in
commands, and wording.
