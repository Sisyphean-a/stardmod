using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;

namespace HotkeyViewer;

internal sealed class HotkeyCatalog
{
    private static readonly StringComparer TextComparer = StringComparer.OrdinalIgnoreCase;
    private readonly IModHelper helper;
    private readonly IMonitor monitor;
    private Dictionary<string, string>? modDirectoryIndex;

    internal HotkeyCatalog(IModHelper helper, IMonitor monitor)
    {
        this.helper = helper;
        this.monitor = monitor;
    }

    internal HotkeyCatalogResult Build(bool refreshDirectories = false)
    {
        List<string> warnings = new();
        HashSet<string> gmcmFields = new(TextComparer);
        List<HotkeyEntry> entries = new();

        entries.AddRange(CollectGameControls());
        entries.AddRange(CollectGenericModConfigMenuEntries(warnings, gmcmFields));
        entries.AddRange(CollectConfigEntries(warnings, gmcmFields, refreshDirectories));

        entries = Deduplicate(entries)
            .OrderBy(entry => entry.Source == HotkeySource.Game ? 0 : 1)
            .ThenBy(entry => entry.OwnerName, TextComparer)
            .ThenBy(entry => entry.Action, TextComparer)
            .ToList();

        Dictionary<string, int> bindingUseCounts = entries
            .SelectMany(entry => entry.Bindings.Select(binding => new
            {
                binding.Normalized,
                OwnerAction = $"{entry.OwnerId}|{entry.Action}"
            }))
            .GroupBy(item => item.Normalized, TextComparer)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.OwnerAction).Distinct(TextComparer).Count(),
                TextComparer);

        return new HotkeyCatalogResult(entries, bindingUseCounts, warnings);
    }

    private static IEnumerable<HotkeyEntry> CollectGameControls()
    {
        Options options = Game1.options;
        if (options is null)
            yield break;

        foreach ((string action, string field, InputButton[] buttons) in new (string, string, InputButton[])[]
        {
            ("行动 / 交互", nameof(options.actionButton), options.actionButton),
            ("取消 / 返回", nameof(options.cancelButton), options.cancelButton),
            ("使用工具", nameof(options.useToolButton), options.useToolButton),
            ("向上移动", nameof(options.moveUpButton), options.moveUpButton),
            ("向右移动", nameof(options.moveRightButton), options.moveRightButton),
            ("向下移动", nameof(options.moveDownButton), options.moveDownButton),
            ("向左移动", nameof(options.moveLeftButton), options.moveLeftButton),
            ("打开菜单", nameof(options.menuButton), options.menuButton),
            ("跑步", nameof(options.runButton), options.runButton),
            ("聊天", nameof(options.chatButton), options.chatButton),
            ("打开地图", nameof(options.mapButton), options.mapButton),
            ("打开日志", nameof(options.journalButton), options.journalButton),
            ("工具栏切换", nameof(options.toolbarSwap), options.toolbarSwap),
            ("表情菜单", nameof(options.emoteButton), options.emoteButton),
            ("快捷栏 1", nameof(options.inventorySlot1), options.inventorySlot1),
            ("快捷栏 2", nameof(options.inventorySlot2), options.inventorySlot2),
            ("快捷栏 3", nameof(options.inventorySlot3), options.inventorySlot3),
            ("快捷栏 4", nameof(options.inventorySlot4), options.inventorySlot4),
            ("快捷栏 5", nameof(options.inventorySlot5), options.inventorySlot5),
            ("快捷栏 6", nameof(options.inventorySlot6), options.inventorySlot6),
            ("快捷栏 7", nameof(options.inventorySlot7), options.inventorySlot7),
            ("快捷栏 8", nameof(options.inventorySlot8), options.inventorySlot8),
            ("快捷栏 9", nameof(options.inventorySlot9), options.inventorySlot9),
            ("快捷栏 10", nameof(options.inventorySlot10), options.inventorySlot10),
            ("快捷栏 11", nameof(options.inventorySlot11), options.inventorySlot11),
            ("快捷栏 12", nameof(options.inventorySlot12), options.inventorySlot12)
        })
        {
            List<HotkeyBinding> bindings = GetBindings(buttons).ToList();
            if (bindings.Count == 0)
                continue;

            yield return new HotkeyEntry(action, "星露谷物语", "StardewValley", HotkeySource.Game, bindings, field);
        }
    }

    private IEnumerable<HotkeyEntry> CollectGenericModConfigMenuEntries(List<string> warnings, HashSet<string> gmcmFields)
    {
        object? api = helper.ModRegistry.GetApi("spacechase0.GenericModConfigMenu");
        if (api is null)
            yield break;

        FieldInfo? configManagerField = api.GetType().GetField("ConfigManager", BindingFlags.Instance | BindingFlags.NonPublic);
        object? configManager = configManagerField?.GetValue(api);
        MethodInfo? getAllMethod = configManager?.GetType().GetMethod("GetAll", BindingFlags.Instance | BindingFlags.Public);
        if (configManager is null || getAllMethod is null)
        {
            warnings.Add("GMCM 已加载，但当前版本没有暴露可读取的注册项；模组快捷键将主要来自 config.json 推测。");
            yield break;
        }

        IEnumerable? modConfigs;
        try
        {
            modConfigs = getAllMethod.Invoke(configManager, null) as IEnumerable;
        }
        catch (Exception ex)
        {
            warnings.Add($"读取 GMCM 注册项失败：{ex.GetBaseException().Message}");
            yield break;
        }

        if (modConfigs is null)
            yield break;

        foreach (object modConfig in modConfigs)
        {
            IManifest? manifest = GetProperty<IManifest>(modConfig, "ModManifest");
            MethodInfo? getOptionsMethod = modConfig.GetType().GetMethod("GetAllOptions", BindingFlags.Instance | BindingFlags.Public);
            if (manifest is null || getOptionsMethod is null)
                continue;

            IEnumerable? options;
            try
            {
                options = getOptionsMethod.Invoke(modConfig, null) as IEnumerable;
            }
            catch (Exception ex)
            {
                warnings.Add($"读取 {manifest.Name} 的 GMCM 选项失败：{ex.GetBaseException().Message}");
                continue;
            }

            if (options is null)
                continue;

            foreach (object option in options)
            {
                Type? valueType = GetProperty<Type>(option, "Type");
                if (valueType != typeof(SButton) && valueType != typeof(KeybindList))
                    continue;

                string fieldId = GetProperty<string>(option, "FieldId") ?? "";
                string action = GetOptionName(option, fieldId);
                object? value = GetProperty<object>(option, "Value");
                string rawValue = value?.ToString() ?? "";
                if (!TryParseBindings(rawValue, out List<HotkeyBinding> bindings))
                    continue;

                if (!string.IsNullOrWhiteSpace(fieldId))
                    gmcmFields.Add(GetFieldKey(manifest.UniqueID, fieldId));

                yield return new HotkeyEntry(action, manifest.Name, manifest.UniqueID, HotkeySource.GenericModConfigMenu, bindings, fieldId);
            }
        }
    }

    private IEnumerable<HotkeyEntry> CollectConfigEntries(
        List<string> warnings,
        HashSet<string> gmcmFields,
        bool refreshDirectories)
    {
        // Config files are still read every build; only manifest path discovery is cached until Refresh.
        Dictionary<string, string> modDirectories = refreshDirectories || modDirectoryIndex is null
            ? modDirectoryIndex = BuildModDirectoryIndex(warnings)
            : modDirectoryIndex;

        foreach (IModInfo mod in helper.ModRegistry.GetAll().OrderBy(mod => mod.Manifest.Name, TextComparer))
        {
            IManifest manifest = mod.Manifest;
            if (!modDirectories.TryGetValue(manifest.UniqueID, out string? directory))
                continue;

            string configPath = Path.Combine(directory, "config.json");
            if (!File.Exists(configPath))
                continue;

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(File.ReadAllText(configPath));
            }
            catch (Exception ex)
            {
                string message = $"无法读取 {manifest.Name} 的 config.json：{ex.Message}";
                warnings.Add(message);
                monitor.Log(message, LogLevel.Warn);
                continue;
            }

            using (document)
            {
                foreach ((string fieldPath, string rawValue) in EnumerateConfigKeybindCandidates(document.RootElement))
                {
                    if (IsCoveredByGmcm(manifest.UniqueID, fieldPath, gmcmFields))
                        continue;
                    if (!TryParseBindings(rawValue, out List<HotkeyBinding> bindings))
                        continue;

                    yield return new HotkeyEntry(
                        HumanizeConfigPath(fieldPath),
                        manifest.Name,
                        manifest.UniqueID,
                        HotkeySource.ConfigGuess,
                        bindings,
                        fieldPath);
                }
            }
        }
    }

    private Dictionary<string, string> BuildModDirectoryIndex(List<string> warnings)
    {
        DirectoryInfo? modsRoot = Directory.GetParent(helper.DirectoryPath);
        Dictionary<string, string> result = new(TextComparer);
        if (modsRoot is null || !modsRoot.Exists)
            return result;

        EnumerationOptions options = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        foreach (string manifestPath in Directory.EnumerateFiles(modsRoot.FullName, "manifest.json", options))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
                if (document.RootElement.TryGetProperty("UniqueID", out JsonElement idElement)
                    && idElement.ValueKind == JsonValueKind.String)
                {
                    string? uniqueId = idElement.GetString();
                    if (!string.IsNullOrWhiteSpace(uniqueId))
                        result[uniqueId] = Path.GetDirectoryName(manifestPath)!;
                }
            }
            catch (Exception ex)
            {
                string message = $"无法读取 manifest：{manifestPath}（{ex.Message}）";
                warnings.Add(message);
                monitor.Log(message, LogLevel.Warn);
            }
        }

        return result;
    }

    private static IEnumerable<(string FieldPath, string RawValue)> EnumerateConfigKeybindCandidates(JsonElement element, string path = "", bool parentIsHotkeyContainer = false)
    {
        if (element.ValueKind != JsonValueKind.Object)
            yield break;

        foreach (JsonProperty property in element.EnumerateObject())
        {
            string fieldPath = string.IsNullOrWhiteSpace(path) ? property.Name : $"{path}.{property.Name}";
            if (IsSensitivePath(fieldPath))
                continue;

            bool isHotkeyField = parentIsHotkeyContainer || IsHotkeyFieldName(property.Name);
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                string rawValue = property.Value.GetString() ?? "";
                if (isHotkeyField)
                    yield return (fieldPath, rawValue);
            }
            else if (property.Value.ValueKind == JsonValueKind.Object)
            {
                foreach ((string nestedPath, string rawValue) in EnumerateConfigKeybindCandidates(property.Value, fieldPath, isHotkeyField))
                    yield return (nestedPath, rawValue);
            }
            else if (property.Value.ValueKind == JsonValueKind.Array && isHotkeyField)
            {
                foreach (JsonElement item in property.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                        yield return (fieldPath, item.GetString() ?? "");
                }
            }
        }
    }

    private static bool TryParseBindings(string rawValue, out List<HotkeyBinding> bindings)
    {
        bindings = new List<HotkeyBinding>();
        rawValue = rawValue.Trim();
        if (string.IsNullOrWhiteSpace(rawValue) || rawValue.Equals("None", StringComparison.OrdinalIgnoreCase))
            return false;

        if (KeybindList.TryParse(rawValue, out KeybindList? parsed, out string[] errors) && parsed is not null && parsed.IsBound)
        {
            foreach (Keybind keybind in parsed.Keybinds)
            {
                if (!keybind.IsBound)
                    continue;

                string display = keybind.ToString();
                bindings.Add(new HotkeyBinding(display, NormalizeKeybind(display)));
            }
        }
        else if (Enum.TryParse(rawValue, true, out SButton button) && button != SButton.None)
        {
            string display = button.ToString();
            bindings.Add(new HotkeyBinding(display, NormalizeKeybind(display)));
        }

        bindings = bindings
            .Where(binding => !IsControllerBinding(binding))
            .GroupBy(binding => binding.Normalized, TextComparer)
            .Select(group => group.First())
            .ToList();

        return bindings.Count > 0;
    }

    private static IEnumerable<HotkeyBinding> GetBindings(IEnumerable<InputButton> buttons)
    {
        foreach (InputButton button in buttons)
        {
            string? display = null;
            if (button.mouseLeft)
                display = SButton.MouseLeft.ToString();
            else if (button.mouseRight)
                display = SButton.MouseRight.ToString();
            else if (button.key != Keys.None)
                display = button.key.ToString();

            if (!string.IsNullOrWhiteSpace(display))
                yield return new HotkeyBinding(display, NormalizeKeybind(display));
        }
    }

    private static string NormalizeKeybind(string display)
    {
        return string.Join(
            "+",
            display.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(button => button.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant())
                .OrderBy(button => button, StringComparer.Ordinal));
    }

    private static bool IsControllerBinding(HotkeyBinding binding)
    {
        return binding.Normalized.Split('+').Any(button =>
            button.StartsWith("CONTROLLER", StringComparison.OrdinalIgnoreCase)
            || button.StartsWith("DPAD", StringComparison.OrdinalIgnoreCase)
            || button.StartsWith("CHATPAD", StringComparison.OrdinalIgnoreCase)
            || button is "BIGBUTTON" or "LEFTTRIGGER" or "RIGHTTRIGGER" or "LEFTSTICK" or "RIGHTSTICK" or "LEFTSHOULDER" or "RIGHTSHOULDER"
            || button.StartsWith("LEFTTHUMBSTICK", StringComparison.OrdinalIgnoreCase)
            || button.StartsWith("RIGHTTHUMBSTICK", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsHotkeyFieldName(string name)
    {
        string normalized = name.ToLowerInvariant();
        return normalized is "controls" or "control"
            || normalized.Contains("keybind")
            || normalized.Contains("hotkey")
            || normalized.Contains("shortcut")
            || normalized.EndsWith("key")
            || normalized.EndsWith("keys")
            || normalized.EndsWith("button")
            || normalized.EndsWith("buttons")
            || normalized.Contains("快捷")
            || normalized.Contains("按键")
            || normalized.Contains("按钮");
    }

    private static bool IsSensitivePath(string path)
    {
        string compact = new(path.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        return compact.Contains("apikey")
            || compact.Contains("secret")
            || compact.Contains("token")
            || compact.Contains("password")
            || compact.Contains("credential")
            || compact.Contains("privatekey")
            || compact.Contains("authkey");
    }

    private static string HumanizeConfigPath(string fieldPath)
    {
        string label = fieldPath
            .Replace("KeybindList", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Keybind", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Hotkey", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Keyboard", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Controller", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Button", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Controls.", "", StringComparison.OrdinalIgnoreCase)
            .Trim('.', ' ', '_', '-');
        return string.IsNullOrWhiteSpace(label) ? fieldPath : label;
    }

    private static bool IsCoveredByGmcm(string ownerId, string fieldPath, HashSet<string> gmcmFields)
    {
        if (gmcmFields.Contains(GetFieldKey(ownerId, fieldPath)))
            return true;

        int lastDot = fieldPath.LastIndexOf('.');
        string lastSegment = lastDot >= 0 ? fieldPath[(lastDot + 1)..] : fieldPath;
        return gmcmFields.Contains(GetFieldKey(ownerId, lastSegment));
    }

    private static string GetFieldKey(string ownerId, string fieldPath)
    {
        return $"{ownerId}|{fieldPath}";
    }

    private static List<HotkeyEntry> Deduplicate(IEnumerable<HotkeyEntry> entries)
    {
        Dictionary<string, HotkeyEntry> result = new(TextComparer);
        foreach (HotkeyEntry entry in entries)
        {
            string key = $"{entry.OwnerId}|{string.Join(',', entry.Bindings.Select(binding => binding.Normalized).OrderBy(value => value, TextComparer))}|{entry.Action}";
            if (!result.TryGetValue(key, out HotkeyEntry? existing) || IsBetterSource(entry.Source, existing.Source))
                result[key] = entry;
        }

        return result.Values.ToList();
    }

    private static bool IsBetterSource(HotkeySource candidate, HotkeySource current)
    {
        return SourceRank(candidate) < SourceRank(current);
    }

    private static int SourceRank(HotkeySource source)
    {
        return source switch
        {
            HotkeySource.Game => 0,
            HotkeySource.GenericModConfigMenu => 1,
            HotkeySource.ConfigGuess => 2,
            _ => 3
        };
    }

    private static T? GetProperty<T>(object target, string propertyName)
    {
        object? value = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(target);
        return value is T typed ? typed : default;
    }

    private static string GetOptionName(object option, string fieldId)
    {
        Func<string>? name = GetProperty<Func<string>>(option, "Name");
        if (name is null)
            return string.IsNullOrWhiteSpace(fieldId) ? "未命名快捷键" : fieldId;

        try
        {
            string value = name();
            return string.IsNullOrWhiteSpace(value) ? fieldId : value;
        }
        catch
        {
            return string.IsNullOrWhiteSpace(fieldId) ? "未命名快捷键" : fieldId;
        }
    }
}
