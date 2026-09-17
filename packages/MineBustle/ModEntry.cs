using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using Microsoft.Xna.Framework.Graphics;
using xTile;
using xTile.Dimensions;
using Rectangle = Microsoft.Xna.Framework.Rectangle;

namespace MineBustle;

/// <summary>
/// 模组入口类
/// </summary>
public class ModEntry : Mod
{
    /// <summary>
    /// 模组配置
    /// </summary>
    public static ModConfig Config { get; private set; } = null!;

    /// <summary>
    /// 模组监视器（用于日志输出）
    /// </summary>
    public static IMonitor ModMonitor { get; private set; } = null!;

    /// <summary>
    /// 模组辅助工具
    /// </summary>
    public static IModHelper ModHelper { get; private set; } = null!;

    /// <summary>
    /// 祭坛交互处理器
    /// </summary>
    private AltarInteractionHandler? altarHandler;

    // 定义贴图的虚拟路径
    // 这个路径必须是全局唯一的，用于在内存中链接 TMX 地图和 PNG 贴图
    // "Mods/MineBustle/AltarTilesheet" 是我们在内存中给 altar4.png 起的名字
    private const string TilesheetVirtualPath = "Mods/MineBustle/AltarTilesheet";

    // 定义资源文件的本地相对路径 (相对于 Mods/MineBustle/ 文件夹)
    private const string LocalMapPath = "assets/altar2.tmx";
    private const string LocalTexturePath = "assets/altar4.png";

    /// <summary>
    /// 模组入口方法
    /// </summary>
    public override void Entry(IModHelper helper)
    {
        // 初始化静态引用
        Config = helper.ReadConfig<ModConfig>();
        ModMonitor = Monitor;
        ModHelper = helper;

        // 初始化祭坛交互处理器
        altarHandler = new AltarInteractionHandler(helper, Monitor);
        altarHandler.Register();

        // 注册事件
        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.Events.GameLoop.DayStarted += OnDayStarted;

        // 注册资源请求事件，由 SMAPI 原生处理贴图和地图补丁。
        helper.Events.Content.AssetRequested += OnAssetRequested;

        // 应用 Harmony 补丁
        var harmony = new Harmony(ModManifest.UniqueID);
        harmony.PatchAll();

        Monitor.Log("MineBustle - Yoba's Altar 已加载！", LogLevel.Info);
    }

    /// <summary>
    /// 游戏启动时触发
    /// </summary>
    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        Monitor.Log("游戏已启动，由巴的祭坛已准备就绪。", LogLevel.Debug);

