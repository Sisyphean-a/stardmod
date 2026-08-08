using System.Reflection;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;

namespace HotkeyViewer;

public sealed class ModEntry : Mod
{
    private ModConfig config = null!;
    private HotkeyCatalog catalog = null!;

    public override void Entry(IModHelper helper)
    {
        config = helper.ReadConfig<ModConfig>();
        catalog = new HotkeyCatalog(helper, Monitor);

        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.Events.Input.ButtonPressed += OnButtonPressed;
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        object? api = Helper.ModRegistry.GetApi("spacechase0.GenericModConfigMenu");
        if (api is null)
            return;

        try
        {
            InvokeGmcm(
                api,
                "Register",
                ModManifest,
                (Action)(() => config = new ModConfig()),
                (Action)(() => Helper.WriteConfig(config)),
                true);
            InvokeGmcm(
                api,
                "AddKeybindList",
                ModManifest,
                (Func<KeybindList>)(() => config.OpenMenuKey),
                (Action<KeybindList>)(value => config.OpenMenuKey = value),
                (Func<string>)(() => "打开快捷键查看器"),
                (Func<string>)(() => "默认是 ? 键（OemQuestion）。如果它和聊天键冲突，可以在这里改。"),
                nameof(ModConfig.OpenMenuKey));
        }
        catch (Exception ex)
        {
            Monitor.Log($"注册 GMCM 配置菜单失败：{ex.GetBaseException().Message}", LogLevel.Warn);
        }
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (e.IsSuppressed() || !config.OpenMenuKey.JustPressed())
            return;

        if (Game1.activeClickableMenu is HotkeyViewerMenu currentMenu)
        {
            currentMenu.exitThisMenu();
            Helper.Input.SuppressActiveKeybinds(config.OpenMenuKey);
            return;
        }

        if (Game1.activeClickableMenu is not null || Game1.eventUp || Game1.dialogueUp)
            return;

        Game1.activeClickableMenu = new HotkeyViewerMenu(catalog);
        Helper.Input.SuppressActiveKeybinds(config.OpenMenuKey);
        Game1.playSound("bigSelect");
    }

    private static void InvokeGmcm(object api, string methodName, params object?[] args)
    {
        MethodInfo? method = api.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(method => method.Name == methodName && method.GetParameters().Length == args.Length);
        if (method is null)
            throw new MissingMethodException(api.GetType().FullName, methodName);

        method.Invoke(api, args);
    }
}
