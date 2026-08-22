namespace HorseFollower;

public sealed class ModConfig
{
    public int CheckInterval { get; set; } = 10;

    public int FollowDistance { get; set; } = 4;

    public int FollowStartDistance { get; set; } = 6;

    public int StableRadius { get; set; } = 3;

    // Reserved for the retained navigation implementation; navigation is currently not registered.
    public int NavigationSearchNodesPerUpdate { get; set; } = 32;
}
