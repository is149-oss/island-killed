using System;
using System.Collections.Generic;

namespace Island.Services;

public sealed record LyricLine(TimeSpan Time, string Text, IReadOnlyList<LyricWord>? Words = null);
