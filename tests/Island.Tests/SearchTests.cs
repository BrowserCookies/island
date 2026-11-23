using System;
using Island.Core.Search;

namespace Island.Tests;

public class CalculatorTests
{
    [Theory]
    [InlineData("12*7+3", 87)]
    [InlineData("2^10", 1024)]
    [InlineData("(1+2)*3", 9)]
    [InlineData("-4+10", 6)]
    [InlineData("10/4", 2.5)]
    [InlineData("3,5*2", 7)]
    [InlineData("50%*200", 100)]
    [InlineData("6×7", 42)]
    [InlineData("8÷2", 4)]
    [InlineData("3x4", 12)]
    [InlineData(" 1 + 1 ", 2)]
    [InlineData("2^3^2", 512)] // right-associative
    [InlineData("-2^2", -4)]
    public void Evaluates(string input, double expected)
    {
        Assert.True(Calculator.TryEvaluate(input, out var value));
        Assert.Equal(expected, value, 9);
    }

    [Theory]
    [InlineData("7zip")]
    [InlineData("C++")]
    [InlineData("-")]
    [InlineData("1.")]
    [InlineData("2+")]
    [InlineData("hello")]
    [InlineData("42")]
    [InlineData("")]
    [InlineData("1/0")]
    [InlineData("(1+2")]
    [InlineData("x4")]
    public void Rejects(string input) => Assert.False(Calculator.TryEvaluate(input, out _));

    [Theory]
    [InlineData(87, "87")]
    [InlineData(2.5, "2.5")]
    [InlineData(1d / 3, "0.3333333333")]
    [InlineData(-0.1, "-0.1")]
    public void Formats(double value, string expected) => Assert.Equal(expected, Calculator.Format(value));

    [Fact]
    public void Formats_HugeNumbers_WithExponent() => Assert.Contains("E", Calculator.Format(1e21));
}

public class SearchMatcherTests
{
    [Fact]
    public void Prefix_BeatsSubstring() =>
        Assert.True(SearchMatcher.Score("note", "Notepad") > SearchMatcher.Score("pad", "Notepad"));

    [Fact]
    public void Substring_Matches() => Assert.True(SearchMatcher.Score("pad", "Notepad") > 0);

    [Fact]
    public void Initials_Match() => Assert.True(SearchMatcher.Score("vsc", "Visual Studio Code") > 0);

    [Fact]
    public void WordStart_BeatsInitials() =>
        Assert.True(SearchMatcher.Score("stud", "Visual Studio Code") > SearchMatcher.Score("vsc", "Visual Studio Code"));

    [Fact]
    public void ShorterPrefixMatch_Wins() =>
        Assert.True(SearchMatcher.Score("calc", "Calculator") > SearchMatcher.Score("calc", "Calculations Tool Extended"));

    [Fact]
    public void IsCaseAndAccentInsensitive() => Assert.True(SearchMatcher.Score("CAFE", "Café") > 0);

    [Fact]
    public void Exact_BeatsPrefix() =>
        Assert.True(SearchMatcher.Score("spotify", "Spotify") > SearchMatcher.Score("spot", "Spotify"));

    [Fact]
    public void Subsequence_MatchesLowest()
    {
        int s = SearchMatcher.Score("ntpd", "Notepad");
        Assert.True(s > 0);
        Assert.True(s < SearchMatcher.Score("pad", "Notepad"));
    }

    [Fact]
    public void ScatteredLetters_FromWordStarts_Match() => Assert.True(SearchMatcher.Score("gchr", "Google Chrome") > 0);

    [Theory]
    [InlineData("calc", "Rockstar Games Launcher")]
    [InlineData("calc", "Epic Games Launcher")]
    [InlineData("calc", "Local Security Policy")]
    [InlineData("calc", "VLC media player - reset preferences and cache files")]
    [InlineData("xyz", "Notepad")]
    [InlineData("", "Notepad")]
    [InlineData("dapeton", "Notepad")]
    public void NoMatch_IsZero(string q, string text) => Assert.Equal(0, SearchMatcher.Score(q, text));
}

