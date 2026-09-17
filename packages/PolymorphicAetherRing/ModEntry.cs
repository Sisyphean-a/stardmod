using System.Text;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using PolymorphicAetherRing.Framework;

namespace PolymorphicAetherRing;

/// <summary>模组入口类</summary>
public class ModEntry : Mod
{
    /// <summary>戒指的物品ID</summary>
    public const string RingId = "xixifu.AetherRing";

    /// <summary>完整物品ID（用于物品注册表）</summary>
    public const string QualifiedRingId = "(O)" + RingId;

    /// <summary>战斗管理器</summary>
    private RingCombatManager? _combatManager;

    private static ITranslationHelper? _translations;

    /// <summary>配置项</summary>
    public ModConfig Config { get; private set; } = new();

    /// <summary>是否为安卓平台</summary>
    private bool IsAndroid => Constants.TargetPlatform == GamePlatform.Android;

    /// <summary>当前视口是否无法容纳桌面熔铸界面</summary>
    private bool UseCompactFusionMenu => IsAndroid
        || Game1.uiViewport.Width < 1064
        || Game1.uiViewport.Height < 768;

    /// <summary>安卓端长按计时器（ticks）</summary>
    private int _longPressHoldTicks = 0;

    /// <summary>长按是否已触发（防止重复触发）</summary>
    private bool _longPressTriggered = false;

    public override void Entry(IModHelper helper)
    {
        // 1. 读取配置
        Config = helper.ReadConfig<ModConfig>();
        Config.Validate();
        _translations = helper.Translation;

        var harmony = new Harmony(ModManifest.UniqueID);
        harmony.Patch(
            original: AccessTools.Method(typeof(StardewValley.Objects.Ring), nameof(StardewValley.Objects.Ring.getDescription)),
            prefix: new HarmonyMethod(typeof(ModEntry), nameof(LocalizeAetherRingDisplayFields)),
            postfix: new HarmonyMethod(typeof(ModEntry), nameof(AppendFusedWeaponTooltipDescription)));
        harmony.Patch(
            original: AccessTools.PropertyGetter(typeof(StardewValley.Objects.Ring), nameof(StardewValley.Objects.Ring.DisplayName)),
            postfix: new HarmonyMethod(typeof(ModEntry), nameof(LocalizeAetherRingDisplayName)));
        harmony.Patch(
            original: AccessTools.Method(typeof(StardewValley.Objects.Ring), nameof(StardewValley.Objects.Ring.drawTooltip)),
            prefix: new HarmonyMethod(typeof(ModEntry), nameof(LocalizeAetherRingDisplayFields)),
            postfix: new HarmonyMethod(typeof(ModEntry), nameof(DrawFusedWeaponTooltip)));

        // 2. 注册事件
        helper.Events.Content.AssetRequested += OnAssetRequested;
        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        helper.Events.Input.ButtonPressed += OnButtonPressed;
        helper.Events.Input.ButtonReleased += OnButtonReleased;

        Monitor.Log("Polymorphic Aether Ring mod loaded!", LogLevel.Info);
    }

    /// <summary>游戏启动时注册GMCM</summary>
    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        // 获取 GMCM API
        var configMenu = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (configMenu == null)
        {
            Monitor.Log("Generic Mod Config Menu not found or API mismatch.", LogLevel.Debug);
            return;
        }

        // 注册模组配置
        configMenu.Register(
            mod: ModManifest,
            reset: Config.ResetToDefaults,
            save: () =>
            {
                Config.Validate();
                Helper.WriteConfig(Config);
            }
        );

        // 添加配置项
        configMenu.AddSectionTitle(ModManifest, () => Helper.Translation.Get("config.combat_settings"));

        configMenu.AddNumberOption(
            mod: ModManifest,
            name: () => Helper.Translation.Get("config.damage_multiplier"),
            tooltip: () => Helper.Translation.Get("config.damage_multiplier.tooltip"),
            getValue: () => Config.DamageMultiplier,
            setValue: value => Config.DamageMultiplier = value,
            min: ModConfig.MinimumDamageMultiplier,
            max: ModConfig.MaximumDamageMultiplier,
            interval: 0.1f
        );

        configMenu.AddNumberOption(
            mod: ModManifest,
            name: () => Helper.Translation.Get("config.range_multiplier"),
            tooltip: () => Helper.Translation.Get("config.range_multiplier.tooltip"),
            getValue: () => Config.RangeMultiplier,
            setValue: value => Config.RangeMultiplier = value,
            min: ModConfig.MinimumRangeMultiplier,
            max: ModConfig.MaximumRangeMultiplier,
            interval: 0.1f
        );

        configMenu.AddNumberOption(
            mod: ModManifest,
            name: () => Helper.Translation.Get("config.cooldown_multiplier"),
            tooltip: () => Helper.Translation.Get("config.cooldown_multiplier.tooltip"),
            getValue: () => Config.CooldownMultiplier,
            setValue: value => Config.CooldownMultiplier = value,
            min: ModConfig.MinimumCooldownMultiplier,
            max: ModConfig.MaximumCooldownMultiplier,
            interval: 0.1f
        );

