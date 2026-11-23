using System.Threading;

namespace Island.Core.Search;

public enum SearchResultKind { Calculation, App, IslandButton, IslandCommand, Folder, File, Song }

/// <summary>A searchable item. <see cref="Key"/> identifies it in <see cref="SearchHistory"/>.</summary>
public sealed record SearchCandidate(string Key, string Title, string? Subtitle, SearchResultKind Kind);

/// <summary>
/// Tracks the current query so results of slower, asynchronous searches (files, songs) for an
/// older query are dropped instead of replacing the results for what the user typed since.
/// </summary>
public sealed class SearchResultSet
{
    private int _generation;
    private string _query = "";

    public string Query => Volatile.Read(ref _query);

    /// <summary>Starts a new query and returns its generation.</summary>
    public int Begin(string query)
    {
        Volatile.Write(ref _query, query);
        return Interlocked.Increment(ref _generation);
    }

    /// <summary>True if results tagged with <paramref name="generation"/> are still current.</summary>
    public bool Accept(int generation) => Volatile.Read(ref _generation) == generation;
}
