using System;
using System.Collections.Generic;

namespace Island.Services;

public sealed record WeatherNow(string City, double Temp, double FeelsLike, int Code, bool IsDay, double Wind, int Humidity, double Max, double Min, IReadOnlyList<HourForecast> Hours, IReadOnlyList<DayForecast> Days, DateTime UpdatedAt);
