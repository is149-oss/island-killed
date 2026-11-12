using System;
using System.Net;
using System.Net.Http;

namespace Island.Services;

public static class NetProxy
{
	private sealed class SettingsProxy : IWebProxy
	{
		public ICredentials? Credentials { get; set; }

		public Uri? GetProxy(Uri destination)
		{
			return Current();
		}

		public bool IsBypassed(Uri host)
		{
			if (!(Current() == null))
			{
				return host.IsLoopback;
			}
			return true;
		}
	}

	public static HttpMessageHandler Handler()
	{
		return new SocketsHttpHandler
		{
			Proxy = new SettingsProxy(),
			UseProxy = true,
			PooledConnectionLifetime = TimeSpan.FromMinutes(1L),
			ConnectTimeout = TimeSpan.FromSeconds(15L)
		};
	}

	public static Uri? Current()
	{
		return Parse(Settings.Current.ProxyAddress);
	}

	public static Uri? Parse(string? address)
	{
		if (string.IsNullOrWhiteSpace(address))
		{
			return null;
		}
		address = address.Trim();
		if (!address.Contains("://"))
		{
			address = "http://" + address;
		}
		if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? result) || result.Host.Length <= 0 || result.Port <= 0)
		{
			return null;
		}
		return result;
	}
}
