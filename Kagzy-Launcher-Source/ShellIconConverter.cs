using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace KLauncher;

public sealed class ShellIconConverter : IValueConverter
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_PIDL = 0x000000008;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        out SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfoPidl(
        IntPtr pszPath,
        uint dwFileAttributes,
        out SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(
        string name,
        IntPtr bindContext,
        out IntPtr pidl,
        uint attributesIn,
        out uint attributesOut);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint PrivateExtractIcons(
        string fileName,
        int iconIndex,
        int iconWidth,
        int iconHeight,
        [Out] IntPtr[] iconHandles,
        IntPtr resourceIds,
        uint iconCount,
        uint flags);

    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        System.Globalization.CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
            return null;

        if (path.Equals("builtin:xbox", StringComparison.OrdinalIgnoreCase))
            return CreateXboxFallbackIcon();

        if (path.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
            return ExtractShellAppIcon(path) ?? CreateXboxFallbackIcon();

        if (!File.Exists(path))
            return null;

        try
        {
            if (Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase))
                return LoadSvgIcon(path);

            if (IsImageFile(path))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 256;
                bitmap.UriSource = new Uri(Path.GetFullPath(path), UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                return TrimTransparentPadding(bitmap);
            }

            var nativeIcon = ExtractLargestIcon(path);
            if (nativeIcon is not null)
                return nativeIcon;

            var result = SHGetFileInfo(
                path,
                0,
                out var info,
                (uint)Marshal.SizeOf<SHFILEINFO>(),
                SHGFI_ICON | SHGFI_LARGEICON);

            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
                return ExtractAssociatedIcon(path);

            try
            {
                var image = Imaging.CreateBitmapSourceFromHIcon(
                    info.hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                image.Freeze();
                return image;
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }
        catch
        {
            return ExtractAssociatedIcon(path);
        }
    }

    private static ImageSource? ExtractAssociatedIcon(string path)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon is null)
                return null;

            var image = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? ExtractLargestIcon(string path)
    {
        // Many Windows apps store several icon resolutions in the executable. Ask for
        // the largest frame first so the 68 px cards do not get stretched from 32 px.
        foreach (var size in new[] { 256, 128, 96, 64, 48 })
        {
            var handles = new IntPtr[1];
            try
            {
                if (PrivateExtractIcons(path, 0, size, size, handles, IntPtr.Zero, 1, 0) == 0 ||
                    handles[0] == IntPtr.Zero)
                    continue;

                var image = Imaging.CreateBitmapSourceFromHIcon(
                    handles[0],
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();
                return image;
            }
            catch
            {
                // Try the next smaller frame if this executable does not expose it.
            }
            finally
            {
                if (handles[0] != IntPtr.Zero)
                    DestroyIcon(handles[0]);
            }
        }

        return null;
    }

    private static ImageSource? LoadSvgIcon(string path)
    {
        try
        {
            var root = XDocument.Load(path).Root;
            var viewBoxText = root?.Attribute("viewBox")?.Value;
            if (root is null || string.IsNullOrWhiteSpace(viewBoxText))
                return null;

            var viewBox = viewBoxText.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
                .Select(value => double.Parse(value, System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();
            if (viewBox.Length != 4 || viewBox[2] <= 0 || viewBox[3] <= 0)
                return null;

            const double canvasSize = 96;
            const double inset = 4;
            var scale = Math.Min(canvasSize - inset * 2, canvasSize - inset * 2) / Math.Max(viewBox[2], viewBox[3]);
            var offsetX = (canvasSize - viewBox[2] * scale) / 2 - viewBox[0] * scale;
            var offsetY = (canvasSize - viewBox[3] * scale) / 2 - viewBox[1] * scale;
            var transform = new MatrixTransform(scale, 0, 0, scale, offsetX, offsetY);
            var group = new DrawingGroup();
            var color = IconColor(Path.GetFileNameWithoutExtension(path));

            foreach (var pathElement in root.Elements().Where(element => element.Name.LocalName == "path"))
            {
                var data = pathElement.Attribute("d")?.Value;
                if (string.IsNullOrWhiteSpace(data))
                    continue;

                // Geometry.Parse returns a frozen Freezable; clone it before assigning a transform.
                var geometry = Geometry.Parse(data).CloneCurrentValue();
                geometry.Transform = transform;
                geometry.Freeze();
                group.Children.Add(new GeometryDrawing(color, null, geometry));
            }

            if (group.Children.Count == 0)
                return null;

            group.Freeze();
            var image = new DrawingImage(group);
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static SolidColorBrush IconColor(string iconName)
    {
        var hex = iconName.ToLowerInvariant() switch
        {
            "discord" => "#5865F2",
            "epicgames" => "#FFFFFF",
            "operagx" => "#FA1E4E",
            "riotgames" => "#EB0029",
            "steam" => "#66C0F4",
            _ => "#F1F1F1"
        };

        var brush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }

    private static ImageSource? ExtractShellAppIcon(string parsingName)
    {
        IntPtr pidl = IntPtr.Zero;
        IntPtr icon = IntPtr.Zero;
        try
        {
            if (SHParseDisplayName(parsingName, IntPtr.Zero, out pidl, 0, out _) != 0 || pidl == IntPtr.Zero)
                return null;

            var result = SHGetFileInfoPidl(
                pidl,
                0,
                out var info,
                (uint)Marshal.SizeOf<SHFILEINFO>(),
                SHGFI_PIDL | SHGFI_ICON | SHGFI_LARGEICON);
            icon = info.hIcon;
            if (result == IntPtr.Zero || icon == IntPtr.Zero)
                return null;

            var image = Imaging.CreateBitmapSourceFromHIcon(
                icon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (icon != IntPtr.Zero)
                DestroyIcon(icon);
            if (pidl != IntPtr.Zero)
                CoTaskMemFree(pidl);
        }
    }

    private static ImageSource CreateXboxFallbackIcon()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 124, 16)),
            null,
            new EllipseGeometry(new System.Windows.Point(48, 48), 47, 47)));

        var mark = Geometry.Parse(
            "M 23,22 C 33,22 41,32 48,41 C 55,32 63,22 73,22 " +
            "C 70,32 61,42 54,48 C 61,54 70,64 73,74 " +
            "C 63,74 55,64 48,55 C 41,64 33,74 23,74 " +
            "C 26,64 35,54 42,48 C 35,42 26,32 23,22 Z");
        group.Children.Add(new GeometryDrawing(System.Windows.Media.Brushes.White, null, mark));
        group.Freeze();

        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    private static ImageSource TrimTransparentPadding(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[height * stride];
        converted.CopyPixels(pixels, stride, 0);

        var left = width;
        var top = height;
        var right = -1;
        var bottom = -1;

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            if (pixels[y * stride + x * 4 + 3] <= 8)
                continue;

            if (x < left) left = x;
            if (x > right) right = x;
            if (y < top) top = y;
            if (y > bottom) bottom = y;
        }

        if (right < left || bottom < top)
            return source;

        var padding = Math.Max(1, Math.Max(right - left + 1, bottom - top + 1) / 24);
        left = Math.Max(0, left - padding);
        top = Math.Max(0, top - padding);
        right = Math.Min(width - 1, right + padding);
        bottom = Math.Min(height - 1, bottom + padding);

        var cropWidth = right - left + 1;
        var cropHeight = bottom - top + 1;
        if (cropWidth >= width * 0.98 && cropHeight >= height * 0.98)
            return source;

        var cropped = new CroppedBitmap(source, new Int32Rect(left, top, cropWidth, cropHeight));
        cropped.Freeze();
        return cropped;
    }

    private static bool IsImageFile(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() is
            ".bmp" or ".gif" or ".ico" or ".jpeg" or ".jpg" or ".png" or ".tif" or ".tiff";
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
