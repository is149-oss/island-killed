using System;
using System.Threading;
using Windows.System.Power;

namespace Island.Services;

public sealed class PowerService
{
	private SynchronizationContext? _ctx;

	private int _lastPercent;

	public bool HasBattery => PowerManager.BatteryStatus != BatteryStatus.NotPresent;

	public int Percent => PowerManager.RemainingChargePercent;

	public bool IsPluggedIn => PowerManager.PowerSupplyStatus != PowerSupplyStatus.NotPresent;

	public event Action<PowerChange>? Changed;

	public void Init()
	{
		_ctx = SynchronizationContext.Current;
		_lastPercent = Percent;
		PowerManager.PowerSupplyStatusChanged += (object? _, object _) =>
		{
			Post(PowerChange.PluggedChanged);
		};
		PowerManager.RemainingChargePercentChanged += (object? _, object _) =>
		{
			int percent = Percent;
			bool num = (_lastPercent > 20 && percent <= 20) || (_lastPercent > 10 && percent <= 10);
			_lastPercent = percent;
			if (num && !IsPluggedIn)
			{
				Post(PowerChange.Low);
			}
		};
	}

	private void Post(PowerChange change)
	{
		_ctx?.Post((object? _) =>
		{
			Changed?.Invoke(change);
		}, null);
	}
}
