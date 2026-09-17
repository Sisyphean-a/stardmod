using HarmonyLib;
using StardewValley;
using StardewValley.Menus;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace FishingBarGrowth;

/// <summary>
/// BobberBar类的Harmony补丁
/// </summary>
[HarmonyPatch]
public static class BobberBarPatch
{
    private static ModConfig? _config;
    private static Action<string, bool>? _logDebug;
    private static readonly ConditionalWeakTable<BobberBar, AppliedGrowth> AppliedGrowthByBar = new();

    private sealed class AppliedGrowth
    {
        public int Pixels { get; }

        public AppliedGrowth(int pixels)
        {
            Pixels = pixels;
        }
    }

    // 保存最后一次钓鱼的统计数据,供HUD使用
    public static int LastBaseHeight { get; private set; } = 0;
    public static int LastBonusPixels { get; private set; } = 0;
    public static int LastFinalHeight { get; private set; } = 0;
    public static bool HasFishingData { get; private set; } = false;

    /// <summary>
    /// 初始化补丁配置
    /// </summary>
    public static void Initialize(ModConfig config, Action<string, bool> logDebug)
    {
        _config = config;
        _logDebug = logDebug;
    }

    /// <summary>
    /// 手动指定要补丁的目标方法
    /// </summary>
    [HarmonyTargetMethod]
    public static MethodBase TargetMethod()
    {
        // 获取BobberBar的所有构造函数
        var constructors = typeof(BobberBar).GetConstructors();

        // 返回第一个构造函数(通常BobberBar只有一个公共构造函数)
        return constructors[0];
    }

    /// <summary>
    /// BobberBar构造函数的Postfix补丁
    /// 在游戏计算完钓鱼条高度后,添加基于捕获数量的额外高度
    /// </summary>
    [HarmonyPostfix]
    public static void Constructor_Postfix(BobberBar __instance)
    {
        try
        {
            // 检查配置和玩家
            if (_config == null || !_config.EnableMod || Game1.player == null)
                return;

            // 统计有效鱼类总数 (传递调试标志)
            int totalFish = FishCounter.GetTotalFishCount(_config.ExcludeAlgae, _config.ShowDebugInfo);

            // 计算额外像素
            int bonusPixels = FishCounter.CalculateBonusPixels(totalFish, _config.FishPerPixel);

            if (bonusPixels <= 0)
                return;

            // 使用反射获取私有字段 bobberBarHeight
            FieldInfo? heightField = AccessTools.Field(typeof(BobberBar), "bobberBarHeight");

            if (heightField == null)
            {
                _logDebug?.Invoke("无法找到 bobberBarHeight 字段!", true);
                return;
            }

            // 获取当前高度(这是游戏根据等级、装备等计算出的基础高度)
            int baseHeight = (int)heightField.GetValue(__instance)!;
            int newHeight = baseHeight + bonusPixels;

            // 应用最大高度限制
            if (_config.MaxBarHeight > 0 && newHeight > _config.MaxBarHeight)
            {
                newHeight = _config.MaxBarHeight;
            }

            // Rule: treasure mechanics exclude only the height actually added by this mod.
            AppliedGrowthByBar.Add(__instance, new AppliedGrowth(Math.Max(0, newHeight - baseHeight)));

            // 保存统计数据供HUD使用
            LastBaseHeight = baseHeight;
            LastBonusPixels = bonusPixels;
            LastFinalHeight = newHeight;
            HasFishingData = true;

            // 设置新高度
            heightField.SetValue(__instance, newHeight);

            // 输出调试信息
            if (_config.ShowDebugInfo)
            {
                _logDebug?.Invoke(
                    $"钓鱼条高度已修改: 基础={baseHeight}px (等级+装备), 奖励={bonusPixels}px (总鱼数={totalFish}), 最终={newHeight}px",
                    false
                );
            }
        }
        catch (Exception ex)
        {
            _logDebug?.Invoke($"应用钓鱼条补丁时出错: {ex.Message}", true);
        }
    }

    public static int GetTreasureBarHeight(BobberBar bobberBar)
    {
        return AppliedGrowthByBar.TryGetValue(bobberBar, out AppliedGrowth? growth)
            ? Math.Max(0, bobberBar.bobberBarHeight - growth.Pixels)
            : bobberBar.bobberBarHeight;
    }

    [HarmonyPatch(typeof(BobberBar), nameof(BobberBar.update))]
    private static class TreasureBarPatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> KeepTreasureBarAtOriginalHeight(
            IEnumerable<CodeInstruction> instructions)
        {
            FieldInfo treasureAppearTimerField = AccessTools.Field(typeof(BobberBar), nameof(BobberBar.treasureAppearTimer));
            FieldInfo treasureCatchLevelField = AccessTools.Field(typeof(BobberBar), nameof(BobberBar.treasureCatchLevel));
            FieldInfo bobberBarHeightField = AccessTools.Field(typeof(BobberBar), nameof(BobberBar.bobberBarHeight));
            MethodInfo getTreasureBarHeightMethod = AccessTools.Method(
                typeof(BobberBarPatch),
                nameof(GetTreasureBarHeight)
            );

            bool inTreasureLogic = false;
            int replacements = 0;
            var result = new List<CodeInstruction>();

            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.LoadsField(treasureAppearTimerField))
                    inTreasureLogic = true;

                if (inTreasureLogic && instruction.LoadsField(bobberBarHeightField))
                {
                    var replacement = new CodeInstruction(OpCodes.Call, getTreasureBarHeightMethod);
                    replacement.labels.AddRange(instruction.labels);
                    replacement.blocks.AddRange(instruction.blocks);
                    result.Add(replacement);
                    replacements++;
                }
                else
                {
                    result.Add(instruction);
                }

                if (instruction.LoadsField(treasureCatchLevelField))
                    inTreasureLogic = false;
            }

            if (replacements != 2)
            {
                throw new InvalidOperationException(
                    $"宝藏条兼容补丁预期替换2处高度读取，实际找到{replacements}处。"
                );
            }

            return result;
        }
    }
}

