using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Island.Services.Phone;

public static class LinkProtocol
{
	public const int Version = 1;

	public const int DiscoveryPort = 47820;

	public const int DefaultPort = 47821;

	public const int MaxFrame = 1048576;

	public const string DiscoverAsk = "ISLAND?";

	public const string DiscoverReply = "ISLAND!";

	public static async Task WriteFrameAsync(Stream stream, byte[] payload, CancellationToken ct)
	{
		byte[] array = new byte[4];
		BinaryPrimitives.WriteInt32BigEndian(array, payload.Length);
		await stream.WriteAsync(array, ct).ConfigureAwait(continueOnCapturedContext: false);
		await stream.WriteAsync(payload, ct).ConfigureAwait(continueOnCapturedContext: false);
		await stream.FlushAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
	}

	public static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken ct)
	{
		byte[] header = new byte[4];
		await stream.ReadExactlyAsync(header, ct).ConfigureAwait(continueOnCapturedContext: false);
		int num = BinaryPrimitives.ReadInt32BigEndian(header);
		if (num < 0 || num > 1048576)
		{
			throw new InvalidDataException($"frame of {num} bytes");
		}
		byte[] payload = new byte[num];
		await stream.ReadExactlyAsync(payload, ct).ConfigureAwait(continueOnCapturedContext: false);
		return payload;
	}

	public static string ToBase64Url(byte[] data)
	{
		return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-')
			.Replace('/', '_');
	}
}
