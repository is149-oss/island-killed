using System;
using System.Windows.Media;

namespace Island;

public sealed record ToastExtra(string? Sub = null, (string Text, Action Run)? Second = null, ToastAnim Anim = ToastAnim.None, string? DragPath = null, ImageSource? Thumb = null, bool Volume = false);
