namespace Island.Social;
public static class SocialBuild
{
    public static bool TestWindow => Enabled && Island.Services.ReviewSafety.Enabled
        && System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"--social-test-window")>=0;
    public static bool Enabled {get{
#if ISLAND_SOCIAL_HUB
        return true;
#else
        return false;
#endif
    }}
}
