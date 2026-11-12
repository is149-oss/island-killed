namespace Island.Services;

public sealed record MonitorSnapshot(double Cpu, double RamPercent, double RamUsedGb, double RamTotalGb, double Gpu, double NetUpMbps, double NetDownMbps, double Disk, int? Fps);