public class SearchHistoryTests
{
    private static readonly DateTime Now = new(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void MoreUse_MoreBoost()
    {
        var h = new SearchHistory(null);
        h.Record("a", Now);
        h.Record("a", Now);
        h.Record("b", Now);
        Assert.True(h.Boost("a", Now) > h.Boost("b", Now));
        Assert.Equal(0, h.Boost("unknown", Now));
    }

    [Fact]
    public void RecentUse_IsBoosted() 
    {
        var h = new SearchHistory(null);
        h.Record("a", Now.AddDays(-3));
        h.Record("b", Now);
        Assert.True(h.Boost("b", Now) > h.Boost("a", Now));
    }

    [Fact]
    public void RoundTrips_AndCapsAt500()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"island-hist-{Guid.NewGuid():N}.json");
        try
        {
            var h = SearchHistory.Load(path);
            for (int i = 0; i < 501; i++) h.Record($"k{i}", Now.AddMinutes(i));
            h.Save();
            var loaded = SearchHistory.Load(path);
            Assert.Equal(500, loaded.Count);
            Assert.Equal(0, loaded.Boost("k0", Now.AddMinutes(600)));      // oldest dropped
            Assert.True(loaded.Boost("k500", Now.AddMinutes(600)) > 0);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void Load_MissingOrBrokenFile_IsEmpty()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"island-hist-{Guid.NewGuid():N}.json");
        Assert.Equal(0, SearchHistory.Load(path).Count);
        System.IO.File.WriteAllText(path, "{not json");
        try { Assert.Equal(0, SearchHistory.Load(path).Count); }
        finally { System.IO.File.Delete(path); }
    }
}

public class SearchResultSetTests
{
    [Fact]
    public void OnlyLatestGeneration_IsAccepted()
    {
        var set = new SearchResultSet();
        int g1 = set.Begin("pho");
        int g2 = set.Begin("photos");
        Assert.False(set.Accept(g1));
        Assert.True(set.Accept(g2));
        Assert.Equal("photos", set.Query);
    }
}

public class WindowsSearchQueryTests
{
    [Fact]
    public void Builds_PrefixQuery()
    {
        var sql = WindowsSearchQuery.Build("photos", 8);
        Assert.Contains("TOP 8", sql);
        Assert.Contains("System.ItemPathDisplay", sql);
        Assert.Contains("CONTAINS(System.FileName, '\"photos*\"')", sql);
    }

    [Fact]
    public void Escapes_SingleQuotes()
    {
        var sql = WindowsSearchQuery.Build("it's", 8);
        Assert.Contains("it''s", sql);
        Assert.DoesNotContain("it's", sql);
    }

    [Fact]
    public void Removes_DoubleQuotes_FromPhrase()
    {
        var sql = WindowsSearchQuery.Build("a\"b", 8);
        Assert.Contains("'\"ab*\"'", sql);
    }

    [Fact]
    public void BlankQuery_ReturnsNull() => Assert.Null(WindowsSearchQuery.Build("  \" ", 8));
}

public class SearchConfigTests
{
    [Fact]
    public void Defaults_SearchHotkey_AndNoSpotifyClient()
    {
        var cfg = Island.Core.Config.DefaultConfig.Create();
        Assert.Equal("Win+Shift+F", cfg.General.SearchHotkey);
        Assert.Null(cfg.Services.Spotify.ClientId);
    }

    [Fact]
    public void OldConfig_WithoutServices_StillLoads()
    {
        var cfg = Island.Core.Config.ConfigSerializer.Deserialize("""{ "schemaVersion": 1, "general": {}, "buttons": [] }""");
        Assert.NotNull(cfg.Services.Spotify);
        Assert.Equal("Win+Shift+F", cfg.General.SearchHotkey);
    }

