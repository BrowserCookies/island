# Spotlight Search Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** A Win+Shift+F pill search bar that finds apps, Island items, files/folders, does quick math and plays Spotify tracks, plus a configurable shortcut and a Settings → Services tab.

**Architecture:** Pure logic (calculator, matching/ranking, history, Spotify PKCE/JSON, config) lives in `Island.Core/Search` and is unit-tested. The WPF app adds providers (Apps folder catalog, Windows Search OLE DB, Spotify Web API), a pre-created hidden `SearchWindow`, and Settings UI.

**Tech Stack:** .NET 8, WPF, CommunityToolkit.Mvvm, xUnit, `System.Data.OleDb` (Windows Search), `System.Security.Cryptography.ProtectedData` (DPAPI), Spotify Web API.

**Spec:** `docs/superpowers/specs/2026-11-24-spotlight-search-design.md`

## Global Constraints

- Default search hotkey `Win+Shift+F`; configurable in Settings → General.
- Spotify redirect URI exactly `http://127.0.0.1:43819/callback`; scopes `user-read-playback-state user-modify-playback-state`; search `type=track&limit=5`; debounce 250 ms; queries ≥ 2 chars.
- Spotify refresh token only in `%APPDATA%\Island\secrets\spotify.bin` (DPAPI, CurrentUser) — never in `config.json`.
- Search history `%APPDATA%\Island\search-history.json`, max 500 entries.
- File results: `TOP 8`; at most 8 visible rows total in the list.
- Core has no WPF/Win32; positioning in physical pixels; brushes via `DynamicResource`; no implicit usings.
- Don't commit until the user asks (session rule overrides the per-task commit steps).

## Review Focus

1. **Stale async results** — a slow file/Spotify query for "pho" must not replace results for "photos". Expect: only results for the current query generation are shown. → test in Task 2 (`SearchResultSet`).
2. **Quotes in queries** — `it's` or `"x"` typed into search must not break the Windows Search SQL. Expect: escaped, still matches. → test in Task 2 (`WindowsSearchQuery.Build`).
3. **Not-a-calculation input** — `7zip`, `C++`, `-`, `1.` must not show a bogus `= …` row; `2+` must not throw. → tests in Task 1.
4. **Expired/revoked Spotify token** — refresh automatically; if refresh fails, status becomes signed-out and one toast says to sign in again. → Spotify error parsing test in Task 3; behavior verified manually in Task 6.
5. **Hotkey while open / focus loss** — pressing the hotkey again closes the bar; clicking another app closes it. → manual check in Task 5 verification.

---

### Task 1: Calculator (Core)

**Files:**
- Create: `src/Island.Core/Search/Calculator.cs`
- Test: `tests/Island.Tests/SearchTests.cs`

**Interfaces:**
- Produces: `public static class Calculator { bool TryEvaluate(string input, out double value); string Format(double value); }`

- [x] **Step 1: Write failing tests** `CalculatorTests`:
  - `[Theory]` evaluates: `"12*7+3"`→87, `"2^10"`→1024, `"(1+2)*3"`→9, `"-4+10"`→6, `"10/4"`→2.5, `"3,5*2"`→7, `"50%*200"`→100, `"6×7"`→42, `"8÷2"`→4, `"3x4"`→12, `" 1 + 1 "`→2.
  - `[Theory]` rejects (returns false, no throw): `"7zip"`, `"C++"`, `"-"`, `"1."` (plain number), `"2+"`, `"hello"`, `"42"` (no operator → not a calculation), `""`, `"1/0"` (non-finite → false).
  - `Format`: `87`→`"87"`, `2.5`→`"2.5"`, `1d/3`→`"0.3333333333"`, `1e21`→ contains `"E"`.
- [x] **Step 2: Run** `dotnet test tests/Island.Tests --filter CalculatorTests` → FAIL (type missing).
- [x] **Step 3: Implement** recursive descent: expr := term (('+'|'-') term)*; term := power (('*'|'/'|'×'|'÷'|'x') power)*; power := unary ('^' power)?; unary := '-' unary | postfix; postfix := primary '%'?; primary := number | '(' expr ')'. Require at least one binary operator or parentheses. `,` or `.` decimal. `Format`: round to 10 decimals, invariant, trim zeros; `"E"` notation when |v| ≥ 1e15.
- [x] **Step 4: Run** filter → PASS.

### Task 2: Matching, history, result set, Windows Search query (Core)

