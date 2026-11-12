using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media;
using Windows.Devices.Geolocation;

namespace Island.Services;

public sealed class WeatherService
{
	private static readonly HttpClient Http = CreateClient();

	private (double Lat, double Lon, string City)? _location;

	private bool _loading;

	private const string MetAgent = "Island/1.4 github.com/qxezecwry/Island";

	public WeatherNow? Current { get; private set; }

	public bool Failed => Problem != null;

	public string? Problem { get; private set; }

	public string? City { get; private set; } = Settings.Current.WeatherCity;

	public bool IsStale
	{
		get
		{
			if (!(Current == null))
			{
				return DateTime.Now - Current.UpdatedAt > TimeSpan.FromMinutes(30L);
			}
			return true;
		}
	}

	public event Action? Changed;

	private static HttpClient CreateClient()
	{
		HttpClient httpClient = ReviewSafety.Client(NetProxy.Handler());
		httpClient.Timeout = TimeSpan.FromSeconds(12L);
		httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Island/1.0");
		return httpClient;
	}

	public async Task SetCityAsync(string? city)
	{
		City = (string.IsNullOrWhiteSpace(city) ? null : city.Trim());
		Settings.Current.WeatherCity = City;
		Settings.Current.Save();
		_location = null;
		Current = null;
		Problem = null;
		Changed?.Invoke();
		await RefreshAsync();
	}

	public async Task RefreshAsync()
	{
		if (ReviewSafety.Enabled) return; // Avoids the Windows geolocation permission request too.
		if (_loading || !License.Gate())
		{
			return;
		}
		_loading = true;
		try
		{
			(double, double, string)? location = _location;
			if (!location.HasValue)
			{
				_location = await LocateAsync(City);
			}
			if (!_location.HasValue)
			{
				Problem = ((City != null) ? ("Город «" + City + "» не найден") : "Не удалось определить город — укажите его в меню трея");
				return;
			}
			var (lat, lon, city) = _location.Value;
			try
			{
				Current = await OpenMeteoAsync(lat, lon, city);
			}
			catch (Exception ex) when (!DeveloperEndpoints.CustomForecast && ((ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException || ex is KeyNotFoundException || ex is InvalidOperationException) ? true : false))
			{
				Log.Write("weather: Open-Meteo failed (" + ex.GetType().Name + "), trying MET Norway");
				Current = await MetNorwayAsync(lat, lon, city);
			}
			Problem = null;
		}
		catch (Exception)
		{
			if (Current == null)
			{
				Problem = "Не удалось загрузить погоду";
			}
		}
		finally
		{
			_loading = false;
			Changed?.Invoke();
		}
	}

