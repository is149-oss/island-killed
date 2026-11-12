using System.Collections.Generic;

namespace Island.Services;

public sealed class DayStat
{
	public string Day { get; set; } = "";

	public int[] Hours { get; set; } = new int[24];

	public Dictionary<string, int> Artists { get; set; } = new Dictionary<string, int>();

	public Dictionary<string, int> Tracks { get; set; } = new Dictionary<string, int>();
}
