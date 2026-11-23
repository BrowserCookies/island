using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static Island.Interop.NativeMethods;

namespace Island.Services;

/// <summary>Extracts and caches icons from executables, shortcuts and files; loads icon image files.</summary>
public static class IconService
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? GetFileIcon(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        lock (Cache)
        {
            if (Cache.TryGetValue(expanded, out var cached)) return cached;
        }
        var img = Extract(expanded);
        lock (Cache) Cache[expanded] = img;
        return img;
    }

    public static void Invalidate(string path)
    {
        lock (Cache) Cache.Remove(Environment.ExpandEnvironmentVariables(path));
    }

    private static ImageSource? Extract(string path)
    {
        bool exists = File.Exists(path) || Directory.Exists(path);
        uint attrsFlag = exists ? 0 : SHGFI_USEFILEATTRIBUTES;

        // Prefer the 48px "extra large" system image list for crisp icons.
        try
        {
            var info = new SHFILEINFO();
            var r = SHGetFileInfo(path, FILE_ATTRIBUTE_NORMAL, ref info, (uint)System.Runtime.InteropServices.Marshal.SizeOf<SHFILEINFO>(),
                SHGFI_SYSICONINDEX | attrsFlag);
            if (r != IntPtr.Zero)
            {
                var iid = typeof(IImageList).GUID;
                if (SHGetImageList(SHIL_EXTRALARGE, ref iid, out var list) == 0 &&
                    list.GetIcon(info.iIcon, ILD_TRANSPARENT, out var hIcon) == 0 && hIcon != IntPtr.Zero)
                {
                    try { return FromHIcon(hIcon); }
                    finally { DestroyIcon(hIcon); }
                }
            }
        }
        catch (Exception) { }

        try
        {
            var info = new SHFILEINFO();
            var r = SHGetFileInfo(path, FILE_ATTRIBUTE_NORMAL, ref info, (uint)System.Runtime.InteropServices.Marshal.SizeOf<SHFILEINFO>(),
                SHGFI_ICON | SHGFI_LARGEICON | attrsFlag);
            if (r != IntPtr.Zero && info.hIcon != IntPtr.Zero)
            {
                try { return FromHIcon(info.hIcon); }
                finally { DestroyIcon(info.hIcon); }
            }
        }
        catch (Exception) { }

        if (File.Exists(path))
        {
            try
            {
                using var ico = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (ico is not null) return FromHIcon(ico.Handle);
            }
            catch (Exception) { }
        }
        return null;
    }

    private static ImageSource FromHIcon(IntPtr hIcon)
    {
        var src = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        src.Freeze();
        return src;
    }

    public static ImageSource? LoadImageFile(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);
        if (!File.Exists(expanded)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad; // don't keep the file locked
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(expanded, UriKind.Absolute);
            bmp.DecodePixelWidth = 96;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Copies an image into %APPDATA%\Island\icons so the icon survives the original being moved.</summary>
    public static string ImportImage(string sourcePath, string iconsDir)
    {
        Directory.CreateDirectory(iconsDir);
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        var dest = Path.Combine(iconsDir, $"{name}-{Guid.NewGuid().ToString("N")[..8]}{ext}");
        File.Copy(sourcePath, dest, overwrite: false);
        return dest;
    }
}

public sealed record GlyphInfo(string Code, string Name, string Tags)
{
    public string Char => GlyphText(Code);

    public static string GlyphText(string code)
    {
        if (int.TryParse(code.Trim().TrimStart('U', 'u', '+').Replace("0x", "", StringComparison.OrdinalIgnoreCase),
                System.Globalization.NumberStyles.HexNumber, null, out var cp) && cp > 0 && cp <= 0x10FFFF)
            return char.ConvertFromUtf32(cp);
        return "?";
    }
}

/// <summary>The curated glyph list (Segoe Fluent Icons / Segoe MDL2 Assets code points) bundled with the app.</summary>
public static class GlyphCatalog
{
    private static IReadOnlyList<GlyphInfo>? _all;

    public static IReadOnlyList<GlyphInfo> All => _all ??= Load();

    /// <summary>"Segoe Fluent Icons" on Windows 11, with "Segoe MDL2 Assets" as fallback on Windows 10.</summary>
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private static IReadOnlyList<GlyphInfo> Load()
    {
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Island.Assets.glyphs.json");
            if (s is null) return Array.Empty<GlyphInfo>();
            using var doc = JsonDocument.Parse(s);
            return doc.RootElement.EnumerateArray()
                .Select(e => new GlyphInfo(e.GetProperty("code").GetString()!, e.GetProperty("name").GetString()!,
                    e.TryGetProperty("tags", out var t) ? t.GetString() ?? "" : ""))
                .ToList();
        }
        catch (Exception)
        {
            return Array.Empty<GlyphInfo>();
        }
    }
}
