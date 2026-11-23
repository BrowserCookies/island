using System.Collections.ObjectModel;
using Island.Core.Models;

namespace Island.Core.Config;

/// <summary>Tree operations used by the settings editor (reorder, move into / out of groups).</summary>
public static class ButtonTree
{
    /// <summary>Finds the collection that directly contains <paramref name="item"/>.</summary>
    public static ObservableCollection<ButtonItem>? FindParentCollection(ObservableCollection<ButtonItem> root, ButtonItem item)
    {
        if (root.Contains(item)) return root;
        foreach (var b in root)
        {
            if (b.Children is null) continue;
            var found = FindParentCollection(b.Children, item);
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>The group button that owns <paramref name="item"/>, or null for top-level buttons.</summary>
    public static ButtonItem? FindParentButton(ObservableCollection<ButtonItem> root, ButtonItem item)
    {
        foreach (var b in root)
        {
            if (b.Children is null) continue;
            if (b.Children.Contains(item)) return b;
            var found = FindParentButton(b.Children, item);
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>True if <paramref name="candidate"/> is <paramref name="ancestor"/> or one of its descendants.</summary>
    public static bool IsSelfOrDescendant(ButtonItem ancestor, ButtonItem candidate)
    {
        if (ReferenceEquals(ancestor, candidate)) return true;
        if (ancestor.Children is null) return false;
        foreach (var c in ancestor.Children)
            if (IsSelfOrDescendant(c, candidate)) return true;
        return false;
    }

    /// <summary>
    /// Moves <paramref name="item"/> into <paramref name="target"/> at <paramref name="index"/>.
    /// Returns false (and does nothing) if the move would put a group inside itself.
    /// </summary>
    public static bool Move(ObservableCollection<ButtonItem> root, ButtonItem item, ObservableCollection<ButtonItem> target, int index)
    {
        var source = FindParentCollection(root, item);
        if (source is null) return false;

        // Refuse moving a group into its own subtree.
        if (item.Children is not null && ContainsCollection(item, target)) return false;

        int oldIndex = source.IndexOf(item);
        if (ReferenceEquals(source, target))
        {
            if (index > oldIndex) index--;
            index = System.Math.Clamp(index, 0, target.Count - 1);
            if (index != oldIndex) source.Move(oldIndex, index);
            return true;
        }

        source.RemoveAt(oldIndex);
        index = System.Math.Clamp(index, 0, target.Count);
        target.Insert(index, item);
        return true;
    }

    private static bool ContainsCollection(ButtonItem group, ObservableCollection<ButtonItem> target)
    {
        if (group.Children is null) return false;
        if (ReferenceEquals(group.Children, target)) return true;
        foreach (var c in group.Children)
            if (ContainsCollection(c, target)) return true;
        return false;
    }

    /// <summary>
    /// Moves up one slot. At the top of a group the item moves out of the group (above it).
    /// Returns false if nothing moved.
    /// </summary>
    public static bool MoveUp(ObservableCollection<ButtonItem> root, ButtonItem item)
    {
        var list = FindParentCollection(root, item);
        if (list is null) return false;
        int i = list.IndexOf(item);
        if (i > 0)
        {
            list.Move(i, i - 1);
            return true;
        }
        var parent = FindParentButton(root, item);
        if (parent is null) return false;
        var grand = FindParentCollection(root, parent)!;
        list.RemoveAt(i);
        grand.Insert(grand.IndexOf(parent), item);
        return true;
    }

    /// <summary>
    /// Moves down one slot. At the bottom of a group the item moves out of the group (below it).
    /// </summary>
    public static bool MoveDown(ObservableCollection<ButtonItem> root, ButtonItem item)
    {
        var list = FindParentCollection(root, item);
        if (list is null) return false;
        int i = list.IndexOf(item);
        if (i < list.Count - 1)
        {
            list.Move(i, i + 1);
            return true;
        }
        var parent = FindParentButton(root, item);
        if (parent is null) return false;
        var grand = FindParentCollection(root, parent)!;
        list.RemoveAt(i);
        grand.Insert(grand.IndexOf(parent) + 1, item);
        return true;
    }
}
