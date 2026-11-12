using System;

namespace Island.Services;

public sealed record CalendarEvent(string Title, DateTime Start, DateTime End, bool AllDay, string? Link, bool IsBirthday, string? LocalId);