        configMenu.AddSectionTitle(ModManifest, () => Helper.Translation.Get("config.fusion_settings"));

        configMenu.AddBoolOption(
            mod: ModManifest,
            name: () => Helper.Translation.Get("config.return_fused_weapon"),
            tooltip: () => Helper.Translation.Get("config.return_fused_weapon.tooltip"),
            getValue: () => Config.ReturnFusedWeapon,
            setValue: value => Config.ReturnFusedWeapon = value
        );

        configMenu.AddSectionTitle(ModManifest, () => Helper.Translation.Get("config.android_settings"));

        configMenu.AddNumberOption(
            mod: ModManifest,
            name: () => Helper.Translation.Get("config.android_long_press_ms"),
            tooltip: () => Helper.Translation.Get("config.android_long_press_ms.tooltip"),
            getValue: () => Config.AndroidLongPressMs,
            setValue: value => Config.AndroidLongPressMs = (int)value,
            min: ModConfig.MinimumAndroidLongPressMs,
            max: ModConfig.MaximumAndroidLongPressMs,
            interval: 50
        );
    }

    private static void LocalizeAetherRingDisplayFields(StardewValley.Objects.Ring __instance)
    {
        if (__instance.QualifiedItemId != QualifiedRingId)
            return;

        ITranslationHelper translations = GetTranslations();
        __instance.displayName = translations.Get("item.ring.name").ToString();
        __instance.description = translations.Get("item.ring.description").ToString();
    }

    private static void LocalizeAetherRingDisplayName(
        StardewValley.Objects.Ring __instance,
        ref string __result)
    {
        if (__instance.QualifiedItemId != QualifiedRingId)
            return;

        __result = GetTranslations().Get("item.ring.name").ToString();
    }

    private static void AppendFusedWeaponTooltipDescription(
        StardewValley.Objects.Ring __instance,
        ref string __result)
    {
        if (__instance.QualifiedItemId != QualifiedRingId)
            return;

        __result = FusedWeaponTooltip.AppendToDescription(
            __instance,
            __result,
            GetTranslations());
    }

    private static void DrawFusedWeaponTooltip(
        StardewValley.Objects.Ring __instance,
        SpriteBatch spriteBatch,
        ref int x,
        ref int y,
        SpriteFont font,
        float alpha,
        StringBuilder overrideText)
    {
        if (__instance.QualifiedItemId != QualifiedRingId)
            return;

        string details = FusedWeaponTooltip.GetDetails(__instance, GetTranslations());
        string wrappedDetails = Game1.parseText(
            details,
            Game1.smallFont,
            GetTooltipWidth(__instance));
        Utility.drawTextWithShadow(
            spriteBatch,
            wrappedDetails,
            font,
            new Vector2(x + 16, y + 20),
            Game1.textColor * alpha);
        y += (int)font.MeasureString(wrappedDetails).Y;
    }

    private static ITranslationHelper GetTranslations()
    {
        return _translations
            ?? throw new InvalidOperationException("Tooltip translations are not initialized.");
    }

    private static int GetTooltipWidth(Item item)
    {
        return Math.Max(
            272,
            (int)Game1.dialogueFont.MeasureString(item.DisplayName ?? string.Empty).X);
    }

    /// <summary>注册戒指数据资产</summary>
    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        // 注册戒指数据
        if (e.NameWithoutLocale.IsEquivalentTo("Data/Objects"))
        {
            e.Edit(asset =>
            {
                var data = asset.AsDictionary<string, StardewValley.GameData.Objects.ObjectData>();

                // 创建戒指数据
                var ringData = new StardewValley.GameData.Objects.ObjectData
                {
                    Name = RingId,
                    DisplayName = Helper.Translation.Get("item.ring.name"),
                    Description = Helper.Translation.Get("item.ring.description"),
                    Type = "Ring",
                    Category = StardewValley.Object.ringCategory,
                    Price = 5000,
                    Texture = Helper.ModContent.GetInternalAssetName("assets/trinket").Name,
                    SpriteIndex = 0
                };

                data.Data[RingId] = ringData;
                Monitor.Log($"Registered ring: {RingId}", LogLevel.Debug);
            });
        }

        // 注册本地化字符串（可选，已在ObjectData中定义）
        if (e.NameWithoutLocale.IsEquivalentTo("Strings/Objects"))
        {
            e.Edit(asset =>
            {
                var data = asset.AsDictionary<string, string>();
                data.Data["AetherRing_Name"] = Helper.Translation.Get("item.ring.name");
                data.Data["AetherRing_Description"] = Helper.Translation.Get("item.ring.description");
            });
        }
    }

    /// <summary>存档加载时赠送戒指</summary>
    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        // 传递 Config
        _combatManager = new RingCombatManager(Helper, Monitor, Config);

        var player = Game1.player;
        string mailFlag = "xixifu.AetherRing_Received";
        // ... (remaining gifting logic logic unchanged) ...

        // 如果已经收到过（有标记），直接返回
        if (player.hasOrWillReceiveMail(mailFlag))
        {
            return;
        }

        // 深度检查玩家是否已拥有戒指（包括组合戒指的情况）
        bool hasRing = IsRingOwned(player);

        if (hasRing)
        {
            // 补上标记
            player.mailReceived.Add(mailFlag);
            Monitor.Log("Legacy player detected (Checked deep storage): Added missing mail flag for Aether Ring.", LogLevel.Info);
        }
        else
        {
            // 真正的新玩家：赠送戒指并添加标记
            try
            {
                var ring = ItemRegistry.Create(QualifiedRingId);
                player.addItemByMenuIfNecessary(ring);
                player.mailReceived.Add(mailFlag);
                Monitor.Log("Granted Polymorphic Aether Ring to player.", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to create ring: {ex.Message}", LogLevel.Error);
            }
        }
    }

    /// <summary>检查玩家是否拥有戒指（递归检查组合戒指）</summary>
    private bool IsRingOwned(Farmer player)
    {
        // 1. 检查背包
        foreach (var item in player.Items)
        {
            if (item == null) continue;
            if (IsItemTargetRing(item)) return true;
        }

        // 2. 检查装备槽
        if (IsItemTargetRing(player.leftRing.Value)) return true;
        if (IsItemTargetRing(player.rightRing.Value)) return true;

        return false;
    }

    /// <summary>递归检查物品是否为目标戒指</summary>
    private bool IsItemTargetRing(Item? item)
    {
        if (item == null) return false;

        // 直接匹配
        if (item.QualifiedItemId == QualifiedRingId) return true;

        // 检查组合戒指
        if (item is StardewValley.Objects.CombinedRing combinedRing)
        {
            foreach (var child in combinedRing.combinedRings)
            {
                if (IsItemTargetRing(child)) return true;
            }
        }

        return false;
    }


    /// <summary>每帧更新战斗逻辑</summary>
    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        if (_combatManager == null)
        {
            // 防御性检查，理论上不应该发生
            if (e.IsMultipleOf(60)) Monitor.Log("_combatManager is null despite OnSaveLoaded!", LogLevel.Error);
            return;
        }

        _combatManager.Update();

        // 安卓端长按检测
        if (IsAndroid && Context.IsPlayerFree)
        {
            UpdateAndroidLongPress();
        }
    }

    /// <summary>安卓端长按检测逻辑</summary>
    private void UpdateAndroidLongPress()
    {
        // 检查是否按住触摸/左键
        bool isHolding = Helper.Input.IsDown(SButton.MouseLeft);

        if (!isHolding)
        {
            _longPressHoldTicks = 0;
            _longPressTriggered = false;
            return;
        }

        // 已触发则不再处理
        if (_longPressTriggered)
            return;

        // 检查是否持有戒指
        var player = Game1.player;
        var currentItem = player.CurrentItem;
        if (currentItem == null || currentItem.QualifiedItemId != QualifiedRingId)
        {
            _longPressHoldTicks = 0;
            return;
        }

        // 累计计时
        _longPressHoldTicks++;

        // 计算阈值（毫秒转tick，60 FPS）
        int thresholdTicks = (int)(Config.AndroidLongPressMs / 1000.0f * 60);

        if (_longPressHoldTicks >= thresholdTicks)
        {
            // 触发菜单
            _longPressTriggered = true;
            Helper.Input.Suppress(SButton.MouseLeft);
            Game1.activeClickableMenu = CreateFusionMenu(currentItem);
            Monitor.Log("Opened Mobile Fusion Menu (long press)", LogLevel.Debug);
        }
    }

    /// <summary>监听按键打开熔铸面板</summary>
    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady || !Context.IsPlayerFree)
            return;

        // 安卓端：左键由长按逻辑处理，这里不处理
        if (IsAndroid && e.Button == SButton.MouseLeft)
            return;

        // 检查是否按下左键或使用键
        if (e.Button != SButton.MouseLeft && e.Button != SButton.ControllerA)
            return;

        var player = Game1.player;
        var currentItem = player.CurrentItem;

        // 检查当前手持物品是否为我们的戒指
        if (currentItem == null)
            return;

        if (currentItem.QualifiedItemId != QualifiedRingId)
            return;

        // 拦截默认行为并按视口大小选择合适的熔铸界面
        Helper.Input.Suppress(e.Button);
        Game1.activeClickableMenu = CreateFusionMenu(currentItem);
        Monitor.Log("Opened Fusion Menu", LogLevel.Debug);
    }

    private IClickableMenu CreateFusionMenu(Item currentItem)
    {
        return UseCompactFusionMenu
            ? new MobileFusionMenu(currentItem, Helper, Monitor, Config)
            : new FusionMenu(currentItem, Helper, Monitor, Config);
    }

    /// <summary>监听按键释放（用于重置长按状态）</summary>
    private void OnButtonReleased(object? sender, ButtonReleasedEventArgs e)
    {
        if (e.Button == SButton.MouseLeft)
        {
            _longPressHoldTicks = 0;
            _longPressTriggered = false;
        }
    }
}