**Files:**
- Create: `src/Island.Core/Search/SearchMatcher.cs`, `SearchHistory.cs`, `SearchModels.cs`, `WindowsSearchQuery.cs`
- Test: `tests/Island.Tests/SearchTests.cs`

**Interfaces:**
- Produces:
  - `enum SearchResultKind { Calculation, App, IslandButton, IslandCommand, Folder, File, Song }`
  - `sealed record SearchCandidate(string Key, string Title, string? Subtitle, SearchResultKind Kind)`
  - `static class SearchMatcher { int Score(string query, string text); }` — 0 = no match; exact 1000, prefix 800, word-start prefix 600, initials 500, substring 300, subsequence 100 (minus small length penalty `Math.Min(text.Length, 50)`) .
  - `sealed class SearchHistory { static SearchHistory Load(string path); void Save(); void Record(string key, DateTime now); int Boost(string key, DateTime now); }` — boost = `min(count,20)*10` + 50 if used in last 24h; capped at 500 entries (drop least recent).
  - `sealed class SearchResultSet { int Generation; int Begin(string query); bool Accept(int generation); }` — `Begin` increments and returns the generation; `Accept` true only for the current one.
  - `static class WindowsSearchQuery { string Build(string query, int top); }`

- [x] **Step 1: Write failing tests**:
  - `SearchMatcher`: `Score("note","Notepad") > Score("pad","Notepad") > 0`; `Score("vsc","Visual Studio Code") > 0`; `Score("calc","Calculator") > Score("calc","Excel Calculations Tool")`; case/accent-insensitive `Score("cafe","Café") > 0`; `Score("xyz","Notepad") == 0`; exact beats prefix.
  - `SearchHistory`: record twice → `Boost` > record once; round-trip via temp file; 501 distinct keys → 500 after save/load, oldest dropped.
  - `SearchResultSet`: `g1=Begin("pho"); g2=Begin("photos"); Accept(g1)==false; Accept(g2)==true`.
  - `WindowsSearchQuery.Build("it's", 8)` contains `TOP 8`, contains `it''s`, contains no unescaped `'s"`; `Build("a\"b", 8)` contains no raw `"` inside the CONTAINS phrase; `Build("photos", 8)` contains `System.ItemPathDisplay` and `CONTAINS(System.FileName, '"photos*"')`.
- [x] **Step 2: Run** → FAIL.
- [x] **Step 3: Implement.** Normalization: lower-invariant + strip diacritics (FormD, drop NonSpacingMark). Windows Search SQL: `SELECT TOP {n} System.ItemPathDisplay, System.ItemNameDisplay, System.ItemType FROM SystemIndex WHERE CONTAINS(System.FileName, '"{q}*"') AND SCOPE='file:' ORDER BY System.Search.Rank DESC, System.DateModified DESC` with `'`→`''` and `"` removed from q.
- [x] **Step 4: Run** → PASS.

### Task 3: Config additions + Spotify PKCE/JSON (Core)

**Files:**
- Modify: `src/Island.Core/Models/IslandConfig.cs`
- Create: `src/Island.Core/Search/SpotifyProtocol.cs`
- Test: `tests/Island.Tests/SearchTests.cs`, `tests/Island.Tests/ConfigSerializationTests.cs`

**Interfaces:**
- Produces:
  - `GeneralSettings.SearchHotkey` (`string?`, default `"Win+Shift+F"`)
  - `IslandConfig.Services` (`ServicesSettings`), `ServicesSettings.Spotify` (`SpotifySettings`), `SpotifySettings.ClientId` (`string?`)
  - `static class SpotifyProtocol` with `const string RedirectUri = "http://127.0.0.1:43819/callback"`, `const int CallbackPort = 43819`, `const string Scopes = "user-read-playback-state user-modify-playback-state"`, `(string Verifier, string Challenge) CreatePkce()`, `string AuthorizeUrl(string clientId, string challenge, string state)`, `(string? Code, string? State, string? Error) ParseCallback(string requestTarget)`, `IReadOnlyList<SpotifyTrack> ParseTracks(string json)`, `string? ErrorReason(string json)`
  - `sealed record SpotifyTrack(string Uri, string Name, string Artists, string? Album, string? ImageUrl)`

