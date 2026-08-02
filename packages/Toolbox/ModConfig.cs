namespace Toolbox;

public sealed class ModConfig
{
    public int CheckInterval { get; set; } = 10;

    public int ScanRange { get; set; } = 1;

    public float FurnitureLightRadius { get; set; } = 1.5f;

    public float ObjectLightRadius { get; set; } = 1.5f;

    public bool EnableInputMethodControl { get; set; } = true;
}