	private static async Task<WeatherNow> OpenMeteoAsync(double lat, double lon, string city)
	{
		string requestUri = DeveloperEndpoints.WeatherUrl("ISLAND_WEATHER_FORECAST_URL", "https://api.open-meteo.com/v1/forecast") + FormattableString.Invariant($"?latitude={lat:0.###}&longitude={lon:0.###}") + "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m,is_day&hourly=temperature_2m,weather_code,is_day&daily=temperature_2m_max,temperature_2m_min,weather_code,precipitation_probability_max&timezone=auto&forecast_days=7&wind_speed_unit=ms";
		using JsonDocument jsonDocument = JsonDocument.Parse(await Http.GetStringAsync(requestUri));
		JsonElement rootElement = jsonDocument.RootElement;
		JsonElement property = rootElement.GetProperty("current");
		JsonElement property2 = rootElement.GetProperty("daily");
		JsonElement property3 = rootElement.GetProperty("hourly");
		DateTime dateTime = ParseTime(property.GetProperty("time"));
		JsonElement property4 = property3.GetProperty("time");
		JsonElement property5 = property3.GetProperty("temperature_2m");
		JsonElement property6 = property3.GetProperty("weather_code");
		JsonElement property7 = property3.GetProperty("is_day");
		List<HourForecast> list = new List<HourForecast>();
		DateTime date = dateTime.Date;
		DateTime dateTime2 = dateTime.Date.AddDays(1.0).AddHours(6.0);
		for (int i = 0; i < property4.GetArrayLength(); i++)
		{
			DateTime dateTime3 = ParseTime(property4[i]);
			if (!(dateTime3 < date) && !(dateTime3 > dateTime2))
			{
				list.Add(new HourForecast(dateTime3, property5[i].GetDouble(), property6[i].GetInt32(), property7[i].GetInt32() == 1));
			}
		}
		JsonElement property8 = property2.GetProperty("time");
		JsonElement property9 = property2.GetProperty("temperature_2m_max");
		JsonElement property10 = property2.GetProperty("temperature_2m_min");
		JsonElement property11 = property2.GetProperty("weather_code");
		JsonElement property12 = property2.GetProperty("precipitation_probability_max");
		List<DayForecast> list2 = new List<DayForecast>();
		for (int j = 0; j < property8.GetArrayLength(); j++)
		{
			int rainChance = ((property12[j].ValueKind == JsonValueKind.Number) ? property12[j].GetInt32() : 0);
			list2.Add(new DayForecast(ParseTime(property8[j]).Date, property9[j].GetDouble(), property10[j].GetDouble(), property11[j].GetInt32(), rainChance));
		}
		return new WeatherNow(city, property.GetProperty("temperature_2m").GetDouble(), property.GetProperty("apparent_temperature").GetDouble(), property.GetProperty("weather_code").GetInt32(), property.GetProperty("is_day").GetInt32() == 1, property.GetProperty("wind_speed_10m").GetDouble(), property.GetProperty("relative_humidity_2m").GetInt32(), property9[0].GetDouble(), property10[0].GetDouble(), list, list2, DateTime.Now);
	}

