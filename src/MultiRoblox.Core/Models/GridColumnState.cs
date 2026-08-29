namespace MultiRoblox.Core.Models;

/// <summary>Persisted layout of one <c>DataGrid</c> column: its width, position and sort state.
/// Matched back to a live column by <see cref="Header"/>.</summary>
public sealed class GridColumnState
{
    public string Header { get; set; } = "";

    /// <summary>Rendered pixel width. 0 = leave the column at its designed width.</summary>
    public double Width { get; set; }

    /// <summary>Left-to-right position (DataGrid DisplayIndex).</summary>
    public int DisplayIndex { get; set; }

    /// <summary>"Ascending" / "Descending" / null (not a sort column).</summary>
    public string? SortDirection { get; set; }
}
