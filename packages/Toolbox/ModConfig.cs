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

    public bool EnableFarmMusic { get; set; } = true;

    public bool EnableFenceDecay { get; set; } = true;

    public bool EnableAutomaticGates { get; set; } = true;

    public int AutomaticGateCloseDelay { get; set; }

    public bool EnableInputMethodControl { get; set; } = true;

    public bool EnableHarvestWithScythe { get; set; } = true;
}
