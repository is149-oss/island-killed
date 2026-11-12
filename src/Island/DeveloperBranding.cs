// Presentation only. Feature access is controlled separately.
using System.Linq;
namespace Island;

public static class DeveloperBranding
{
    // Optional UI-test window behavior; never used as an entitlement check.
    public static bool WorkspaceReview => IsDeveloper && Island.Services.ReviewSafety.Enabled
        && System.Environment.GetCommandLineArgs().Contains("--developer-workspace");
    public static bool IsDeveloper
    {
        get
        {
#if ISLAND_DEVELOPER_BUILD
            return true;
#else
            return false;
#endif
        }
    }
    public static string CardTitle => IsDeveloper ? "island killed" : "Island Pro";
    public static string Subtitle => IsDeveloper ? "999999d" : "Локальная тестовая версия";
    public static string SettingsTitle => IsDeveloper ? "Настройки Island — " + CardTitle : "Настройки Island";
    public static string LockedDescription => IsDeveloper
        ? "Локальная тестовая версия. Доступ к функции определяется действующей лицензией или trial."
        : "Пробный период закончился. Ключ, чтобы вернуть все функции, — в Telegram-боте";
}
