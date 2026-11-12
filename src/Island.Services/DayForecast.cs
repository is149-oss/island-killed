using System;

namespace Island.Services;

public sealed record DayForecast(DateTime Date, double Max, double Min, int Code, int RainChance);