	private static async Task<WeatherNow> MetNorwayAsync(double lat, double lon, string city)
	{
		string requestUri = FormattableString.Invariant($"https://api.met.no/weatherapi/locationforecast/2.0/complete?lat={lat:0.####}&lon={lon:0.####}");
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUri);
		request.Headers.UserAgent.Clear();
		request.Headers.TryAddWithoutValidation("User-Agent", "Island/1.4 github.com/qxezecwry/Island");
		using HttpResponseMessage response = await Http.SendAsync(request);
		response.EnsureSuccessStatusCode();
		using JsonDocument jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		JsonElement property = jsonDocument.RootElement.GetProperty("properties").GetProperty("timeseries");
		List<(DateTime, double, double, double, double, string, double)> list = new List<(DateTime, double, double, double, double, string, double)>();
		foreach (JsonElement item4 in property.EnumerateArray())
		{
			DateTime item = DateTime.Parse(item4.GetProperty("time").GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal).ToLocalTime();
			JsonElement property2 = item4.GetProperty("data");
			JsonElement property3 = property2.GetProperty("instant").GetProperty("details");
			string? text = null;
			double num = 0.0;
			string[] array = new string[3] { "next_1_hours", "next_6_hours", "next_12_hours" };
			foreach (string propertyName in array)
			{
				if (!property2.TryGetProperty(propertyName, out var value))
				{
					continue;
				}
				if (text == null)
				{
					text = ((value.TryGetProperty("summary", out var value2) && value2.TryGetProperty("symbol_code", out var value3)) ? value3.GetString() : null);
				}
				if (value.TryGetProperty("details", out var value4))
				{
					JsonElement value6;
					if (value4.TryGetProperty("probability_of_precipitation", out var value5))
					{
						num = Math.Max(num, value5.GetDouble());
					}
					else if (value4.TryGetProperty("precipitation_amount", out value6))
					{
						num = Math.Max(num, (value6.GetDouble() > 0.2) ? 60 : ((value6.GetDouble() > 0.0) ? 30 : 0));
					}
				}
				if (text != null)
				{
					break;
				}
			}
			list.Add((item, Num(property3, "air_temperature"), Num(property3, "apparent_air_temperature", Num(property3, "air_temperature")), Num(property3, "wind_speed"), Num(property3, "relative_humidity"), text, num));
		}
		if (list.Count == 0)
		{
			throw new InvalidOperationException("MET Norway: no forecast");
		}
		(DateTime, double, double, double, double, string, double) tuple = list[0];
		List<HourForecast> list2 = new List<HourForecast>();
		DateTime dateTime = tuple.Item1.Date.AddDays(1.0).AddHours(6.0);
		foreach (var item5 in list)
		{
			if (item5.Item1 <= dateTime && item5.Item6 != null)
			{
				list2.Add(new HourForecast(item5.Item1, item5.Item2, MetCode(item5.Item6), MetIsDay(item5.Item6, item5.Item1)));
			}
		}
		List<DayForecast> list3 = new List<DayForecast>();
		foreach (IGrouping<DateTime, (DateTime, double, double, double, double, string, double)> day in list.GroupBy(((DateTime Time, double Temp, double Feels, double Wind, double Humidity, string Symbol, double Rain) s) => s.Time.Date).Take(7))
		{
			(DateTime, double, double, double, double, string, double) tuple2 = day.Where(((DateTime Time, double Temp, double Feels, double Wind, double Humidity, string Symbol, double Rain) s) => s.Symbol != null).OrderBy(((DateTime Time, double Temp, double Feels, double Wind, double Humidity, string Symbol, double Rain) s) => Math.Abs((s.Time - day.Key.AddHours(13.0)).TotalHours)).FirstOrDefault();
			int code = ((tuple2.Item6 != null) ? MetCode(tuple2.Item6) : 3);
			list3.Add(new DayForecast(day.Key, day.Max(((DateTime Time, double Temp, double Feels, double Wind, double Humidity, string Symbol, double Rain) s) => s.Temp), day.Min(((DateTime Time, double Temp, double Feels, double Wind, double Humidity, string Symbol, double Rain) s) => s.Temp), code, (int)day.Max(((DateTime Time, double Temp, double Feels, double Wind, double Humidity, string Symbol, double Rain) s) => s.Rain)));
		}
		double item2 = tuple.Item2;
		double item3 = tuple.Item3;
		int code2 = ((tuple.Item6 != null) ? MetCode(tuple.Item6) : 3);
		bool isDay;
		if (tuple.Item6 == null)
		{
			int i = tuple.Item1.Hour;
			isDay = i >= 7 && i < 19;
		}
		else
		{
			isDay = MetIsDay(tuple.Item6, tuple.Item1);
		}
		return new WeatherNow(city, item2, item3, code2, isDay, tuple.Item4, (int)Math.Round(tuple.Item5), list3[0].Max, list3[0].Min, list2, list3, DateTime.Now);
	}

	private static double Num(JsonElement e, string name, double fallback = 0.0)
	{
		if (!e.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
		{
			return fallback;
		}
		return value.GetDouble();
	}

	private static bool MetIsDay(string symbol, DateTime time)
	{
		if (!symbol.EndsWith("_day"))
		{
			if (!symbol.EndsWith("_night") && !symbol.EndsWith("_polartwilight"))
			{
				int hour = time.Hour;
				if (hour >= 7)
				{
					return hour < 19;
				}
				return false;
			}
			return false;
		}
		return true;
	}

	private static int MetCode(string symbol)
	{
		string text = symbol.Split('_')[0];
		if (text.Contains("thunder"))
		{
			return 95;
		}
		bool flag = text.StartsWith("heavy");
		bool flag2 = text.StartsWith("light");
		if (text.Contains("snow"))
		{
			if (!flag)
			{
				if (!flag2)
				{
					return 73;
				}
				return 71;
			}
			return 75;
		}
		if (text.Contains("sleet"))
		{
			if (!flag)
			{
				return 66;
			}
			return 67;
		}
		if (text.Contains("rain"))
		{
			if (!text.Contains("showers"))
			{
				if (!flag)
				{
					if (!flag2)
					{
						return 63;
					}
					return 61;
				}
				return 65;
			}
			if (!flag)
			{
				if (!flag2)
				{
					return 81;
				}
				return 80;
			}
			return 82;
		}
		return text switch
		{
			"clearsky" => 0, 
			"fair" => 1, 
			"partlycloudy" => 2, 
			"fog" => 45, 
			_ => 3, 
		};
	}

	private static DateTime ParseTime(JsonElement e)
	{
		return DateTime.Parse(e.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None);
	}

	private static async Task<(double, double, string)?> LocateAsync(string? city)
	{
		if (city != null)
		{
			return await GeocodeAsync(city);
		}
		// A custom provider must not silently invoke Windows/IP geolocation.
		if (DeveloperEndpoints.CustomForecast || DeveloperEndpoints.CustomGeocoding) return null;
		try
		{
			if (await Geolocator.RequestAccessAsync() == GeolocationAccessStatus.Allowed)
			{
				BasicGeoposition position = (await new Geolocator
				{
					DesiredAccuracyInMeters = 3000u
				}.GetGeopositionAsync(TimeSpan.FromHours(1), TimeSpan.FromSeconds(8L))).Coordinate.Point.Position;
				double latitude = position.Latitude;
				double longitude = position.Longitude;
				return (latitude, longitude, await ReverseGeocodeAsync(position.Latitude, position.Longitude));
			}
		}
		catch (Exception)
		{
		}
		using JsonDocument doc = JsonDocument.Parse(await Http.GetStringAsync("https://ipapi.co/json/"));
		JsonElement r = doc.RootElement;
		if (r.TryGetProperty("utc_offset", out var value))
		{
			string? text = value.GetString();
			if (text != null && text.Length == 5 && Math.Abs((double)(((text[0] != '-') ? 1 : (-1)) * (int.Parse(text.Substring(1, 2)) * 60 + int.Parse(text.Substring(3, 2)))) - TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes) > 1.0)
			{
				return null;
			}
		}
		string ipCity = (r.TryGetProperty("city", out var value2) ? (value2.GetString() ?? "") : "");
		(double, double, string)? tuple = await GeocodeAsync(ipCity);
		return (r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble(), tuple?.Item3 ?? ipCity);
	}

	private static async Task<(double, double, string)?> GeocodeAsync(string city)
	{
		if (string.IsNullOrWhiteSpace(city))
		{
			return null;
		}
		try
		{
			string requestUri = DeveloperEndpoints.WeatherUrl("ISLAND_WEATHER_GEOCODING_URL", "https://geocoding-api.open-meteo.com/v1/search") + "?name=" + Uri.EscapeDataString(city) + "&count=1&language=ru";
			using JsonDocument jsonDocument = JsonDocument.Parse(await Http.GetStringAsync(requestUri));
			if (jsonDocument.RootElement.TryGetProperty("results", out var value) && value.GetArrayLength() > 0)
			{
				JsonElement jsonElement = value[0];
				return (jsonElement.GetProperty("latitude").GetDouble(), jsonElement.GetProperty("longitude").GetDouble(), jsonElement.GetProperty("name").GetString() ?? city);
			}
		}
		catch (Exception)
		{
		}
		return DeveloperEndpoints.CustomGeocoding ? null : await NominatimAsync(city);
	}

	private static async Task<(double, double, string)?> NominatimAsync(string city)
	{
		_ = 1;
		try
		{
			using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "https://nominatim.openstreetmap.org/search?q=" + Uri.EscapeDataString(city) + "&format=json&limit=1&accept-language=ru");
			request.Headers.UserAgent.Clear();
			request.Headers.TryAddWithoutValidation("User-Agent", "Island/1.4 github.com/qxezecwry/Island");
			using HttpResponseMessage response = await Http.SendAsync(request);
			response.EnsureSuccessStatusCode();
			using JsonDocument jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
			if (jsonDocument.RootElement.GetArrayLength() == 0)
			{
				return null;
			}
			JsonElement jsonElement = jsonDocument.RootElement[0];
			string text2;
			if (jsonElement.TryGetProperty("name", out var value))
			{
				string? text = value.GetString();
				if (text != null && text.Length > 0)
				{
					text2 = text;
					goto IL_01d0;
				}
			}
			text2 = city;
			goto IL_01d0;
			IL_01d0:
			string item = text2;
			return (double.Parse(jsonElement.GetProperty("lat").GetString(), CultureInfo.InvariantCulture), double.Parse(jsonElement.GetProperty("lon").GetString(), CultureInfo.InvariantCulture), item);
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static async Task<string> ReverseGeocodeAsync(double lat, double lon)
	{
		try
		{
			string requestUri = FormattableString.Invariant($"https://api.bigdatacloud.net/data/reverse-geocode-client?latitude={lat:0.###}&longitude={lon:0.###}&localityLanguage=ru");
			using JsonDocument jsonDocument = JsonDocument.Parse(await Http.GetStringAsync(requestUri));
			JsonElement rootElement = jsonDocument.RootElement;
			string? text = (rootElement.TryGetProperty("city", out var value) ? value.GetString() : null);
			if (string.IsNullOrEmpty(text) && rootElement.TryGetProperty("locality", out var value2))
			{
				text = value2.GetString();
			}
			return text ?? "";
		}
		catch (Exception)
		{
			return "";
		}
	}

	public static (string Glyph, string Text, Color Tint) Describe(int code, bool isDay)
	{
		Color color = Color.FromRgb(byte.MaxValue, 210, 90);
		Color color2 = Color.FromRgb(201, 211, byte.MaxValue);
		Color color3 = Color.FromRgb(216, 222, 230);
		Color item = Color.FromRgb(127, 184, byte.MaxValue);
		Color item2 = Color.FromRgb(207, 232, byte.MaxValue);
		Color item3 = Color.FromRgb(195, 155, byte.MaxValue);
		switch (code)
		{
		case 0:
			return (Glyph: isDay ? "☀" : "☾", Text: "Ясно", Tint: isDay ? color : color2);
		case 1:
			return (Glyph: isDay ? "☀" : "☾", Text: "Преимущественно ясно", Tint: isDay ? color : color2);
		case 2:
			return (Glyph: isDay ? "⛅" : "☁", Text: "Переменная облачность", Tint: isDay ? color : color3);
		case 3:
			return (Glyph: "☁", Text: "Пасмурно", Tint: color3);
		case 45:
		case 48:
			return (Glyph: "☁", Text: "Туман", Tint: color3);
		case 51:
		case 53:
		case 55:
		case 56:
		case 57:
			return (Glyph: "☂", Text: "Морось", Tint: item);
		case 61:
		case 63:
		case 66:
			return (Glyph: "☂", Text: "Дождь", Tint: item);
		case 65:
		case 67:
			return (Glyph: "☂", Text: "Сильный дождь", Tint: item);
		case 80:
		case 81:
		case 82:
			return (Glyph: "☂", Text: "Ливень", Tint: item);
		case 71:
		case 73:
		case 77:
			return (Glyph: "❄", Text: "Снег", Tint: item2);
		case 75:
		case 85:
		case 86:
			return (Glyph: "❄", Text: "Снегопад", Tint: item2);
		case 95:
		case 96:
		case 99:
			return (Glyph: "⛈", Text: "Гроза", Tint: item3);
		default:
			return (Glyph: "☁", Text: "Облачно", Tint: color3);
		}
	}

	public static string FormatTemp(double t)
	{
		return $"{(int)Math.Round(t)}°".Replace("-", "−");
	}
}
