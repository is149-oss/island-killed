// Central clipboard gate for local review; normal-mode calls retain the original WPF APIs.
using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media.Imaging;
namespace Island.Services;
public static class ReviewClipboard
{
    private static void Check() { if (ReviewSafety.Enabled) throw new InvalidOperationException("Локальная проверка: системный буфер обмена отключён."); }
    public static bool ContainsText() { Check(); return Clipboard.ContainsText(); }
    public static bool ContainsImage() { Check(); return Clipboard.ContainsImage(); }
    public static string GetText() { Check(); return Clipboard.GetText(); }
    public static BitmapSource? GetImage() { Check(); return Clipboard.GetImage(); }
    public static IDataObject? GetDataObject() { Check(); return Clipboard.GetDataObject(); }
    public static void SetText(string text) { Check(); Clipboard.SetText(text); }
    public static void SetImage(BitmapSource image) { Check(); Clipboard.SetImage(image); }
    public static void SetDataObject(object data, bool copy) { Check(); Clipboard.SetDataObject(data, copy); }
    public static void SetFileDropList(StringCollection files) { Check(); Clipboard.SetFileDropList(files); }
}
