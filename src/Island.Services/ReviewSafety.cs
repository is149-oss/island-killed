// Local review policy. This is not an operating-system sandbox.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Island.Services;

public static class ReviewSafety
{
    // Default to local review. Restoring integrations requires an explicit command-line choice.
    public static bool Enabled { get; } = !Environment.GetCommandLineArgs().Contains("--enable-integrations");
    // Narrow opt-in: Windows media sessions only; does not enable HTTP, clipboard or browser inspection.
    public static bool MediaEnabled { get; } = !Enabled || Environment.GetCommandLineArgs().Contains("--enable-media");
    public static string Profile => Path.Combine(Island.Social.WindowsNotificationProvider.HasIdentity
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IslandSocialHub")
        : AppContext.BaseDirectory, Island.Social.SocialBuild.Enabled ? "SocialHubProfile" : DeveloperAccess.Enabled ? "DeveloperProProfile" : "ReviewProfile");

    public static HttpClient Client(HttpMessageHandler? handler = null)
    {
        if (Enabled)
        {
            handler?.Dispose();
            return new HttpClient(new DenyNetwork());
        }
        return handler is null ? new HttpClient() : new HttpClient(handler);
    }

    public static Process? Start(ProcessStartInfo info)
    {
        if (Enabled) throw new InvalidOperationException("Локальная проверка: запуск внешних программ отключён.");
        return Process.Start(info);
    }

    public static Process? Start(string file, string args) => Start(new ProcessStartInfo(file, args));

    public static void RequireUpdates()
    {
        if (DeveloperAccess.Enabled || Enabled || !Integrity.Enforced)
            throw new InvalidOperationException("Встроенное обновление отключено: не настроен доверенный подписант или включена локальная проверка.");
    }

    private sealed class DenyNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(new HttpRequestException("Локальная проверка: HTTP-запрос отключён."));
    }
}
