using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using Island.Core.Search;
using Island.Services;

namespace Island.Search;

/// <summary>
/// Spotify sign-in (Authorization Code + PKCE, loopback redirect on 127.0.0.1), track search and
/// playback. The refresh token is encrypted for the current Windows user (DPAPI) and stored in
/// <c>%APPDATA%\Island\secrets\spotify.bin</c>, never in config.json.
/// </summary>
public sealed class SpotifyService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Island.Spotify.v1");

    private string? _clientId; // the Client ID that issued the stored refresh token
    private readonly string _secretsPath;
    private readonly ToastService _toasts;
    private readonly Dispatcher _dispatcher;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _refreshToken;
    private string? _accessToken;
    private DateTime _accessExpiresUtc;
    private DateTime _lastRateLimitToast;

    public SpotifyService(string dataDirectory, ToastService toasts, Dispatcher dispatcher)
    {
        _secretsPath = Path.Combine(dataDirectory, "secrets", "spotify.bin");
        _toasts = toasts;
        _dispatcher = dispatcher;
        LoadSecrets();
    }

    public bool IsSignedIn => _refreshToken is not null;
    public string? DisplayName { get; private set; }
    public bool IsSigningIn { get; private set; }

    public event EventHandler? StatusChanged;

    // ------------------------------------------------------------------ sign-in

    public async Task SignInAsync(string? clientIdInput, CancellationToken ct)
    {
        var clientId = clientIdInput?.Trim();
        if (string.IsNullOrEmpty(clientId)) throw new InvalidOperationException("Paste your Spotify Client ID first.");

        TcpListener listener;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, SpotifyProtocol.CallbackPort);
            listener.Start();
        }
        catch (SocketException)
        {
            throw new InvalidOperationException($"Port {SpotifyProtocol.CallbackPort} is in use by another program, so the Spotify sign-in can't finish. Close it and try again.");
        }

        IsSigningIn = true;
        RaiseStatus();
        try
        {
            var (verifier, challenge) = SpotifyProtocol.CreatePkce();
            var state = SpotifyProtocol.CreateState();
            ShellLauncher.Open(SpotifyProtocol.AuthorizeUrl(clientId, challenge, state));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            string code = await WaitForCallbackAsync(listener, state, timeout.Token);

            var token = await PostTokenAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = SpotifyProtocol.RedirectUri,
                ["client_id"] = clientId,
                ["code_verifier"] = verifier,
            }, ct) ?? throw new InvalidOperationException("Spotify didn't accept the sign-in. Check the Client ID and the redirect URI in your Spotify app.");

            _accessToken = token.AccessToken;
            _accessExpiresUtc = DateTime.UtcNow.AddSeconds(token.ExpiresInSeconds);
            _refreshToken = token.RefreshToken ?? throw new InvalidOperationException("Spotify didn't return a refresh token.");
            _clientId = clientId;
            DisplayName = await GetDisplayNameAsync(ct);
            SaveSecrets();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException("Sign-in timed out. Try again and finish it in the browser within 3 minutes.");
        }
        finally
        {
            listener.Stop();
            IsSigningIn = false;
            RaiseStatus();
        }
    }

    private static async Task<string> WaitForCallbackAsync(TcpListener listener, string expectedState, CancellationToken ct)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(ct);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            // Browsers may open a connection and send nothing on it (preconnect); don't wait on it.
            using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            string requestLine;
            try
            {
                requestLine = await reader.ReadLineAsync(readTimeout.Token) ?? "";
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                continue;
            }
            var parts = requestLine.Split(' ');
            var (code, state, error) = parts.Length >= 2 ? SpotifyProtocol.ParseCallback(parts[1]) : (null, null, null);

            bool done = code is not null || error is not null;
            var body = error is not null ? "Spotify sign-in was cancelled. You can close this tab."
                : code is not null ? "Island is connected to Spotify. You can close this tab."
                : "";
            var html = $"<!doctype html><meta charset=utf-8><title>Island</title><body style=\"font-family:Segoe UI;background:#1c1c1f;color:#eee;display:grid;place-items:center;height:100vh;margin:0\"><p style=\"font-size:18px\">{body}</p>";
            var bytes = Encoding.UTF8.GetBytes(html);
            var header = $"HTTP/1.1 {(done ? "200 OK" : "404 Not Found")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(header), ct);
            await stream.WriteAsync(bytes, ct);

            if (error is not null) throw new InvalidOperationException(error == "access_denied" ? "Spotify sign-in was cancelled." : $"Spotify sign-in failed: {error}");
            if (code is null) continue; // e.g. the browser asking for /favicon.ico
            if (state != expectedState) throw new InvalidOperationException("Spotify sign-in failed: the reply didn't match this sign-in. Try again.");
            return code;
        }
    }

    public void SignOut()
    {
        _refreshToken = null;
        _accessToken = null;
        _clientId = null;
        DisplayName = null;
        try { File.Delete(_secretsPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        RaiseStatus();
    }

    // ------------------------------------------------------------------ search & play

    public async Task<IReadOnlyList<SpotifyTrack>> SearchAsync(string query, CancellationToken ct)
    {
        if (!IsSignedIn) return Array.Empty<SpotifyTrack>();
        try
        {
            var url = $"{SpotifyProtocol.ApiBase}/search?type=track&limit=5&q={Uri.EscapeDataString(query)}";
            using var resp = await SendAsync(HttpMethod.Get, url, null, ct);
            if (resp is null) return Array.Empty<SpotifyTrack>();
            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            {
                NotifyRateLimited();
                return Array.Empty<SpotifyTrack>();
            }
            if (!resp.IsSuccessStatusCode) return Array.Empty<SpotifyTrack>();
            return SpotifyProtocol.ParseTracks(await resp.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return Array.Empty<SpotifyTrack>();
        }
    }

    public async Task PlayAsync(SpotifyTrack track)
    {
        var body = JsonSerializer.Serialize(new { uris = new[] { track.Uri } });
        using (var resp = await SendAsync(HttpMethod.Put, $"{SpotifyProtocol.ApiBase}/me/player/play", body, CancellationToken.None))
        {
            if (resp is null) throw new InvalidOperationException("Sign in to Spotify in Settings → Services.");
            if (resp.IsSuccessStatusCode) return;
            var text = await resp.Content.ReadAsStringAsync();
            var reason = SpotifyProtocol.ErrorReason(text);
            if (reason == "PREMIUM_REQUIRED") throw new InvalidOperationException("Spotify only lets apps start playback on Premium accounts.");
            if (resp.StatusCode != HttpStatusCode.NotFound && reason != "NO_ACTIVE_DEVICE")
                throw new InvalidOperationException($"Spotify couldn't play it ({(int)resp.StatusCode}).");
        }

        // Nothing is playing anywhere: use an available device, or start the Spotify app and wait for it.
        var device = await FindDeviceAsync();
        if (device is null)
        {
            ShellLauncher.Open("spotify:");
            for (int i = 0; i < 20 && device is null; i++)
            {
                await Task.Delay(500);
                device = await FindDeviceAsync();
            }
        }
        if (device is null) throw new InvalidOperationException("Couldn't find a Spotify device. Open Spotify and try again.");

        using var again = await SendAsync(HttpMethod.Put, $"{SpotifyProtocol.ApiBase}/me/player/play?device_id={Uri.EscapeDataString(device)}", body, CancellationToken.None);
        if (again is null || !again.IsSuccessStatusCode)
            throw new InvalidOperationException($"Spotify couldn't play it ({(again is null ? "signed out" : ((int)again.StatusCode).ToString())}).");
    }

    private async Task<string?> FindDeviceAsync()
    {
        using var resp = await SendAsync(HttpMethod.Get, $"{SpotifyProtocol.ApiBase}/me/player/devices", null, CancellationToken.None);
        return resp is { IsSuccessStatusCode: true } ? SpotifyProtocol.PickDevice(await resp.Content.ReadAsStringAsync()) : null;
    }

    private async Task<string?> GetDisplayNameAsync(CancellationToken ct)
    {
        using var resp = await SendAsync(HttpMethod.Get, $"{SpotifyProtocol.ApiBase}/me", null, ct);
        return resp is { IsSuccessStatusCode: true } ? SpotifyProtocol.ParseDisplayName(await resp.Content.ReadAsStringAsync(ct)) : null;
    }

    // ------------------------------------------------------------------ HTTP + tokens

    /// <summary>Sends an authorized request, refreshing the access token once on 401. Null if signed out.</summary>
    private async Task<HttpResponseMessage?> SendAsync(HttpMethod method, string url, string? jsonBody, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var token = await GetAccessTokenAsync(forceRefresh: attempt > 0, ct);
            if (token is null) return null;
            var req = new HttpRequestMessage(method, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (jsonBody is not null) req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            var resp = await Http.SendAsync(req, ct);
            if (resp.StatusCode != HttpStatusCode.Unauthorized || attempt > 0) return resp;
            resp.Dispose();
        }
        return null;
    }

    private async Task<string?> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct)
    {
        await _tokenLock.WaitAsync(ct);
        try
        {
            if (!forceRefresh && _accessToken is not null && DateTime.UtcNow < _accessExpiresUtc.AddSeconds(-60)) return _accessToken;
            var refresh = _refreshToken;
            var clientId = _clientId;
            if (refresh is null || string.IsNullOrEmpty(clientId)) return null;

            var (token, status) = await PostTokenWithStatusAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refresh,
                ["client_id"] = clientId,
            }, ct);
            if (token is null && status != HttpStatusCode.BadRequest) return null; // Spotify hiccup: stay signed in
            if (token is null)
            {
                // 400 = refresh token revoked or expired for good: sign out and say so once.
                _ = _dispatcher.BeginInvoke(() =>
                {
                    SignOut();
                    _toasts.Error("Sign in again in Settings → Services.", "Spotify");
                });
                _refreshToken = null;
                return null;
            }
            _accessToken = token.AccessToken;
            _accessExpiresUtc = DateTime.UtcNow.AddSeconds(token.ExpiresInSeconds);
            if (token.RefreshToken is not null && token.RefreshToken != _refreshToken)
            {
                _refreshToken = token.RefreshToken;
                SaveSecrets();
            }
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static async Task<SpotifyToken?> PostTokenAsync(Dictionary<string, string> form, CancellationToken ct) =>
        (await PostTokenWithStatusAsync(form, ct)).Token;

    private static async Task<(SpotifyToken? Token, HttpStatusCode Status)> PostTokenWithStatusAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var resp = await Http.PostAsync(SpotifyProtocol.TokenEndpoint, new FormUrlEncodedContent(form), ct);
        return (SpotifyProtocol.ParseToken(await resp.Content.ReadAsStringAsync(ct)), resp.StatusCode);
    }

    private void NotifyRateLimited()
    {
        if (DateTime.UtcNow - _lastRateLimitToast < TimeSpan.FromMinutes(1)) return;
        _lastRateLimitToast = DateTime.UtcNow;
        _dispatcher.BeginInvoke(() => _toasts.Error("Spotify's rate limit was reached; song results are paused for a moment.", "Spotify"));
    }

    // ------------------------------------------------------------------ secrets (DPAPI)

    private sealed record Secrets(string RefreshToken, string? DisplayName, string ClientId);

    private void LoadSecrets()
    {
        try
        {
            if (!File.Exists(_secretsPath)) return;
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(_secretsPath), Entropy, DataProtectionScope.CurrentUser);
            var s = JsonSerializer.Deserialize<Secrets>(plain);
            if (s is null || string.IsNullOrEmpty(s.ClientId)) return;
            _refreshToken = s.RefreshToken;
            DisplayName = s.DisplayName;
            _clientId = s.ClientId;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException or UnauthorizedAccessException)
        {
            _refreshToken = null;
        }
    }

    private void SaveSecrets()
    {
        if (_refreshToken is null || _clientId is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_secretsPath)!);
        var plain = JsonSerializer.SerializeToUtf8Bytes(new Secrets(_refreshToken, DisplayName, _clientId));
        File.WriteAllBytes(_secretsPath, ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser));
    }

    private void RaiseStatus() => _dispatcher.BeginInvoke(() => StatusChanged?.Invoke(this, EventArgs.Empty));
}