- [x] **Step 1: Write failing tests**: default config has `SearchHotkey == "Win+Shift+F"` and `Services.Spotify.ClientId == null`; JSON without `services` still loads; round-trip keeps ClientId. PKCE: verifier length 43–128, challenge = base64url(SHA256(verifier)) without `=`; `AuthorizeUrl` contains `response_type=code`, `code_challenge_method=S256`, url-encoded redirect `http%3A%2F%2F127.0.0.1%3A43819%2Fcallback`, scopes encoded. `ParseCallback("/callback?code=abc&state=xyz")` → (abc, xyz, null); `?error=access_denied` → error. `ParseTracks` on a sample `/v1/search` body with 2 tracks → names, `"Artist A, Artist B"`, uri, smallest image ≥ 64 px. `ErrorReason("{\"error\":{\"status\":404,\"reason\":\"NO_ACTIVE_DEVICE\"}}") == "NO_ACTIVE_DEVICE"`; non-JSON → null.
- [x] **Step 2: Run** → FAIL. **Step 3: Implement** (System.Text.Json, `RandomNumberGenerator`, `SHA256`). **Step 4: Run** → all tests PASS.

### Task 4: Providers — app catalog and file search (App)

**Files:**
- Create: `src/Island/Search/AppCatalog.cs`, `src/Island/Search/FileSearch.cs`
- Modify: `src/Island/Island.csproj` (add `System.Data.OleDb` 8.x)

**Interfaces:**
- Produces:
  - `sealed class AppCatalog { IReadOnlyList<AppEntry> Apps; void RefreshInBackground(); event EventHandler? Changed; }`, `sealed record AppEntry(string Name, string LaunchPath)` with `LaunchPath = "shell:AppsFolder\\" + parsingName`.
  - `static class FileSearch { Task<IReadOnlyList<FileHit>> QueryAsync(string query, int top, CancellationToken ct); }`, `sealed record FileHit(string Path, string Name, bool IsFolder)`.

- [x] **Step 1:** `AppCatalog` enumerates `Shell.Application.NameSpace("shell:AppsFolder").Items()` via `dynamic` on a dedicated STA thread; skips duplicates by name+path; refresh no more often than every 10 minutes.
- [x] **Step 2:** `FileSearch` opens `Provider=Search.CollatorDSO;Extended Properties='Application=Windows';`, runs `WindowsSearchQuery.Build` on a thread-pool thread, `IsFolder` from `System.ItemType == "Directory"`; any exception → empty list.
- [x] **Step 3: Verify** with a temporary console check or the running app (Task 5): `Calculator`, `Spotify` appear in `Apps`; `QueryAsync("Documents")` returns ≥1 hit. Build: `dotnet build Island.sln` → 0 errors.

### Task 5: Search window, view model, hotkey, running results (App)

**Files:**
- Create: `src/Island/Search/SearchWindow.xaml(.cs)`, `src/Island/Search/SearchViewModel.cs`, `src/Island/Search/SearchController.cs`
- Modify: `src/Island/IslandController.cs` (register `SearchHotkey`; expose `OpenGroup(Guid)`), `src/Island/App.xaml.cs` (create `SearchController` after `IslandController`), `src/Island/Interop/WindowHelper.cs` (if a `ShowAndActivate` helper is needed)

**Interfaces:**
- Consumes: Tasks 1–4; `ActionRunner.RunAsync(ButtonItem)`, `ShellLauncher.Open/OpenPath/RevealFile`, `ClipboardHelper`, `IconService.GetFileIcon`.
- Produces: `SearchController.Toggle()`, `SearchController.SpotifyProvider` hook (set in Task 6): `Func<string, CancellationToken, Task<IReadOnlyList<SpotifyTrack>>>?` and `Func<SpotifyTrack, Task>? PlayTrack`.
  - `SearchResultViewModel` (Title, Subtitle, KindLabel, IconSpec or ImageSource, `Func<Task> Run`, `Action? Reveal`).

