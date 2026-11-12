using System;

namespace Island.Services;

public sealed record HourForecast(DateTime Time, double Temp, int Code, bool IsDay);
