using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Island.Services.Phone;

public sealed class LinkCipher : IDisposable
{
	private const int TagSize = 16;

	private readonly AesGcm _send;

	private readonly AesGcm _receive;

	private ulong _sent;

	private ulong _received;

	private LinkCipher(byte[] sendKey, byte[] receiveKey)
	{
		_send = new AesGcm(sendKey, 16);
		_receive = new AesGcm(receiveKey, 16);
	}

	public static LinkCipher ForPc(byte[] pairKey, byte[] phoneNonce, byte[] pcNonce)
	{
		(byte[] PhoneToPc, byte[] PcToPhone) tuple = DeriveKeys(pairKey, phoneNonce, pcNonce);
		var (receiveKey, _) = tuple;
		return new LinkCipher(tuple.PcToPhone, receiveKey);
	}

	public static LinkCipher ForPhone(byte[] pairKey, byte[] phoneNonce, byte[] pcNonce)
	{
		var (sendKey, receiveKey) = DeriveKeys(pairKey, phoneNonce, pcNonce);
		return new LinkCipher(sendKey, receiveKey);
	}

	public static (byte[] PhoneToPc, byte[] PcToPhone) DeriveKeys(byte[] pairKey, byte[] phoneNonce, byte[] pcNonce)
	{
		byte[] array = new byte[phoneNonce.Length + pcNonce.Length];
		phoneNonce.CopyTo(array, 0);
		pcNonce.CopyTo(array, phoneNonce.Length);
		return (PhoneToPc: HKDF.DeriveKey(HashAlgorithmName.SHA256, pairKey, 32, array, Encoding.ASCII.GetBytes("island-link v1 phone>pc")), PcToPhone: HKDF.DeriveKey(HashAlgorithmName.SHA256, pairKey, 32, array, Encoding.ASCII.GetBytes("island-link v1 pc>phone")));
	}

	private static byte[] Nonce(ulong counter)
	{
		byte[] array = new byte[12];
		BinaryPrimitives.WriteUInt64BigEndian(array.AsSpan(4), counter);
		return array;
	}

	public byte[] Seal(byte[] plain)
	{
		byte[] array = new byte[plain.Length + 16];
		_send.Encrypt(Nonce(_sent++), plain, array.AsSpan(0, plain.Length), array.AsSpan(plain.Length));
		return array;
	}

	public byte[] Open(byte[] frame)
	{
		if (frame.Length < 16)
		{
			throw new CryptographicException("frame too short");
		}
		byte[] array = new byte[frame.Length - 16];
		_receive.Decrypt(Nonce(_received), frame.AsSpan(0, array.Length), frame.AsSpan(array.Length), array);
		_received++;
		return array;
	}

	public void Dispose()
	{
		_send.Dispose();
		_receive.Dispose();
	}
}