    [Fact]
    public void RoundTrip_KeepsSpotifyClientId_AndSearchHotkey()
    {
        var cfg = Island.Core.Config.DefaultConfig.Create();
        cfg.Services.Spotify.ClientId = "abc123";
        cfg.General.SearchHotkey = "Ctrl+Alt+F";
        var back = Island.Core.Config.ConfigSerializer.Deserialize(Island.Core.Config.ConfigSerializer.Serialize(cfg));
        Assert.Equal("abc123", back.Services.Spotify.ClientId);
        Assert.Equal("Ctrl+Alt+F", back.General.SearchHotkey);
    }
}

public class SpotifyProtocolTests
{
    [Fact]
    public void Pkce_ChallengeIsBase64UrlSha256OfVerifier()
    {
        var (verifier, challenge) = SpotifyProtocol.CreatePkce();
        Assert.InRange(verifier.Length, 43, 128);
        var expected = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(expected, challenge);
        Assert.NotEqual(verifier, SpotifyProtocol.CreatePkce().Verifier);
    }

    [Fact]
    public void AuthorizeUrl_HasPkceAndLoopbackRedirect()
    {
        var url = SpotifyProtocol.AuthorizeUrl("client1", "chal", "st8");
        Assert.StartsWith("https://accounts.spotify.com/authorize?", url);
        Assert.Contains("client_id=client1", url);
        Assert.Contains("response_type=code", url);
        Assert.Contains("code_challenge_method=S256", url);
        Assert.Contains("code_challenge=chal", url);
        Assert.Contains("state=st8", url);
        Assert.Contains("redirect_uri=http%3A%2F%2F127.0.0.1%3A43819%2Fcallback", url);
        Assert.Contains("scope=user-read-playback-state%20user-modify-playback-state", url);
    }

    [Fact]
    public void ParseCallback_CodeAndState()
    {
        var r = SpotifyProtocol.ParseCallback("/callback?code=abc&state=xyz");
        Assert.Equal(("abc", "xyz", (string?)null), r);
    }

    [Fact]
    public void ParseCallback_Error() =>
        Assert.Equal("access_denied", SpotifyProtocol.ParseCallback("/callback?error=access_denied&state=xyz").Error);

    [Fact]
    public void ParseCallback_OtherPath_IsNothing() =>
        Assert.Equal(((string?)null, (string?)null, (string?)null), SpotifyProtocol.ParseCallback("/favicon.ico"));

    [Fact]
    public void ParseTracks_ReadsNameArtistsUriAndImage()
    {
        const string json = """
        { "tracks": { "items": [
          { "uri": "spotify:track:1", "name": "Song One", "artists": [ { "name": "Artist A" }, { "name": "Artist B" } ],
            "album": { "name": "Album X", "images": [ { "url": "big", "width": 640, "height": 640 }, { "url": "mid", "width": 300, "height": 300 }, { "url": "small", "width": 64, "height": 64 } ] } },
          { "uri": "spotify:track:2", "name": "Song Two", "artists": [ { "name": "Solo" } ], "album": { "name": "Y", "images": [] } }
        ] } }
        """;
        var tracks = SpotifyProtocol.ParseTracks(json);
        Assert.Equal(2, tracks.Count);
        Assert.Equal(new SpotifyTrack("spotify:track:1", "Song One", "Artist A, Artist B", "Album X", "small"), tracks[0]);
        Assert.Null(tracks[1].ImageUrl);
    }

    [Fact]
    public void ParseTracks_BadJson_IsEmpty() => Assert.Empty(SpotifyProtocol.ParseTracks("<html>"));

    [Fact]
    public void ErrorReason_Parsed()
    {
        Assert.Equal("NO_ACTIVE_DEVICE", SpotifyProtocol.ErrorReason("""{"error":{"status":404,"message":"x","reason":"NO_ACTIVE_DEVICE"}}"""));
        Assert.Null(SpotifyProtocol.ErrorReason("not json"));
        Assert.Null(SpotifyProtocol.ErrorReason("""{"error":"invalid_grant"}"""));
    }
}