        // 集成 Generic Mod Config Menu
        SetupConfigMenu();
    }

    /// <summary>
    /// 设置 Generic Mod Config Menu
    /// </summary>
    private void SetupConfigMenu()
    {
        // 获取 GMCM API
        var configMenu = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (configMenu is null)
        {
            Monitor.Log("未找到 Generic Mod Config Menu，跳过配置菜单集成。", LogLevel.Debug);
            return;
        }

        // 注册模组
        configMenu.Register(
            mod: ModManifest,
            reset: () => Config = new ModConfig(),
            save: () => {
                Helper.WriteConfig(Config);
                // 配置保存后，立即使矿井地图失效，强制游戏下一帧重新加载
                // 这样用户在 GMCM 中切换开关后，祭坛会立即消失或出现
                Helper.GameContent.InvalidateCache("Maps/Mine");
            }
        );

        // 添加配置选项
        configMenu.AddBoolOption(
            mod: ModManifest,
            name: () => Helper.Translation.Get("config.enable_altar.name"),
            tooltip: () => "",
            getValue: () => Config.EnableAltar,
            setValue: value => Config.EnableAltar = value
        );

        configMenu.AddNumberOption(
            mod: ModManifest,
            name: () => Helper.Translation.Get("config.base_fee.name"),
            tooltip: () => Helper.Translation.Get("config.base_fee.tooltip"),
            getValue: () => (float)Config.BaseFee,
            setValue: value => Config.BaseFee = (int)value,
            min: 0f,
            max: 10000f,
            interval: 50f
        );

        configMenu.AddNumberOption(
            mod: ModManifest,
            name: () => Helper.Translation.Get("config.inflation.name"),
            tooltip: () => Helper.Translation.Get("config.inflation.tooltip"),
            getValue: () => (float)Config.InflationCoefficient,
            setValue: value => Config.InflationCoefficient = (double)value,
            min: 0f,
            max: 0.001f,
            interval: 0.00001f,
            formatValue: value => value.ToString("F5")
        );

        configMenu.AddNumberOption(
            mod: ModManifest,
            name: () => Helper.Translation.Get("config.penalty.name"),
            tooltip: () => Helper.Translation.Get("config.penalty.tooltip"),
            getValue: () => (float)Config.PenaltyExponent,
            setValue: value => Config.PenaltyExponent = (double)value,
            min: 1.0f,
            max: 3.0f,
            interval: 0.1f,
            formatValue: value => value.ToString("F1")
        );

        // --- 新增: 是否减少石头开关 ---
        configMenu.AddBoolOption(
            mod: ModManifest,
            name: () => Helper.Translation.Get("config.reduce_stones.name"),
            tooltip: () => Helper.Translation.Get("config.reduce_stones.tooltip"),
            getValue: () => Config.ReduceStones,
            setValue: value => Config.ReduceStones = value
        );

        Monitor.Log("Generic Mod Config Menu 集成成功！", LogLevel.Debug);
    }

    /// <summary>
    /// 每天开始时重置倍率
    /// </summary>
    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        Config.CurrentMultiplier = 1.0;
        Monitor.Log("新的一天开始，怪物生成倍率已重置为 1.0x", LogLevel.Debug);
    }

    /// <summary>
    /// 保存配置
    /// </summary>
    public static void SaveConfig()
    {
        ModHelper.WriteConfig(Config);
    }

    /// <summary>
    /// 当游戏请求加载任何资源时触发。
    /// 这里提供虚拟祭坛贴图，并将祭坛地图补丁覆盖到 Maps/Mine。
    /// </summary>
    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        // --- 任务 1: 加载贴图 (Action: Load) ---
        // 判断请求的资源名称是否匹配我们定义的虚拟路径
        if (e.Name.IsEquivalentTo(TilesheetVirtualPath))
        {
            // 使用 SMAPI 的 LoadFromModFile API 加载本地 png
            e.LoadFromModFile<Texture2D>(LocalTexturePath, AssetLoadPriority.Medium);
        }

        // --- 任务 2: 编辑地图 (Action: EditMap) ---
        // 判断请求的资源是否是矿井地图
        if (e.Name.IsEquivalentTo("Maps/Mine"))
        {
            // 配置关闭时不修改原版矿井地图。
            if (Config.EnableAltar)
            {
                // 请求 SMAPI 编辑该资源
                e.Edit(asset =>
                {
                    // 获取地图编辑器接口
                    var editor = asset.AsMap();

                    // 执行具体的补丁应用逻辑
                    ApplyAltarPatch(editor);
                });
            }
        }
    }

    /// <summary>
    /// 将本地的 altar2.tmx 补丁应用到游戏原版地图上
    /// </summary>
    /// <param name="editor">SMAPI 提供的地图编辑器接口</param>
    private void ApplyAltarPatch(IAssetDataForMap editor)
    {
        try
        {
            // 1. 加载本地的 TMX 地图文件
            // 注意：这里我们加载它作为一个独立的 Map 对象
            Map sourceMap = Helper.ModContent.Load<Map>(LocalMapPath);

            // 2. 【关键步骤】修复贴图引用 (Re-linking Tilesheets)
            // altar2.tmx 内部写着 <image source="altar4.png"/>
            // 我们必须遍历这个源地图的所有图块集，找到引用 altar4.png 的那个，
            // 并将其 ImageSource 修改为我们的虚拟路径。
            foreach (var tilesheet in sourceMap.TileSheets)
            {
                // 检查 ImageSource 是否包含文件名（路径分隔符可能不同，所以用 EndsWith 或 Contains）
                if (tilesheet.ImageSource.Contains("altar4.png"))
                {
                    // 修改指向，指向内存中的虚拟资产
                    // 这样当游戏渲染这个地图时，会去请求 TilesheetVirtualPath，
                    // 从而触发 OnAssetRequested 中的第一个 if 分支。
                    tilesheet.ImageSource = TilesheetVirtualPath;
                }
            }

            // 3. 定义覆盖区域
            // 祭坛地图当前覆盖到目标地图的 (19, 2) 位置，大小为 2×3。
            Rectangle targetArea = new Rectangle(19, 2, 2, 3);

            // 4. 执行合并 (PatchMap)
            // SMAPI 的 PatchMap 方法会自动处理图层合并（Layer Merging）。
            // 它会将 sourceMap 的 "Front" 层覆盖到 targetMap 的 "Front" 层，依此类推。
            editor.PatchMap(sourceMap, targetArea: targetArea);
        }
        catch (Exception ex)
        {
            ModMonitor.Log($"应用地图补丁时发生错误: {ex.Message}", LogLevel.Error);
        }
    }
}