- [x] **Step 1:** `SearchWindow`: `WindowStyle=None`, `AllowsTransparency`, `Topmost`, `ShowInTaskbar=False`, `WS_EX_TOOLWINDOW`; pill `TextBox` (height 56, `CornerRadius` 28, island brushes, search glyph `E721`), results card below (max 8 rows, row height 44, icon 28). Created and `Show()`n off-screen then hidden at startup to warm up layout.
- [x] **Step 2:** Show: position on monitor under cursor (physical px: center X, pill top at 28% of work-area height), `Activate()` + `Keyboard.Focus(box)`, clear query, 120 ms fade/scale. Hide on Esc, `Deactivated`, hotkey while visible, and before running a result.
- [x] **Step 3:** `SearchViewModel.Query` setter → synchronous local results (calculator, apps, Island buttons incl. group children, built-in commands: Settings, Show/Hide island, Script run log, Screenshot) ranked by `SearchMatcher.Score + SearchHistory.Boost`, top 6; then `SearchResultSet.Begin` and async file (`TOP 8`) and Spotify (debounced 250 ms, ≥ 2 chars) blocks appended when `Accept(gen)`; total capped at 8 + songs block of up to 5 (list scrolls).
- [x] **Step 4:** Keys: ↑/↓, Enter → run + `SearchHistory.Record`, Ctrl/Shift+Enter → reveal for files/folders, Esc. Calculator Enter → clipboard + toast `"Copied 87"`. Group buttons → `IslandController.OpenGroup(id)`.
- [x] **Step 5: Verify** on Windows: Win+Shift+F opens the bar centered with caret; typing `calc` lists Calculator first; `12*7+3` shows `= 87`; `Documents` shows the folder; Enter launches; pressing hotkey again or clicking elsewhere closes. Run `dotnet test` → all PASS.

### Task 6: Spotify service (App)

**Files:**
- Create: `src/Island/Search/SpotifyService.cs`
- Modify: `src/Island/Island.csproj` (add `System.Security.Cryptography.ProtectedData` 8.x), `src/Island/App.xaml.cs` (construct, wire into `SearchController`)

**Interfaces:**
- Consumes: `SpotifyProtocol`, `SpotifySettings.ClientId`.
- Produces: `sealed class SpotifyService { bool IsSignedIn; string? DisplayName; event EventHandler? StatusChanged; Task SignInAsync(CancellationToken); void SignOut(); Task<IReadOnlyList<SpotifyTrack>> SearchAsync(string q, CancellationToken); Task PlayAsync(SpotifyTrack t); }`

- [x] **Step 1:** Sign-in: PKCE, open `AuthorizeUrl` in browser, `TcpListener(IPAddress.Loopback, 43819)` accepts one GET, parse with `ParseCallback`, reply with a small HTML "You can close this tab", check state, exchange code at `https://accounts.spotify.com/api/token`, `GET /v1/me` for display name. 3-minute timeout.
- [x] **Step 2:** Tokens: access token in memory with expiry; refresh token DPAPI-protected in `secrets\spotify.bin`; refresh 60 s before expiry; refresh failure → sign out + one toast "Spotify: sign in again in Settings → Services".
- [x] **Step 3:** Play: `PUT /v1/me/player/play {"uris":[uri]}`; on `NO_ACTIVE_DEVICE` → `GET /v1/me/player/devices`, use the first; if none, `ShellLauncher.Open("spotify:")`, poll devices every 500 ms up to 10 s, then play with `device_id`. 429 → toast "Spotify rate limit reached".
- [x] **Step 4: Verify** after Task 7 sign-in: typing a song name shows tracks; Enter plays it in Spotify.

### Task 7: Settings — search hotkey box and Services tab (App)

**Files:**
- Modify: `src/Island/Views/SettingsWindow.xaml(.cs)`, `src/Island/ViewModels/SettingsViewModel.cs`

**Interfaces:**
- Consumes: `GeneralSettings.SearchHotkey`, `SpotifyService`, `HotkeyService.IsAvailable`.

- [x] **Step 1:** General: "Search shortcut" capture box + Clear + status, reusing the island hotkey capture logic generalized to a target (`SetHotkey(HotkeyTarget, HotkeyGesture?)`); the island hotkey and search hotkey must differ (status error if equal).
- [x] **Step 2:** New "Services" tab: Spotify card — numbered setup steps (create app at developer.spotify.com/dashboard, add redirect URI `http://127.0.0.1:43819/callback` with a Copy button, tick Web API, paste Client ID), Client ID box bound to `Working.Services.Spotify.ClientId`, Sign in / Sign out button (Sign in saves the Client ID first), status text bound to the service.
- [x] **Step 3: Verify:** change the search shortcut, Save, new shortcut works; Services tab signs in and shows "Signed in as …".

### Task 8: Docs and final verification

**Files:**
- Modify: `README.md` (Search section, Services), `CLAUDE.md` (Search architecture, secrets file, OleDb/ProtectedData deps, `WM_SIZE` panel note)

- [x] **Step 1:** Update docs.
- [x] **Step 2:** `dotnet build Island.sln` (0 warnings new), `dotnet test tests/Island.Tests` all PASS, run the app and walk through Review Focus 1–5.
