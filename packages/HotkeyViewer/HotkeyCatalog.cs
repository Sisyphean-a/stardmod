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
    private readonly string? modsRootPath;
    private readonly HashSet<string> loggedWarnings = new(StringComparer.Ordinal);
    private Dictionary<string, string>? modDirectoryIndex;
    private IReadOnlyList<string> directoryWarnings = Array.Empty<string>();
    private Dictionary<string, ConfigFileCache> configCache = new(StringComparer.OrdinalIgnoreCase);
    private HotkeyCatalogResult catalogResult = EmptyResult;
    private Task<LoadResult>? loadTask;
    private CancellationTokenSource? loadCancellation;
    private object? gmcmApi;
    private GmcmReflectionCache? gmcmReflection;
    private int loadGeneration;
    private int revision;
    private bool isLoading;

    private static HotkeyCatalogResult EmptyResult { get; } = new(
        Array.Empty<HotkeyEntry>(),
        new Dictionary<string, int>(TextComparer),
        Array.Empty<string>());

    internal HotkeyCatalog(IModHelper helper, IMonitor monitor)
    {
        this.helper = helper;
        this.monitor = monitor;
        modsRootPath = Directory.GetParent(helper.DirectoryPath)?.FullName;
    }

    internal HotkeyCatalogResult CurrentResult => catalogResult;
    internal bool IsLoading => isLoading;
    internal int Revision => revision;

    // Flow: snapshot game/GMCM state on the main thread, scan only files in the worker,
    // then atomically publish one generation from PumpCompleted. A forced refresh ignores both caches.
    internal void BeginLoad(bool forceRefresh = false)
    {
        PumpCompleted();

        if (loadTask is not null && !loadTask.IsCompleted)
        {
            if (!forceRefresh)
                return;

            loadCancellation?.Cancel();
            loadCancellation?.Dispose();
            loadTask = null;
        }

        List<string> warnings = new();
        HashSet<string> gmcmFields = new(TextComparer);
        List<HotkeyEntry> baseEntries = new();
        baseEntries.AddRange(CollectGameControls());
        baseEntries.AddRange(CollectGenericModConfigMenuEntries(warnings, gmcmFields));

        List<ModSnapshot> mods = helper.ModRegistry.GetAll()
            .Select(mod => new ModSnapshot(mod.Manifest.UniqueID, mod.Manifest.Name))
            .OrderBy(mod => mod.Name, TextComparer)
            .ToList();
        Dictionary<string, string>? directorySnapshot = modDirectoryIndex is null
            ? null
            : new Dictionary<string, string>(modDirectoryIndex, TextComparer);
        IReadOnlyList<string> directoryWarningsSnapshot = directoryWarnings.ToList();
        Dictionary<string, ConfigFileCache> cacheSnapshot = new(configCache, StringComparer.OrdinalIgnoreCase);
        CancellationTokenSource cancellation = new();
        int generation = ++loadGeneration;
        loadCancellation = cancellation;
        isLoading = true;

        loadTask = Task.Run(
            () => LoadConfigFiles(
                generation,
                modsRootPath,
                mods,
                directorySnapshot,
                directoryWarningsSnapshot,
                cacheSnapshot,
                forceRefresh,
                cancellation.Token,
                baseEntries,
                warnings),
            cancellation.Token);
    }

    internal void PumpCompleted()
    {
        Task<LoadResult>? task = loadTask;
        if (task is null || !task.IsCompleted)
            return;

        loadTask = null;
        CancellationTokenSource? cancellation = loadCancellation;
        loadCancellation = null;

        if (task.IsCanceled)
        {
            cancellation?.Dispose();
            return;
        }

        LoadResult loaded;
        try
        {
            loaded = task.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            string message = $"后台读取快捷键配置失败：{ex.GetBaseException().Message}";
            LogWarning(message);
            catalogResult = new HotkeyCatalogResult(
                catalogResult.Entries,
                catalogResult.BindingUseCounts,
                catalogResult.Warnings.Concat(new[] { message }).ToList());
            isLoading = false;
            revision++;
            cancellation?.Dispose();
            return;
        }

        cancellation?.Dispose();
        if (loaded.Generation != loadGeneration)
            return;

        modDirectoryIndex = loaded.DirectoryIndex;
        directoryWarnings = loaded.DirectoryWarnings;
        configCache = loaded.ConfigCache;
        catalogResult = ComposeResult(loaded.BaseEntries, loaded.Mods, loaded.ConfigCache, loaded.Warnings);
        isLoading = false;
        revision++;

        foreach (string warning in catalogResult.Warnings)
            LogWarning(warning);
    }

    private HotkeyCatalogResult ComposeResult(
        IReadOnlyList<HotkeyEntry> baseEntries,
        IReadOnlyList<ModSnapshot> mods,
        IReadOnlyDictionary<string, ConfigFileCache> cache,
        IReadOnlyList<string> warnings)
    {
        List<HotkeyEntry> entries = new(baseEntries);
        HashSet<string> gmcmFields = new(
            baseEntries
                .Where(entry => entry.Source == HotkeySource.GenericModConfigMenu && !string.IsNullOrWhiteSpace(entry.Detail))
                .Select(entry => GetFieldKey(entry.OwnerId, entry.Detail)),
            TextComparer);

        foreach (ModSnapshot mod in mods)
        {
            if (modDirectoryIndex is null || !modDirectoryIndex.TryGetValue(mod.UniqueId, out string? directory))
                continue;

            string configPath = Path.Combine(directory, "config.json");
            if (!cache.TryGetValue(configPath, out ConfigFileCache? config))
                continue;

            foreach (ConfigCandidate candidate in config.Candidates)
            {
                if (IsCoveredByGmcm(mod.UniqueId, candidate.FieldPath, gmcmFields))
                    continue;
                if (!TryParseBindings(candidate.RawValue, out List<HotkeyBinding> bindings))
                    continue;

                entries.Add(new HotkeyEntry(
                    HumanizeConfigPath(candidate.FieldPath),
                    mod.Name,
                    mod.UniqueId,
                    HotkeySource.ConfigGuess,
                    bindings,
                    candidate.FieldPath));
            }
        }

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

        return new HotkeyCatalogResult(entries, bindingUseCounts, warnings.ToList());
    }

    private static LoadResult LoadConfigFiles(
        int generation,
        string? modsRootPath,
        IReadOnlyList<ModSnapshot> mods,
        IReadOnlyDictionary<string, string>? directorySnapshot,
        IReadOnlyList<string> cachedDirectoryWarnings,
        IReadOnlyDictionary<string, ConfigFileCache> cacheSnapshot,
        bool forceRefresh,
        CancellationToken cancellationToken,
        IReadOnlyList<HotkeyEntry> baseEntries,
        IReadOnlyList<string> initialWarnings)
    {
        List<string> warnings = new(initialWarnings);
        List<string> currentDirectoryWarnings = directorySnapshot is null || forceRefresh
            ? new()
            : new(cachedDirectoryWarnings);
        Dictionary<string, string> directories = directorySnapshot is null || forceRefresh
            ? BuildModDirectoryIndex(modsRootPath, currentDirectoryWarnings, cancellationToken)
            : directorySnapshot.ToDictionary(pair => pair.Key, pair => pair.Value, TextComparer);
        warnings.AddRange(currentDirectoryWarnings);
        Dictionary<string, ConfigFileCache> cache = new(StringComparer.OrdinalIgnoreCase);

        foreach (ModSnapshot mod in mods)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!directories.TryGetValue(mod.UniqueId, out string? directory))
                continue;

            string configPath = Path.Combine(directory, "config.json");
            if (!File.Exists(configPath))
                continue;

            try
            {
                FileInfo info = new(configPath);
                ConfigFileStamp stamp = new(info.Length, info.LastWriteTimeUtc);
                if (!forceRefresh
                    && cacheSnapshot.TryGetValue(configPath, out ConfigFileCache? previous)
                    && previous.Stamp == stamp)
                {
                    cache[configPath] = previous;
                    if (previous.Warning is not null)
                        warnings.Add(previous.Warning);
                    continue;
                }

                List<ConfigCandidate> candidates = ReadConfigCandidates(configPath, cancellationToken);
                cache[configPath] = new ConfigFileCache(stamp, candidates, null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                string message = $"无法读取 {mod.Name} 的 config.json：{ex.Message}";
                warnings.Add(message);
                cache[configPath] = new ConfigFileCache(
                    new ConfigFileStamp(0, DateTime.MinValue),
                    Array.Empty<ConfigCandidate>(),
                    message);
            }
        }

        return new LoadResult(generation, baseEntries, mods, directories, cache, warnings, currentDirectoryWarnings);
    }

    private static List<ConfigCandidate> ReadConfigCandidates(string path, CancellationToken cancellationToken)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024, FileOptions.SequentialScan);
        byte[] buffer = new byte[64 * 1024];
        int buffered = 0;
        JsonReaderState state = new();
        Stack<JsonFrame> frames = new();
        List<ConfigCandidate> candidates = new();
        bool sawToken = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = stream.Read(buffer, buffered, buffer.Length - buffered);
            bool isFinalBlock = read == 0;
            buffered += read;
            Utf8JsonReader reader = new(buffer.AsSpan(0, buffered), isFinalBlock, state);
            while (reader.Read())
            {
                sawToken = true;
                cancellationToken.ThrowIfCancellationRequested();
                ProcessJsonToken(ref reader, frames, candidates);
            }

            int consumed = checked((int)reader.BytesConsumed);
            state = reader.CurrentState;
            if (consumed < buffered)
                Buffer.BlockCopy(buffer, consumed, buffer, 0, buffered - consumed);
            buffered -= consumed;

            if (isFinalBlock)
                break;
            if (buffered == buffer.Length)
                throw new JsonException("JSON token exceeds the streaming buffer size.");
        }

        if (!sawToken)
            throw new JsonException("JSON 内容为空。");
        return candidates;
    }

    private static void ProcessJsonToken(
        ref Utf8JsonReader reader,
        Stack<JsonFrame> frames,
        List<ConfigCandidate> candidates)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
            case JsonTokenType.StartArray:
            {
                JsonValueContext context = TakeValueContext(frames);
                bool isArray = reader.TokenType == JsonTokenType.StartArray;
                bool nestedInArray = frames.Count > 0 && frames.Peek().IsArray;
                frames.Push(new JsonFrame(
                    isArray,
                    context.Path ?? "",
                    context.IsHotkeyContainer,
                    context.Ignore || nestedInArray));
                break;
            }
            case JsonTokenType.PropertyName:
                if (frames.Count > 0 && !frames.Peek().IsArray)
                    frames.Peek().PendingProperty = frames.Peek().Ignore ? null : reader.GetString();
                break;
            case JsonTokenType.String:
                JsonValueContext stringContext = TakeValueContext(frames);
                if (!stringContext.Ignore && stringContext.IsHotkeyContainer && stringContext.Path is not null)
                    candidates.Add(new ConfigCandidate(stringContext.Path, reader.GetString() ?? ""));
                break;
            case JsonTokenType.EndObject:
            case JsonTokenType.EndArray:
                if (frames.Count > 0)
                    frames.Pop();
                break;
            default:
                _ = TakeValueContext(frames);
                break;
        }
    }

    private static JsonValueContext TakeValueContext(Stack<JsonFrame> frames)
    {
        if (frames.Count == 0)
            return new("", false, false);

        JsonFrame frame = frames.Peek();
        if (frame.IsArray)
            return new(frame.Path, frame.IsHotkeyContainer, frame.Ignore);

        string? property = frame.PendingProperty;
        frame.PendingProperty = null;
        if (frame.Ignore || property is null)
            return new(null, false, true);

        string path = string.IsNullOrWhiteSpace(frame.Path) ? property : $"{frame.Path}.{property}";
        if (IsSensitivePath(path))
            return new(null, false, true);

        return new(path, frame.IsHotkeyContainer || IsHotkeyFieldName(property), false);
    }

    private static Dictionary<string, string> BuildModDirectoryIndex(
        string? modsRootPath,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> result = new(TextComparer);
        if (string.IsNullOrWhiteSpace(modsRootPath) || !Directory.Exists(modsRootPath))
            return result;

        EnumerationOptions options = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        foreach (string manifestPath in Directory.EnumerateFiles(modsRootPath, "manifest.json", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using FileStream stream = new(manifestPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.SequentialScan);
                using JsonDocument document = JsonDocument.Parse(stream);
                if (document.RootElement.TryGetProperty("UniqueID", out JsonElement idElement)
                    && idElement.ValueKind == JsonValueKind.String)
                {
                    string? uniqueId = idElement.GetString();
                    if (!string.IsNullOrWhiteSpace(uniqueId))
                        result[uniqueId] = Path.GetDirectoryName(manifestPath)!;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                warnings.Add($"无法读取 manifest：{manifestPath}（{ex.Message}）");
            }
        }

        return result;
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
            if (bindings.Count > 0)
                yield return new HotkeyEntry(action, "星露谷物语", "StardewValley", HotkeySource.Game, bindings, field);
        }
    }

    private List<HotkeyEntry> CollectGenericModConfigMenuEntries(List<string> warnings, HashSet<string> gmcmFields)
    {
        object? api = helper.ModRegistry.GetApi("spacechase0.GenericModConfigMenu");
        if (!ReferenceEquals(gmcmApi, api))
        {
            gmcmApi = api;
            gmcmReflection = api is null ? null : new GmcmReflectionCache();
        }

        List<HotkeyEntry> entries = new();
        if (api is null)
            return entries;

        try
        {
            GmcmReflectionCache reflection = gmcmReflection!;
            FieldInfo? configManagerField = reflection.GetField(api.GetType(), "ConfigManager");
            object? configManager = configManagerField?.GetValue(api);
            if (configManager is null)
            {
                warnings.Add("GMCM 已加载，但当前版本没有暴露可读取的注册项；模组快捷键将主要来自 config.json 推测。");
                return entries;
            }

            MethodInfo? getAllMethod = reflection.GetMethod(configManager.GetType(), "GetAll");
            if (getAllMethod is null)
            {
                warnings.Add("GMCM 已加载，但当前版本没有暴露可读取的注册项；模组快捷键将主要来自 config.json 推测。");
                return entries;
            }

            if (getAllMethod.Invoke(configManager, null) is not IEnumerable modConfigs)
                return entries;

            foreach (object modConfig in modConfigs)
            {
                try
                {
                    IManifest? manifest = GetProperty<IManifest>(reflection, modConfig, "ModManifest");
                    MethodInfo? getOptionsMethod = reflection.GetMethod(modConfig.GetType(), "GetAllOptions");
                    if (manifest is null || getOptionsMethod is null)
                    {
                        warnings.Add($"读取 GMCM 注册项失败（{modConfig.GetType().FullName ?? "未知模组"}）：缺少 ModManifest 或 GetAllOptions。");
                        continue;
                    }
                    if (getOptionsMethod.Invoke(modConfig, null) is not IEnumerable options)
                        continue;

                    foreach (object option in options)
                    {
                        Type? valueType = GetProperty<Type>(reflection, option, "Type");
                        if (valueType != typeof(SButton) && valueType != typeof(KeybindList))
                            continue;

                        string fieldId = GetProperty<string>(reflection, option, "FieldId") ?? "";
                        string action = GetOptionName(reflection, option, fieldId, warnings, manifest.Name);
                        object? value = GetProperty<object>(reflection, option, "Value");
                        string rawValue = value?.ToString() ?? "";
                        if (!TryParseBindings(rawValue, out List<HotkeyBinding> bindings))
                            continue;

                        if (!string.IsNullOrWhiteSpace(fieldId))
                            gmcmFields.Add(GetFieldKey(manifest.UniqueID, fieldId));
                        entries.Add(new HotkeyEntry(action, manifest.Name, manifest.UniqueID, HotkeySource.GenericModConfigMenu, bindings, fieldId));
                    }
                }
                catch (Exception ex)
                {
                    string name = modConfig.GetType().FullName ?? "未知模组";
                    warnings.Add($"读取 GMCM 注册项失败（{name}）：{ex.GetBaseException().Message}");
                }
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"读取 GMCM 注册项失败：{ex.GetBaseException().Message}");
        }

        return entries;
    }

    private static T? GetProperty<T>(GmcmReflectionCache reflection, object target, string propertyName)
    {
        PropertyInfo? property = reflection.GetProperty(target.GetType(), propertyName);
        object? value = property?.GetValue(target);
        return value is T typed ? typed : default;
    }

    private static string GetOptionName(
        GmcmReflectionCache reflection,
        object option,
        string fieldId,
        List<string> warnings,
        string modName)
    {
        Func<string>? name = GetProperty<Func<string>>(reflection, option, "Name");
        if (name is null)
            return string.IsNullOrWhiteSpace(fieldId) ? "未命名快捷键" : fieldId;

        try
        {
            string value = name();
            return string.IsNullOrWhiteSpace(value) ? fieldId : value;
        }
        catch (Exception ex)
        {
            warnings.Add($"读取 {modName} 的 GMCM 快捷键名称失败：{ex.GetBaseException().Message}");
            return string.IsNullOrWhiteSpace(fieldId) ? "未命名快捷键" : fieldId;
        }
    }

    private static bool TryParseBindings(string rawValue, out List<HotkeyBinding> bindings)
    {
        bindings = new List<HotkeyBinding>();
        rawValue = rawValue.Trim();
        if (string.IsNullOrWhiteSpace(rawValue) || rawValue.Equals("None", StringComparison.OrdinalIgnoreCase))
            return false;

        if (KeybindList.TryParse(rawValue, out KeybindList? parsed, out _) && parsed is not null && parsed.IsBound)
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

    private static string GetFieldKey(string ownerId, string fieldPath) => $"{ownerId}|{fieldPath}";

    private static List<HotkeyEntry> Deduplicate(IEnumerable<HotkeyEntry> entries)
    {
        Dictionary<string, HotkeyEntry> result = new(TextComparer);
        foreach (HotkeyEntry entry in entries)
        {
            string key = $"{entry.OwnerId}|{string.Join(',', entry.Bindings.Select(binding => binding.Normalized).OrderBy(value, TextComparer))}|{entry.Action}";
            if (!result.TryGetValue(key, out HotkeyEntry? existing) || SourceRank(entry.Source) < SourceRank(existing.Source))
                result[key] = entry;
        }
        return result.Values.ToList();
    }

    private static int SourceRank(HotkeySource source) => source switch
    {
        HotkeySource.Game => 0,
        HotkeySource.GenericModConfigMenu => 1,
        HotkeySource.ConfigGuess => 2,
        _ => 3
    };

    private void LogWarning(string warning)
    {
        if (loggedWarnings.Add(warning))
            monitor.Log(warning, LogLevel.Warn);
    }

    private sealed record ModSnapshot(string UniqueId, string Name);
    private sealed record ConfigCandidate(string FieldPath, string RawValue);
    private sealed record ConfigFileStamp(long Length, DateTime LastWriteUtc);
    private sealed record ConfigFileCache(ConfigFileStamp Stamp, IReadOnlyList<ConfigCandidate> Candidates, string? Warning);
    private sealed record LoadResult(
        int Generation,
        IReadOnlyList<HotkeyEntry> BaseEntries,
        IReadOnlyList<ModSnapshot> Mods,
        Dictionary<string, string> DirectoryIndex,
        Dictionary<string, ConfigFileCache> ConfigCache,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<string> DirectoryWarnings);

    private sealed class JsonFrame
    {
        internal JsonFrame(bool isArray, string path, bool isHotkeyContainer, bool ignore)
        {
            IsArray = isArray;
            Path = path;
            IsHotkeyContainer = isHotkeyContainer;
            Ignore = ignore;
        }

        internal bool IsArray { get; }
        internal string Path { get; }
        internal bool IsHotkeyContainer { get; }
        internal bool Ignore { get; }
        internal string? PendingProperty { get; set; }
    }

    private readonly record struct JsonValueContext(string? Path, bool IsHotkeyContainer, bool Ignore);

    private sealed class GmcmReflectionCache
    {
        private readonly Dictionary<(Type Type, string Name), FieldInfo?> fields = new();
        private readonly Dictionary<(Type Type, string Name), PropertyInfo?> properties = new();
        private readonly Dictionary<(Type Type, string Name), MethodInfo?> methods = new();

        internal FieldInfo? GetField(Type type, string name)
        {
            if (!fields.TryGetValue((type, name), out FieldInfo? field))
            {
                field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                fields[(type, name)] = field;
            }
            return field;
        }

        internal PropertyInfo? GetProperty(Type type, string name)
        {
            if (!properties.TryGetValue((type, name), out PropertyInfo? property))
            {
                property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                properties[(type, name)] = property;
            }
            return property;
        }

        internal MethodInfo? GetMethod(Type type, string name)
        {
            if (!methods.TryGetValue((type, name), out MethodInfo? method))
            {
                method = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(candidate => candidate.Name == name && candidate.GetParameters().Length == 0);
                methods[(type, name)] = method;
            }
            return method;
        }
    }
}
