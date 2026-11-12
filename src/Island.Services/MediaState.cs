using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Island.Services;

public sealed record MediaState(string Title, string Artist, string SourceApp, bool IsPlaying, BitmapImage? Cover, Color Accent, TimeSpan Position, TimeSpan Duration, DateTimeOffset PositionUpdatedAt, string SourceId = "", bool CanNext = true, bool CanPrevious = true, string Album = "", string AlbumArtist = "", string Genre = "", int TrackNumber = 0, string Kind = "");
