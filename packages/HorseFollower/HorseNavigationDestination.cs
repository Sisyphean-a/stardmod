using Microsoft.Xna.Framework;
using StardewValley;

namespace HorseFollower;

internal sealed class HorseNavigationDestination
{
    private HorseNavigationDestination(
        string id,
        string displayName,
        string mapName,
        Point[] entranceTiles,
        Point[] parkingCandidates,
        bool isCommunityCenter = false)
    {
        Id = id;
        DisplayName = displayName;
        MapName = mapName;
        EntranceTiles = entranceTiles;
        ParkingCandidates = parkingCandidates;
        IsCommunityCenter = isCommunityCenter;
    }

    internal string Id { get; }

    internal string DisplayName { get; }

    internal string MapName { get; }

    internal IReadOnlyList<Point> EntranceTiles { get; }

    internal IReadOnlyList<Point> ParkingCandidates { get; }

    internal bool IsCommunityCenter { get; }

    internal bool IsAvailable
    {
        get
        {
            if (!IsCommunityCenter)
                return true;

            return Game1.MasterPlayer.mailReceived.Contains("ccDoorUnlock")
                || Game1.MasterPlayer.mailReceived.Contains("JojaMember");
        }
    }

    internal string AvailabilityText => IsAvailable ? "" : "尚未开放";

    internal static IReadOnlyList<HorseNavigationDestination> All { get; } = new HorseNavigationDestination[]
    {
        new(
            "SeedShop",
            "皮埃尔杂货店",
            "Town",
            new[] { new Point(43, 56), new Point(44, 56) },
            new[] { new Point(43, 58), new Point(44, 58), new Point(42, 57), new Point(45, 57), new Point(42, 58), new Point(45, 58) }),
        new(
            "JojaMart",
            "乔家超市",
            "Town",
            new[] { new Point(95, 50), new Point(96, 50) },
            new[] { new Point(95, 52), new Point(96, 52), new Point(94, 51), new Point(97, 51), new Point(94, 52), new Point(97, 52) }),
        new(
            "Blacksmith",
            "铁匠铺",
            "Town",
            new[] { new Point(94, 81) },
            new[] { new Point(94, 83), new Point(93, 82), new Point(95, 82), new Point(93, 83), new Point(95, 83) }),
        new(
            "Saloon",
            "星露谷酒吧",
            "Town",
            new[] { new Point(45, 70) },
            new[] { new Point(45, 72), new Point(44, 71), new Point(46, 71), new Point(44, 72), new Point(46, 72) }),
        new(
            "Hospital",
            "哈维诊所",
            "Town",
            new[] { new Point(36, 55) },
            new[] { new Point(36, 57), new Point(35, 56), new Point(37, 56), new Point(35, 57), new Point(37, 57) }),
        new(
            "ArchaeologyHouse",
            "博物馆 / 图书馆",
            "Town",
            new[] { new Point(101, 89) },
            new[] { new Point(101, 91), new Point(100, 90), new Point(102, 90), new Point(100, 91), new Point(102, 91) }),
        new(
            "CommunityCenter",
            "社区中心",
            "Town",
            new[] { new Point(52, 19), new Point(53, 19) },
            new[] { new Point(52, 21), new Point(53, 21), new Point(51, 20), new Point(54, 20), new Point(51, 21), new Point(54, 21) },
            isCommunityCenter: true),
        new(
            "Carpenter",
            "木匠店",
            "Mountain",
            new[] { new Point(12, 25) },
            new[] { new Point(12, 27), new Point(11, 26), new Point(13, 26), new Point(11, 27), new Point(13, 27) }),
        new(
            "AdventureGuild",
            "冒险家公会",
            "Mountain",
            new[] { new Point(76, 8) },
            new[] { new Point(76, 10), new Point(75, 9), new Point(77, 9), new Point(75, 10), new Point(77, 10) }),
        new(
            "AnimalShop",
            "玛妮牧场",
            "Forest",
            new[] { new Point(90, 15) },
            new[] { new Point(90, 17), new Point(89, 16), new Point(91, 16), new Point(89, 17), new Point(91, 17) }),
        new(
            "FishShop",
            "威利鱼店",
            "Beach",
            new[] { new Point(30, 33) },
            new[] { new Point(30, 35), new Point(29, 34), new Point(31, 34), new Point(29, 35), new Point(31, 35) })
    };
}
