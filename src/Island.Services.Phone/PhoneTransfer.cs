using System;

namespace Island.Services.Phone;

public sealed record PhoneTransfer(string Id, string Name, long Done, long Size, bool Incoming, bool Clipboard, PhoneTransferState State)
{
	public double Fraction
	{
		get
		{
			if (Size <= 0)
			{
				return (State == PhoneTransferState.Done) ? 1 : 0;
			}
			return Math.Min(1.0, (double)Done / (double)Size);
		}
	}
}
