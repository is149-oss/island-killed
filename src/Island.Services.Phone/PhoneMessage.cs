using System;

namespace Island.Services.Phone;

public sealed record PhoneMessage(string From, string Text, DateTime Time);