public class FileNoiseTests
{
    [Theory]
    [InlineData(@"C:\Users\me\proj\node_modules\mongodb\a.js", true)]
    [InlineData(@"C:\Users\me\proj\.git\HEAD", true)]
    [InlineData(@"C:\Users\me\AppData\Local\Temp\x.txt", true)]
    [InlineData(@"C:\Users\me\proj\obj\Debug\a.cs", true)]
    [InlineData(@"C:\$Recycle.Bin\S-1\a.txt", true)]
    [InlineData(@"C:\Users\me\Documents", false)]
    [InlineData(@"C:\Users\me\Documents\node_modules notes.txt", false)]
    public void IsNoisePath(string path, bool expected) => Assert.Equal(expected, WindowsSearchQuery.IsNoisePath(path));
}

public class SpotifyParsingTests
{
    [Fact]
    public void ParseToken_ReadsAccessExpiryAndRefresh()
    {
        var t = SpotifyProtocol.ParseToken("""{"access_token":"AT","token_type":"Bearer","expires_in":3600,"refresh_token":"RT","scope":"x"}""");
        Assert.NotNull(t);
        Assert.Equal("AT", t!.AccessToken);
        Assert.Equal(3600, t.ExpiresInSeconds);
        Assert.Equal("RT", t.RefreshToken);
    }

    [Fact]
    public void ParseToken_RefreshResponseWithoutNewRefreshToken() =>
        Assert.Null(SpotifyProtocol.ParseToken("""{"access_token":"AT","expires_in":3600}""")!.RefreshToken);

    [Theory]
    [InlineData("""{"error":"invalid_grant","error_description":"Refresh token revoked"}""")]
    [InlineData("nope")]
    public void ParseToken_ErrorOrJunk_IsNull(string json) => Assert.Null(SpotifyProtocol.ParseToken(json));

    [Fact]
    public void ParseDevices_PrefersActive()
    {
        const string json = """{"devices":[{"id":"d1","is_active":false,"name":"Phone"},{"id":"d2","is_active":true,"name":"PC"},{"id":null,"is_active":false,"name":"Restricted"}]}""";
        Assert.Equal("d2", SpotifyProtocol.PickDevice(json));
        Assert.Equal("d1", SpotifyProtocol.PickDevice("""{"devices":[{"id":"d1","is_active":false}]}"""));
        Assert.Null(SpotifyProtocol.PickDevice("""{"devices":[]}"""));
    }

    [Fact]
    public void ParseDisplayName_FallsBackToId()
    {
        Assert.Equal("Nik", SpotifyProtocol.ParseDisplayName("""{"display_name":"Nik","id":"u1"}"""));
        Assert.Equal("u1", SpotifyProtocol.ParseDisplayName("""{"display_name":null,"id":"u1"}"""));
    }
}

public class SearchPolicyTests
{
    [Theory]
    [InlineData(@"C:\docs\report.pdf", "file.pdf")]
    [InlineData(@"C:\docs\Other.PDF", "file.pdf")]
    [InlineData(@"C:\tools\app.exe", @"C:\tools\app.exe")]
    [InlineData(@"C:\links\Thing.lnk", @"C:\links\Thing.lnk")]
    [InlineData(@"C:\icons\a.ico", @"C:\icons\a.ico")]
    [InlineData(@"C:\docs\README", @"C:\docs\README")]
    public void FileIconKey_SharesIconsPerExtension(string path, string expected) =>
        Assert.Equal(expected, SearchPolicy.FileIconKey(path));

    [Theory]
    [InlineData(SearchResultKind.App, true)]
    [InlineData(SearchResultKind.File, true)]
    [InlineData(SearchResultKind.Calculation, true)]
    [InlineData(SearchResultKind.IslandButton, true)]
    [InlineData(SearchResultKind.IslandCommand, false)] // Settings / run log need focus themselves
    public void RestoresFocus_ExceptForIslandWindows(SearchResultKind kind, bool expected) =>
        Assert.Equal(expected, SearchPolicy.RestoreFocusBeforeRunning(kind, opensIslandWindow: false) );

    [Fact]
    public void ButtonThatOpensSettings_KeepsFocus() =>
        Assert.False(SearchPolicy.RestoreFocusBeforeRunning(SearchResultKind.IslandButton, opensIslandWindow: true));
}
