// Build-specific local access; separate from activation keys.
namespace Island.Services;

public static class DeveloperAccess
{
    public static bool Enabled
    {
        get
        {
#if ISLAND_DEVELOPER_PRO
            return true;
#else
            return false;
#endif
        }
    }
}
