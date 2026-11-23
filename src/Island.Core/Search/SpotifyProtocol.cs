using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Island.Core.Search;

public sealed record SpotifyTrack(string Uri, string Name, string Artists, string? Album, string? ImageUrl);

public sealed record SpotifyToken(string AccessToken, int ExpiresInSeconds, string? RefreshToken);

/// <summary>
/// The pure parts of Spotify's Authorization Code + PKCE flow and Web API responses.
/// Development Mode rules (Feb 2026): loopback redirects must use 127.0.0.1, not localhost.
/// </summary>
public static class SpotifyProtocol
{
    public const int CallbackPort = 43819;
    public const string RedirectUri = "http://127.0.0.1:43819/callback";
    public const string Scopes = "user-read-playback-state user-modify-playback-state";
    public const string AuthorizeEndpoint = "https://accounts.spotify.com/authorize";
    public const string TokenEndpoint = "https://accounts.spotify.com/api/token";
    public const string ApiBase = "https://api.spotify.com/v1";

    public static (string Verifier, string Challenge) CreatePkce()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(48)); // 64 chars
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    public static string CreateState() => Base64Url(RandomNumberGenerator.GetBytes(16));

    public static string AuthorizeUrl(string clientId, string challenge, string state) =>
        AuthorizeEndpoint + "?" + string.Join("&",
            "client_id=" + Uri.EscapeDataString(clientId),
            "response_type=code",
            "redirect_uri=" + Uri.EscapeDataString(RedirectUri),
            "code_challenge_method=S256",
            "code_challenge=" + Uri.EscapeDataString(challenge),
            "state=" + Uri.EscapeDataString(state),
            "scope=" + Uri.EscapeDataString(Scopes));

    /// <summary>Parses the request target of the loopback redirect, e.g. "/callback?code=…&amp;state=…".</summary>
    public static (string? Code, string? State, string? Error) ParseCallback(string requestTarget)
    {
        int q = requestTarget.IndexOf('?');
        var path = q < 0 ? requestTarget : requestTarget[..q];
        if (!path.Equals("/callback", StringComparison.OrdinalIgnoreCase) || q < 0) return (null, null, null);

        string? code = null, state = null, error = null;
        foreach (var pair in requestTarget[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            var key = eq < 0 ? pair : pair[..eq];
            var value = eq < 0 ? "" : Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
            switch (key)
            {
                case "code": code = value; break;
                case "state": state = value; break;
                case "error": error = value; break;
            }
        }
        return (code, state, error);
    }

    /// <summary>Tracks from a <c>GET /v1/search?type=track</c> response. Bad JSON gives an empty list.</summary>
    public static IReadOnlyList<SpotifyTrack> ParseTracks(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("tracks", out var tracks) || !tracks.TryGetProperty("items", out var items))
                return Array.Empty<SpotifyTrack>();
            var list = new List<SpotifyTrack>();
            foreach (var t in items.EnumerateArray())
            {
                if (t.ValueKind != JsonValueKind.Object) continue;
                var uri = Str(t, "uri");
                var name = Str(t, "name");
                if (uri is null || name is null) continue;
                var artists = t.TryGetProperty("artists", out var a) && a.ValueKind == JsonValueKind.Array
                    ? string.Join(", ", a.EnumerateArray().Select(x => Str(x, "name")).Where(n => n is not null))
                    : "";
                string? album = null, image = null;
                if (t.TryGetProperty("album", out var al) && al.ValueKind == JsonValueKind.Object)
                {
                    album = Str(al, "name");
                    image = SmallestImage(al, minSize: 64);
                }
                list.Add(new SpotifyTrack(uri, name, artists, album, image));
            }
            return list;
        }
        catch (JsonException)
        {
            return Array.Empty<SpotifyTrack>();
        }
    }

    /// <summary>The <c>error.reason</c> of a Web API error body (e.g. NO_ACTIVE_DEVICE), or null.</summary>
    public static string? ErrorReason(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object
                ? Str(e, "reason")
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A token endpoint response, or null for an error body or junk.</summary>
    public static SpotifyToken? ParseToken(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            var access = Str(r, "access_token");
            if (access is null) return null;
            int expires = r.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var ei) ? ei : 3600;
            return new SpotifyToken(access, expires, Str(r, "refresh_token"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>From <c>GET /me/player/devices</c>: the active device, else the first usable one.</summary>
    public static string? PickDevice(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("devices", out var devices) || devices.ValueKind != JsonValueKind.Array) return null;
            string? first = null;
            foreach (var d in devices.EnumerateArray())
            {
                var id = Str(d, "id");
                if (id is null) continue; // restricted devices have no id
                if (d.TryGetProperty("is_active", out var a) && a.ValueKind == JsonValueKind.True) return id;
                first ??= id;
            }
            return first;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? ParseDisplayName(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return Str(doc.RootElement, "display_name") ?? Str(doc.RootElement, "id");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? SmallestImage(JsonElement album, int minSize)
    {
        if (!album.TryGetProperty("images", out var imgs) || imgs.ValueKind != JsonValueKind.Array) return null;
        string? best = null;
        int bestSize = int.MaxValue;
        foreach (var i in imgs.EnumerateArray())
        {
            int w = i.TryGetProperty("width", out var wv) && wv.TryGetInt32(out var wi) ? wi : 0;
            var url = Str(i, "url");
            if (url is null || w < minSize || w >= bestSize) continue;
            best = url;
            bestSize = w;
        }
        return best;
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
