using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace Toolbox;

public sealed class ModConfig
{
    public bool EnableAutoPet { get; set; } = true;

    public int CheckInterval { get; set; } = 10;

    public int ScanRange { get; set; } = 1;

    public bool EnableFurnitureLightRadius { get; set; } = true;

    public float FurnitureLightRadius { get; set; } = 1.5f;

    public bool EnableObjectLightRadius { get; set; } = true;

    public float ObjectLightRadius { get; set; } = 1.5f;

    public bool EnableFenceDecay { get; set; } = true;

    public bool EnableAutomaticGates { get; set; } = true;

    public int AutomaticGateCloseDelay { get; set; }

    public bool EnableInputMethodControl { get; set; } = true;

    public bool EnableHarvestWithScythe { get; set; } = true;

    public bool EnableQuickStack { get; set; } = true;

    public int QuickStackRange { get; set; } = 14;

    public bool EnablePassableCrops { get; set; } = true;

    public bool PassableCrops { get; set; } = true;

    public bool PassableScarecrows { get; set; } = true;

    public bool PassableSprinklers { get; set; } = true;

    public bool PassableForage { get; set; } = true;

    public bool PassableTeaBushes { get; set; } = true;

    public int PassableTreeGrowth { get; set; } = 4;

    public int PassableFruitTreeGrowth { get; set; } = 1;

    public bool PassableWeeds { get; set; } = true;

    public bool PassableByAll { get; set; }

    public bool SlowDownWhenPassing { get; set; } = true;

    public bool ShakeWhenPassing { get; set; } = true;

    public bool PlaySoundWhenPassing { get; set; } = true;

    public bool UseCustomDrawing { get; set; } = true;

    public string[] IncludeObjects { get; set; } = Array.Empty<string>();

    public string[] ExcludeObjects { get; set; } = Array.Empty<string>();

    public bool EnableNpcMapLocations { get; set; } = true;

    public NpcIconStyle NpcIconStyle { get; set; } = NpcIconStyle.Default;

    public KeybindList MinimapToggleKey { get; set; } = new(SButton.OemPipe);

    public bool ShowMinimap { get; set; } = true;

    public bool LockMinimapPosition { get; set; }

    public int MinimapX { get; set; } = 12;

    public int MinimapY { get; set; } = 12;

    public int MinimapWidth { get; set; } = 75;

    public int MinimapHeight { get; set; } = 45;

    public float MinimapOpacity { get; set; } = 1f;

    public HashSet<string> MinimapExclusions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public float NpcMarkerScale { get; set; } = 1f;

    public float CurrentPlayerMarkerScale { get; set; } = 1f;

    public float OtherPlayerMarkerScale { get; set; } = 1f;

    public bool? FilterNpcsSpokenTo { get; set; }

    public bool OnlySameLocation { get; set; }

    public int HeartLevelMin { get; set; }

    public int HeartLevelMax { get; set; } = 14;

    public bool ShowQuests { get; set; } = true;

    public bool ShowHiddenVillagers { get; set; }

    public bool ShowBookseller { get; set; } = true;

    public bool ShowTravelingMerchant { get; set; } = true;

    public bool ShowHorse { get; set; } = true;

    public bool ShowChildren { get; set; }

    public bool ShowFarmBuildings { get; set; } = true;

    public Dictionary<string, bool> NpcVisibility { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> NpcMarkerOffsets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public uint MiniMapCacheTicks { get; set; } = 15;

    public uint NpcCacheTicks { get; set; } = 30;
}
