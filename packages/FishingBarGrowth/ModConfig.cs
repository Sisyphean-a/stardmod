namespace FishingBarGrowth;

/// <summary>
/// Mod配置类,用于存储可配置的选项
/// </summary>
public class ModConfig
{
    /// <summary>
    /// 每钓多少条鱼增加1像素长度
    /// </summary>
    public int FishPerPixel { get; set; } = 10;

    /// <summary>
    /// 钓鱼条最大高度限制(像素),0表示无限制
    /// </summary>
    public int MaxBarHeight { get; set; } = 600;

    /// <summary>
    /// 是否启用Mod功能
    /// </summary>
    public bool EnableMod { get; set; } = true;

    /// <summary>
    /// 是否排除藻类和海草(152, 153, 157)
    /// </summary>
    public bool ExcludeAlgae { get; set; } = true;

    /// <summary>
    /// 是否在控制台显示调试信息
    /// </summary>
    public bool ShowDebugInfo { get; set; } = false;

    /// <summary>
    /// 是否在屏幕上显示钓鱼统计HUD
    /// </summary>
    public bool ShowFishingHUD { get; set; } = true;

    /// <summary>
    /// HUD显示的X位置偏移(从左边缘开始)
    /// </summary>
    public int HudXOffset { get; set; } = 20;

    /// <summary>
    /// HUD显示的Y位置偏移(从底部开始)
    /// </summary>
    public int HudYOffset { get; set; } = 180;
}

