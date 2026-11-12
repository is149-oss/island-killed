using System;

namespace Island.Services;

public sealed class AiException : Exception
{
	public AiException(string message)
		: base(message)
	{
	}
}
