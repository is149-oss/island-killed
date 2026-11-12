using System.Collections.Generic;

namespace Island.Services;

public sealed record ListenSummary(int Seconds, int DaysWithMusic, int[] PerDay, string[] DayNames, List<(string Name, int Seconds)> Artists, List<(string Name, int Seconds)> Tracks, int? BestHour);
