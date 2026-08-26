using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Characters;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Quests;
using StardewValley.WorldMaps;

namespace Toolbox;

public enum NpcIconStyle
{
    Default,
    Vanilla
}

internal sealed class NpcMapLocationsFeature
{
    private const string SyncedNpcMarkersMessage = "ToolboxNpcMarkers";

    private static readonly HashSet<string> AlwaysHiddenNpcNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Mister Qi",
        "Bouncer",
        "Henchman",
        "Birdie"
    };

    private static readonly HashSet<string> HiddenUntilMetNpcNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dwarf",
        "Krobus",
        "Leo",
        "Sandy",
        "Wizard"
    };

    private static readonly Dictionary<string, int> DefaultMarkerOffsets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Abigail"] = 3,
        ["Alex"] = 0,
        ["Birdie"] = 6,
        ["Caroline"] = 2,
        ["Clint"] = -1,
        ["Demetrius"] = -2,
        ["Dwarf"] = 1,
        ["Elliott"] = -1,
        ["Emily"] = 1,
        ["Evelyn"] = 4,
        ["George"] = 4,
        ["Gus"] = 2,
        ["Gunther"] = 3,
        ["Haley"] = 2,
        ["Harvey"] = -1,
        ["Fizz"] = 4,
        ["Jas"] = 7,
        ["Jodi"] = 3,
        ["Kent"] = -1,
        ["Krobus"] = 0,
        ["Leah"] = 2,
        ["Leo"] = 6,
        ["Lewis"] = 1,
        ["Linus"] = 6,
        ["Marlon"] = 2,
        ["Marnie"] = 4,
        ["Maru"] = 2,
        ["Pam"] = 5,
        ["Penny"] = 3,
        ["Pierre"] = 0,
        ["Robin"] = 2,
        ["Sam"] = 0,
        ["Sandy"] = 2,
        ["Sebastian"] = 1,
        ["Shane"] = 1,
        ["Vincent"] = 8,
        ["Willy"] = -1,
        ["Wizard"] = 0
    };

    private static readonly Dictionary<string, Point> BuildingMarkerSizes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Shed"] = new Point(5, 7),
        ["Coop"] = new Point(5, 7),
        ["Barn"] = new Point(6, 7),
        ["SlimeHutch"] = new Point(7, 7),
        ["Greenhouse"] = new Point(5, 7),
        ["FarmHouse"] = new Point(5, 7),
        ["Cabin"] = new Point(4, 7),
        ["Log Cabin"] = new Point(4, 7),
        ["Plank Cabin"] = new Point(4, 7),
        ["Stone Cabin"] = new Point(4, 7)
    };

    private readonly IModHelper helper;
    private readonly IManifest manifest;
    private readonly Func<ModConfig> getConfig;
    private readonly Dictionary<string, NpcMarker> npcMarkers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BuildingMarker> buildingMarkers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<NpcMarker> orderedNpcMarkers = new();
    private readonly Dictionary<long, FarmerMapPosition> farmerMapPositions = new();
    private readonly Dictionary<string, NpcMapPositionCache> npcMapPositions = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<NPC> villagersBuffer = new();
    private readonly HashSet<NPC> seenVillagers = new();
    private readonly HashSet<string> questTargets = new(StringComparer.Ordinal);
    private readonly HashSet<string> activeNpcNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> staleMarkerNames = new();
    private readonly List<string> signatureNames = new();
    private Dictionary<string, SyncedNpcMarker>? pendingSyncedMarkers;
    private bool villagersDirty = true;
    private bool cachedShowHorse;
    private bool cachedShowChildren;
    private bool cachedRidingHorse;
    private Horse? cachedMount;
    private readonly StringBuilder markerSignatureBuilder = new();
    private string? markerSignature;
    private NpcMapPage? mapPage;
    private NpcMapPage? minimapPage;
    private SpriteBatch? minimapSpriteBatch;
    private SpriteBatch? minimapMapSpriteBatch;
    private RasterizerState? minimapRasterizer;
    private RenderTarget2D? minimapMapTexture;
    private NpcMapPage? minimapMapSourcePage;
    private MinimapLayoutKey? minimapLayoutKey;
    private bool minimapDragging;
    private Point minimapDragOffset;

    internal NpcMapLocationsFeature(IModHelper helper, IManifest manifest, Func<ModConfig> getConfig)
    {
        this.helper = helper;
        this.manifest = manifest;
        this.getConfig = getConfig;
    }

    internal void RegisterEvents()
    {
        helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        helper.Events.GameLoop.DayStarted += OnDayStarted;
        helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        helper.Events.Player.Warped += OnWarped;
        helper.Events.World.BuildingListChanged += OnBuildingListChanged;
        helper.Events.World.NpcListChanged += OnNpcListChanged;
        helper.Events.Input.ButtonsChanged += OnButtonsChanged;
        helper.Events.Input.ButtonPressed += OnButtonPressed;
        helper.Events.Input.ButtonReleased += OnButtonReleased;
        helper.Events.Display.RenderingHud += OnRenderingHud;
        helper.Events.Display.WindowResized += OnWindowResized;
        helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
        helper.Events.Multiplayer.PeerConnected += OnPeerConnected;
    }

    internal void OnConfigChanged()
    {
        NormalizeConfig();
        mapPage = null;
        minimapPage = null;
        InvalidateMinimapMapTexture();
        RestoreVanillaMapPage();
        UpdateMinimapVisibility();
        if (Context.IsWorldReady && getConfig().EnableNpcMapLocations)
        {
            UpdateFarmBuildingLocations();
            if (Context.IsMainPlayer)
            {
                ResetMarkers();
                UpdateMarkers();
            }
            else
            {
                RefreshClientMarkers();
            }
        }
    }

    private void RefreshClientMarkers()
    {
        foreach ((string name, NpcMarker marker) in npcMarkers)
        {
            NPC? npc = marker.Type == CharacterType.Special
                ? null
                : Game1.getCharacterFromName(name, false, false);
            ApplyClientMarkerVisibility(name, marker, npc);
        }

        RebuildMarkerOrder();
    }

    private void ApplyClientMarkerVisibility(string name, NpcMarker marker, NPC? npc)
    {
        ModConfig config = getConfig();
        if ((name == "Bookseller" && !config.ShowBookseller)
            || (name == "Merchant" && !config.ShowTravelingMerchant)
            || (marker.Type == CharacterType.Horse && !config.ShowHorse)
            || (marker.Type == CharacterType.Child && !config.ShowChildren))
        {
            marker.IsHidden = true;
            return;
        }

        if (marker.Type == CharacterType.Special)
        {
            marker.IsHidden = false;
            return;
        }

        if (npc is not null)
            marker.IsHidden = ShouldHide(npc, marker);
    }

    private void RestoreVanillaMapPage()
    {
        if (Game1.activeClickableMenu is not GameMenu menu
            || GameMenu.mapTab < 0
            || GameMenu.mapTab >= menu.pages.Count
            || menu.pages[GameMenu.mapTab] is not NpcMapPage)
        {
            return;
        }

        menu.pages[GameMenu.mapTab] = new MapPage(menu.xPositionOnScreen, menu.yPositionOnScreen, menu.width, menu.height);
        if (menu.currentTab == GameMenu.mapTab)
            menu.setTabNeighborsForCurrentPage();
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        NormalizeConfig();
        farmerMapPositions.Clear();
        npcMapPositions.Clear();
        pendingSyncedMarkers = null;
        mapPage = null;
        minimapPage = null;
        InvalidateMinimapMapTexture();
        ResetMarkers();
        UpdateFarmBuildingLocations();
        UpdateMarkers();
        UpdateMinimapVisibility();
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        if (!getConfig().EnableNpcMapLocations)
            return;

        npcMapPositions.Clear();
        ResetMarkers();
        UpdateMarkers();
        minimapPage = null;
        InvalidateMinimapMapTexture();
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        npcMarkers.Clear();
        buildingMarkers.Clear();
        orderedNpcMarkers.Clear();
        farmerMapPositions.Clear();
        npcMapPositions.Clear();
        villagersBuffer.Clear();
        seenVillagers.Clear();
        questTargets.Clear();
        pendingSyncedMarkers = null;
        activeNpcNames.Clear();
        staleMarkerNames.Clear();
        signatureNames.Clear();
        villagersDirty = true;
        cachedMount = null;
        mapPage = null;
        minimapPage = null;
        minimapDragging = false;
        InvalidateMinimapMapTexture();
        minimapSpriteBatch?.Dispose();
        minimapSpriteBatch = null;
        minimapMapSpriteBatch?.Dispose();
        minimapMapSpriteBatch = null;
        minimapRasterizer?.Dispose();
        minimapRasterizer = null;
    }

    private void OnWarped(object? sender, WarpedEventArgs e)
    {
        if (!e.IsLocalPlayer || !getConfig().EnableNpcMapLocations)
            return;

        farmerMapPositions.Clear();
        npcMapPositions.Clear();
        UpdateMinimapVisibility(e.NewLocation);
        minimapPage = null;
        InvalidateMinimapMapTexture();
        UpdateMarkers();
    }

    private void OnBuildingListChanged(object? sender, BuildingListChangedEventArgs e)
    {
        if (getConfig().EnableNpcMapLocations)
            UpdateFarmBuildingLocations();
    }

    private void OnNpcListChanged(object? sender, NpcListChangedEventArgs e)
    {
        villagersDirty = true;
        if (!Context.IsMainPlayer && pendingSyncedMarkers is not null)
            ApplySyncedMarkers();
    }

    private void OnButtonsChanged(object? sender, ButtonsChangedEventArgs e)
    {
        if (!Context.IsWorldReady || !getConfig().EnableNpcMapLocations)
            return;

        if (Game1.activeClickableMenu is null && getConfig().MinimapToggleKey.JustPressed())
        {
            ModConfig config = getConfig();
            config.ShowMinimap = !config.ShowMinimap;
            helper.WriteConfig(config);
            UpdateMinimapVisibility();
        }
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady
            || !getConfig().EnableNpcMapLocations
            || !IsMinimapDragButton(e.Button)
            || getConfig().LockMinimapPosition
            || !IsMinimapVisible()
            || !GetMinimapBounds().Contains(Game1.getMousePosition()))
        {
            return;
        }

        minimapDragging = true;
        Point mouse = Game1.getMousePosition();
        Rectangle bounds = GetMinimapBounds();
        minimapDragOffset = new Point(mouse.X - bounds.X, mouse.Y - bounds.Y);
    }

    private void OnButtonReleased(object? sender, ButtonReleasedEventArgs e)
    {
        if (!IsMinimapDragButton(e.Button) || !minimapDragging)
            return;

        minimapDragging = false;
        ModConfig config = getConfig();
        MoveMinimap(config, Game1.getMousePosition());
        helper.WriteConfig(config);
    }

    private void OnWindowResized(object? sender, WindowResizedEventArgs e)
    {
        minimapPage = null;
        InvalidateMinimapMapTexture();
        if (Context.IsWorldReady)
            UpdateMinimapVisibility();
    }

    private void OnRenderingHud(object? sender, RenderingHudEventArgs e)
    {
        if (!Context.IsWorldReady
            || !getConfig().EnableNpcMapLocations
            || !IsMinimapVisible()
            || !Game1.displayHUD
            || Game1.game1.takingMapScreenshot)
        {
            return;
        }

        if (minimapDragging)
        {
            ModConfig config = getConfig();
            MoveMinimap(config, Game1.getMousePosition());
            UpdateMinimapPage();
        }

        DrawMinimap();
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        ModConfig config = getConfig();
        if (!config.EnableNpcMapLocations)
        {
            RestoreVanillaMapPage();
            return;
        }

        if (!Context.IsMainPlayer
            && pendingSyncedMarkers is not null
            && e.IsMultipleOf(Math.Max(1u, config.NpcCacheTicks)))
        {
            ApplySyncedMarkers();
        }

        if (e.IsMultipleOf(Math.Max(1u, config.NpcCacheTicks)))
            UpdateMarkers();

        if (e.IsMultipleOf(Math.Max(1u, config.MiniMapCacheTicks)))
            UpdateMinimapPage();

        if (Game1.activeClickableMenu is GameMenu menu && menu.currentTab == GameMenu.mapTab)
        {
            InstallMapPage(menu);
        }
        else
        {
            mapPage = null;
        }
    }

    private void InstallMapPage(GameMenu menu)
    {
        if (GameMenu.mapTab < 0 || GameMenu.mapTab >= menu.pages.Count)
            return;

        if (menu.pages[GameMenu.mapTab] is NpcMapPage page)
        {
            mapPage = page;
            return;
        }

        mapPage = new NpcMapPage(menu.xPositionOnScreen, menu.yPositionOnScreen, menu.width, menu.height, this);
        menu.pages[GameMenu.mapTab] = mapPage;
        menu.setTabNeighborsForCurrentPage();
    }

    private void ResetMarkers()
    {
        npcMarkers.Clear();
        orderedNpcMarkers.Clear();
        npcMapPositions.Clear();
        activeNpcNames.Clear();
        villagersDirty = true;
        markerSignature = null;
        if (!Context.IsWorldReady)
            return;

        AddSpecialMarkers();
        if (!Context.IsMainPlayer)
        {
            RebuildMarkerOrder();
            return;
        }

        foreach (NPC npc in GetVillagers())
            AddNpcMarker(npc);
        RebuildMarkerOrder();
    }

    private static NpcMarker? CreateSpecialMarker(string name)
    {
        return name switch
        {
            "Bookseller" => new NpcMarker(
                "书摊老板",
                Game1.mouseCursors_1_6,
                new Rectangle(180, 490, 14, 18),
                null,
                CharacterType.Special),
            "Merchant" => new NpcMarker(
                "旅行商人",
                Game1.mouseCursors,
                new Rectangle(191, 1410, 22, 21),
                null,
                CharacterType.Special),
            _ => null
        };
    }

    private void AddSpecialMarkers()
    {
        ModConfig config = getConfig();
        if (config.ShowBookseller && Utility.getDaysOfBooksellerThisSeason().Contains(Game1.dayOfMonth))
        {
            MapPosition? position = GetWorldMapPosition(Game1.getLocationFromName("Town"), new Point(108, 25));
            if (position is not null)
            {
                npcMarkers["Bookseller"] = new NpcMarker(
                    "书摊老板",
                    Game1.mouseCursors_1_6,
                    new Rectangle(180, 490, 14, 18),
                    position,
                    CharacterType.Special);
            }
        }

        if (config.ShowTravelingMerchant && Game1.getLocationFromName("Forest") is Forest forest && forest.ShouldTravelingMerchantVisitToday())
        {
            Point cartTile = forest.GetTravelingMerchantCartTile();
            MapPosition? position = GetWorldMapPosition(forest, new Point(cartTile.X + 4, cartTile.Y));
            if (position is not null)
            {
                npcMarkers["Merchant"] = new NpcMarker(
                    "旅行商人",
                    Game1.mouseCursors,
                    new Rectangle(191, 1410, 22, 21),
                    position,
                    CharacterType.Special);
            }
        }
    }

    private void AddNpcMarker(NPC npc)
    {
        if (npc.SimpleNonVillagerNPC || IsIgnoredNpcType(npc))
            return;

        CharacterType type = npc switch
        {
            Horse => CharacterType.Horse,
            Child => CharacterType.Child,
            Raccoon => CharacterType.Raccoon,
            _ => CharacterType.Villager
        };
        string displayName = string.IsNullOrWhiteSpace(npc.displayName) ? npc.Name : npc.displayName;
        Texture2D? sprite = npc.Sprite?.Texture;
        if (sprite is null)
            return;

        int offset = getConfig().NpcMarkerOffsets.GetValueOrDefault(npc.Name, DefaultMarkerOffsets.GetValueOrDefault(npc.Name, 0));
        npcMarkers[npc.Name] = new NpcMarker(displayName, sprite, null, null, type)
        {
            CropOffset = offset,
            VanillaSourceRect = npc.getMugShotSourceRect(),
            IsBirthday = npc.isBirthday()
        };
    }

    private void UpdateMarkers()
    {
        if (!Context.IsWorldReady || !getConfig().EnableNpcMapLocations)
            return;

        if (!Context.IsMainPlayer)
            return;

        if (npcMarkers.Count == 0)
            ResetMarkers();

        HashSet<string> questTargets = GetQuestTargets();
        activeNpcNames.Clear();
        foreach (NPC npc in GetVillagers())
        {
            if (npc.SimpleNonVillagerNPC || IsIgnoredNpcType(npc))
                continue;

            activeNpcNames.Add(npc.Name);
            if (!npcMarkers.TryGetValue(npc.Name, out NpcMarker? marker))
            {
                AddNpcMarker(npc);
                npcMarkers.TryGetValue(npc.Name, out marker);
            }

            if (marker is null || npc.currentLocation is null)
                continue;

            marker.LocationName = npc.currentLocation.NameOrUniqueName;
            marker.IsOutdoors = npc.currentLocation.IsOutdoors;
            marker.Position = GetCachedNpcMapPosition(npc);
            marker.IsBirthday = npc.isBirthday();
            marker.HasQuest = questTargets.Contains(npc.Name);
            marker.IsHidden = ShouldHide(npc, marker);
            marker.Layer = marker.IsBirthday || marker.HasQuest ? 5 : marker.IsOutdoors ? 6 : 2;
        }

        staleMarkerNames.Clear();
        foreach ((string name, NpcMarker marker) in npcMarkers)
        {
            if (marker.Type != CharacterType.Special && !activeNpcNames.Contains(name))
                staleMarkerNames.Add(name);
        }

        foreach (string name in staleMarkerNames)
            npcMarkers.Remove(name);

        string newSignature = GetMarkerSignature();
        if (string.Equals(markerSignature, newSignature, StringComparison.Ordinal))
            return;

        markerSignature = newSignature;
        RebuildMarkerOrder();
        SyncNpcMarkers();
    }

    private void RebuildMarkerOrder()
    {
        orderedNpcMarkers.Clear();
        orderedNpcMarkers.AddRange(npcMarkers.Values.OrderBy(marker => marker.Layer));
    }

    private void SyncNpcMarkers(long? recipientPlayerId = null)
    {
        if (!Context.IsMultiplayer || !Context.IsMainPlayer)
            return;

        Dictionary<string, SyncedNpcMarker> syncedMarkers = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, NpcMarker marker) in npcMarkers)
        {
            syncedMarkers[name] = new SyncedNpcMarker
            {
                DisplayName = marker.DisplayName,
                LocationName = marker.LocationName,
                IsOutdoors = marker.IsOutdoors,
                RegionId = marker.Position?.RegionId,
                X = marker.Position?.X ?? 0,
                Y = marker.Position?.Y ?? 0,
                IsBirthday = marker.IsBirthday,
                HasQuest = marker.HasQuest,
                IsHidden = marker.IsHidden,
                CropOffset = marker.CropOffset,
                Type = marker.Type
            };
        }

        helper.Multiplayer.SendMessage(
            syncedMarkers,
            SyncedNpcMarkersMessage,
            new[] { manifest.UniqueID },
            recipientPlayerId.HasValue ? new[] { recipientPlayerId.Value } : null);
    }

    private void OnPeerConnected(object? sender, PeerConnectedEventArgs e)
    {
        if (!Context.IsWorldReady
            || !Context.IsMainPlayer
            || !getConfig().EnableNpcMapLocations)
        {
            return;
        }

        UpdateMarkers();
        SyncNpcMarkers(e.Peer.PlayerID);
    }

    private void ApplySyncedMarkers()
    {
        Dictionary<string, SyncedNpcMarker>? syncedMarkers = pendingSyncedMarkers;
        if (syncedMarkers is null)
            return;

        bool allMarkersResolved = true;
        activeNpcNames.Clear();
        foreach (string name in syncedMarkers.Keys)
            activeNpcNames.Add(name);

        staleMarkerNames.Clear();
        foreach (string name in npcMarkers.Keys)
        {
            if (!activeNpcNames.Contains(name))
                staleMarkerNames.Add(name);
        }

        foreach (string name in staleMarkerNames)
            npcMarkers.Remove(name);

        foreach ((string name, SyncedNpcMarker synced) in syncedMarkers)
        {
            if (!npcMarkers.TryGetValue(name, out NpcMarker? marker))
            {
                if (synced.Type == CharacterType.Special)
                {
                    marker = CreateSpecialMarker(name);
                    if (marker is not null)
                        npcMarkers[name] = marker;
                }
                else
                {
                    NPC? npc = Game1.getCharacterFromName(name, false, false);
                    if (npc is not null)
                    {
                        AddNpcMarker(npc);
                        npcMarkers.TryGetValue(name, out marker);
                    }
                    else
                    {
                        allMarkersResolved = false;
                    }
                }
            }

            if (marker is null)
                continue;

            marker.LocationName = synced.LocationName;
            marker.IsOutdoors = synced.IsOutdoors;
            marker.Position = synced.RegionId is null ? null : new MapPosition(synced.RegionId, synced.X, synced.Y);
            marker.IsBirthday = synced.IsBirthday;
            marker.HasQuest = synced.HasQuest;
            marker.CropOffset = synced.CropOffset;
            marker.IsHidden = synced.IsHidden;
            marker.Layer = marker.IsBirthday || marker.HasQuest ? 5 : marker.IsOutdoors ? 6 : 2;

            NPC? localNpc = marker.Type == CharacterType.Special
                ? null
                : Game1.getCharacterFromName(name, false, false);
            ApplyClientMarkerVisibility(name, marker, localNpc);
        }

        if (allMarkersResolved)
            pendingSyncedMarkers = null;
        RebuildMarkerOrder();
    }

    private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
    {
        if (Context.IsMainPlayer
            || e.FromModID != manifest.UniqueID
            || e.FromPlayerID != Game1.MasterPlayer.UniqueMultiplayerID
            || e.Type != SyncedNpcMarkersMessage)
        {
            return;
        }

        pendingSyncedMarkers = e.ReadAs<Dictionary<string, SyncedNpcMarker>>();
        ApplySyncedMarkers();
    }

    private List<NPC> GetVillagers()
    {
        ModConfig config = getConfig();
        bool ridingHorse = config.ShowHorse && Game1.player.isRidingHorse();
        Horse? mount = ridingHorse ? Game1.player.mount : null;
        if (!villagersDirty
            && cachedShowHorse == config.ShowHorse
            && cachedShowChildren == config.ShowChildren
            && cachedRidingHorse == ridingHorse
            && ReferenceEquals(cachedMount, mount))
        {
            return villagersBuffer;
        }

        villagersBuffer.Clear();
        seenVillagers.Clear();
        Utility.ForEachCharacter(npc =>
        {
            if (npc is not null
                && !npc.IsInvisible
                && (npc.IsVillager
                    || npc.isMarried()
                    || (config.ShowHorse && npc is Horse)
                    || (config.ShowChildren && npc is Child))
                && seenVillagers.Add(npc))
            {
                villagersBuffer.Add(npc);
            }

            return true;
        }, false);

        if (ridingHorse
            && mount is not null
            && seenVillagers.Add(mount))
        {
            villagersBuffer.Add(mount);
        }

        cachedShowHorse = config.ShowHorse;
        cachedShowChildren = config.ShowChildren;
        cachedRidingHorse = ridingHorse;
        cachedMount = mount;
        villagersDirty = false;
        return villagersBuffer;
    }

    private bool ShouldHide(NPC npc, NpcMarker marker)
    {
        ModConfig config = getConfig();
        if (config.NpcVisibility.TryGetValue(npc.Name, out bool explicitlyVisible))
            return !explicitlyVisible;

        if (!config.ShowHiddenVillagers && IsDefaultHidden(npc.Name))
            return true;

        bool important = config.ShowQuests && (marker.HasQuest || marker.IsBirthday);
        if (!important && config.FilterNpcsSpokenTo.HasValue && config.FilterNpcsSpokenTo.Value != Game1.player.hasTalkedToFriendToday(npc.Name))
            return true;

        if (config.OnlySameLocation && !IsSameLocation(npc.currentLocation, Game1.currentLocation))
            return true;

        int hearts = Game1.player.getFriendshipHeartLevelForNPC(npc.Name);
        return hearts < config.HeartLevelMin || hearts > config.HeartLevelMax;
    }

    private static bool IsDefaultHidden(string name)
    {
        if (AlwaysHiddenNpcNames.Contains(name))
            return true;

        if (name.Equals("Marlon", StringComparison.OrdinalIgnoreCase))
            return !Game1.player.eventsSeen.Contains("100162");

        if (HiddenUntilMetNpcNames.Contains(name))
            return !Game1.player.friendshipData.ContainsKey(name);

        return false;
    }

    private static bool IsSameLocation(GameLocation? first, GameLocation? second)
    {
        if (first is null || second is null)
            return false;

        if (first.NameOrUniqueName == second.NameOrUniqueName)
            return true;

        GameLocation firstRoot = first.GetRootLocation();
        GameLocation secondRoot = second.GetRootLocation();
        return firstRoot.NameOrUniqueName == secondRoot.NameOrUniqueName;
    }

    private HashSet<string> GetQuestTargets()
    {
        questTargets.Clear();
        foreach (Quest quest in Game1.player.questLog)
        {
            if (!quest.accepted.Value || !quest.dailyQuest.Value || quest.completed.Value)
                continue;

            string? target = quest switch
            {
                ItemDeliveryQuest itemQuest => itemQuest.target.Value,
                SlayMonsterQuest slayQuest => slayQuest.target.Value,
                FishingQuest fishingQuest => fishingQuest.target.Value,
                ResourceCollectionQuest resourceQuest => resourceQuest.target.Value,
                _ => null
            };
            if (!string.IsNullOrEmpty(target))
                questTargets.Add(target);
        }

        return questTargets;
    }

    private string GetMarkerSignature()
    {
        markerSignatureBuilder.Clear();
        signatureNames.Clear();
        signatureNames.AddRange(npcMarkers.Keys);
        signatureNames.Sort(StringComparer.OrdinalIgnoreCase);
        foreach (string name in signatureNames)
        {
            NpcMarker marker = npcMarkers[name];
            markerSignatureBuilder.Append(name).Append('\u001f')
                .Append(marker.LocationName).Append('\u001f')
                .Append(marker.IsOutdoors ? '1' : '0')
                .Append(marker.Position?.RegionId).Append(':')
                .Append(marker.Position?.X ?? 0).Append(':')
                .Append(marker.Position?.Y ?? 0).Append('\u001f')
                .Append(marker.IsBirthday ? '1' : '0')
                .Append(marker.HasQuest ? '1' : '0')
                .Append(marker.IsHidden ? '1' : '0')
                .Append(marker.CropOffset).Append('\u001f')
                .Append((int)marker.Type).Append(';');
        }

        return markerSignatureBuilder.ToString();
    }

    private void UpdateFarmBuildingLocations()
    {
        buildingMarkers.Clear();
        if (!Context.IsWorldReady || !getConfig().ShowFarmBuildings)
            return;

        Farm farm = Game1.getFarm();
        foreach (Building building in farm.buildings)
        {
            GameLocation? indoors = building.GetIndoors();
            if (indoors is null)
                continue;

            Point tile = new(
                building.tileX.Value + building.tilesWide.Value / 2,
                building.tileY.Value + building.tilesHigh.Value / 2);
            MapPosition? position = GetWorldMapPosition(farm, tile);
            if (position is not null)
                buildingMarkers[indoors.NameOrUniqueName] = new BuildingMarker(building.buildingType.Value, position);
        }
    }

    private MapPosition? GetWorldMapPosition(GameLocation? location, Point tile)
    {
        if (location is null)
            return null;

        MapAreaPositionWithContext? mapPosition = WorldMapManager.GetPositionData(location, tile);
        if (!mapPosition.HasValue)
            return null;

        Vector2 pixelPosition = mapPosition.Value.GetMapPixelPosition();
        return new MapPosition(mapPosition.Value.Data.Region.Id, (int)pixelPosition.X, (int)pixelPosition.Y);
    }

    private MapPosition? GetCachedWorldMapPosition(Farmer farmer, GameLocation? location, Point tile)
    {
        long farmerId = farmer.UniqueMultiplayerID;
        if (farmerMapPositions.TryGetValue(farmerId, out FarmerMapPosition? cached)
            && ReferenceEquals(cached.Location, location)
            && cached.Tile == tile)
        {
            return cached.Position;
        }

        MapPosition? position = GetWorldMapPosition(location, tile);
        farmerMapPositions[farmerId] = new FarmerMapPosition(location, tile, position);
        return position;
    }

    private MapPosition? GetCachedNpcMapPosition(NPC npc)
    {
        GameLocation? location = npc.currentLocation;
        Point tile = npc.TilePoint;
        if (npcMapPositions.TryGetValue(npc.Name, out NpcMapPositionCache? cached)
            && ReferenceEquals(cached.Location, location)
            && cached.Tile == tile)
        {
            return cached.Position;
        }

        MapPosition? position = GetWorldMapPosition(location, tile);
        npcMapPositions[npc.Name] = new NpcMapPositionCache(location, tile, position);
        return position;
    }

    private bool IsIgnoredNpcType(NPC npc)
    {
        return string.Equals(
            npc.GetType().FullName,
            "CustomCompanions.Framework.Companions.MapCompanion",
            StringComparison.OrdinalIgnoreCase);
    }

    private bool IsMinimapVisible()
    {
        ModConfig config = getConfig();
        if (!config.ShowMinimap || !config.EnableNpcMapLocations || !Context.IsWorldReady)
            return false;

        // The current location can be temporarily null while the world is loading or a player is warping.
        GameLocation? location = Game1.currentLocation;
        if (location is null || location is MineShaft)
            return false;

        if (config.MinimapExclusions.Contains(location.NameOrUniqueName)
            || config.MinimapExclusions.Contains(location.IsOutdoors ? "Outdoors" : "Indoors"))
        {
            return false;
        }

        return true;
    }

    private void UpdateMinimapVisibility(GameLocation? location = null)
    {
        minimapPage = null;
        InvalidateMinimapMapTexture();
        if (location is not null && getConfig().MinimapExclusions.Contains(location.NameOrUniqueName))
            return;
    }

    private Rectangle GetMinimapBounds()
    {
        ModConfig config = getConfig();
        return new Rectangle(
            config.MinimapX,
            config.MinimapY,
            Math.Max(45, config.MinimapWidth * 4),
            Math.Max(45, config.MinimapHeight * 4));
    }

    private void MoveMinimap(ModConfig config, Point mouse)
    {
        Rectangle bounds = GetMinimapBounds();
        int width = bounds.Width;
        int height = bounds.Height;
        config.MinimapX = MathHelper.Clamp(mouse.X - minimapDragOffset.X, 12, Math.Max(12, Game1.uiViewport.Width - width - 12));
        config.MinimapY = MathHelper.Clamp(mouse.Y - minimapDragOffset.Y, 12, Math.Max(12, Game1.uiViewport.Height - height - 12));
    }

    private void UpdateMinimapPage()
    {
        if (!IsMinimapVisible())
            return;

        MapPosition? playerPosition = GetCachedWorldMapPosition(
            Game1.player,
            Game1.currentLocation,
            Game1.player.TilePoint);
        if (playerPosition is null)
        {
            minimapPage = null;
            InvalidateMinimapMapTexture();
            return;
        }

        Rectangle bounds = GetMinimapBounds();
        MinimapLayoutKey layoutKey = new(
            playerPosition.RegionId,
            Game1.currentLocation.NameOrUniqueName,
            Game1.player.TilePoint,
            bounds,
            Game1.uiViewport.Width,
            Game1.uiViewport.Height);
        if (minimapPage is not null && minimapLayoutKey == layoutKey)
            return;

        if (minimapPage is null
            || minimapPage.RegionId != playerPosition.RegionId
            || minimapPage.ViewportWidth != Game1.uiViewport.Width
            || minimapPage.ViewportHeight != Game1.uiViewport.Height)
        {
            minimapPage = new NpcMapPage(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height, this);
            InvalidateMinimapMapTexture();
        }

        minimapLayoutKey = layoutKey;
        Rectangle mapBounds = minimapPage.MapBounds;
        int mapWidth = mapBounds.Width * 4;
        int mapHeight = mapBounds.Height * 4;
        int x = bounds.Center.X - playerPosition.X;
        int y = bounds.Center.Y - playerPosition.Y;

        if (mapHeight > bounds.Height)
            y = MathHelper.Clamp(y, bounds.Bottom - mapHeight, bounds.Top);
        if (mapWidth > bounds.Width)
            x = MathHelper.Clamp(x, bounds.Right - mapWidth, bounds.Left);

        mapBounds.X = x;
        mapBounds.Y = y;
        minimapPage.MapBounds = mapBounds;
    }

    private void DrawMinimap()
    {
        NpcMapPage? page = minimapPage;
        if (page is null)
            return;

        GraphicsDevice graphicsDevice = ((GraphicsResource)Game1.spriteBatch).GraphicsDevice;
        Rectangle previousScissor = graphicsDevice.ScissorRectangle;
        EnsureMinimapMapTexture(graphicsDevice);
        RenderTarget2D? mapTexture = minimapMapTexture;
        if (mapTexture is null)
            return;

        Rectangle bounds = GetMinimapBounds();
        SpriteBatch minimapBatch = minimapSpriteBatch ??= new SpriteBatch(graphicsDevice);
        bool spriteBatchStarted = false;
        try
        {
            graphicsDevice.ScissorRectangle = bounds;
            minimapRasterizer ??= new RasterizerState { ScissorTestEnable = true };
            minimapBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.NonPremultiplied,
                SamplerState.PointClamp,
                null,
                minimapRasterizer);
            spriteBatchStarted = true;
            float opacity = MathHelper.Clamp(getConfig().MinimapOpacity, 0.05f, 1f);
            minimapBatch.Draw(Game1.staminaRect, bounds, Color.Black * opacity);
            Rectangle mapBounds = page.MapBounds;
            minimapBatch.Draw(
                mapTexture,
                new Rectangle(mapBounds.X, mapBounds.Y, mapBounds.Width * 4, mapBounds.Height * 4),
                Color.White * opacity);
            page.drawMiniPortraits(minimapBatch, opacity);
        }
        finally
        {
            try
            {
                if (spriteBatchStarted)
                    minimapBatch.End();
            }
            finally
            {
                graphicsDevice.ScissorRectangle = previousScissor;
            }
        }

        Color border = IsMinimapDragZoneHovered() ? Color.White : Color.LightGray;
        Game1.spriteBatch.Draw(Game1.staminaRect, new Rectangle(bounds.X - 2, bounds.Y - 2, bounds.Width + 4, 2), border * 0.75f);
        Game1.spriteBatch.Draw(Game1.staminaRect, new Rectangle(bounds.X - 2, bounds.Bottom, bounds.Width + 4, 2), border * 0.75f);
        Game1.spriteBatch.Draw(Game1.staminaRect, new Rectangle(bounds.X - 2, bounds.Y, 2, bounds.Height), border * 0.75f);
        Game1.spriteBatch.Draw(Game1.staminaRect, new Rectangle(bounds.Right, bounds.Y, 2, bounds.Height), border * 0.75f);
    }

    private void EnsureMinimapMapTexture(GraphicsDevice graphicsDevice)
    {
        NpcMapPage? page = minimapPage;
        if (page is null)
            return;

        Rectangle mapBounds = page.MapBounds;
        int textureWidth = Math.Max(1, mapBounds.Width * 4);
        int textureHeight = Math.Max(1, mapBounds.Height * 4);
        if (minimapMapTexture is not null
            && ReferenceEquals(minimapMapSourcePage, page)
            && minimapMapTexture.Width == textureWidth
            && minimapMapTexture.Height == textureHeight)
        {
            return;
        }

        InvalidateMinimapMapTexture();
        RenderTarget2D? texture = null;
        SpriteBatch? mapSpriteBatch = null;
        RenderTargetBinding[]? previousTargets = null;
        Viewport previousViewport = default;
        Rectangle previousScissor = default;
        Rectangle originalMapBounds = mapBounds;
        bool stateCaptured = false;
        bool spriteBatchStarted = false;
        try
        {
            texture = new RenderTarget2D(
                graphicsDevice,
                textureWidth,
                textureHeight,
                false,
                SurfaceFormat.Color,
                DepthFormat.None);
            mapSpriteBatch = minimapMapSpriteBatch ??= new SpriteBatch(graphicsDevice);
            previousTargets = graphicsDevice.GetRenderTargets();
            previousViewport = graphicsDevice.Viewport;
            previousScissor = graphicsDevice.ScissorRectangle;
            stateCaptured = true;

            try
            {
                page.MapBounds = new Rectangle(0, 0, originalMapBounds.Width, originalMapBounds.Height);
                graphicsDevice.SetRenderTarget(texture);
                graphicsDevice.Clear(Color.Transparent);
                mapSpriteBatch.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.NonPremultiplied,
                    SamplerState.PointClamp);
                spriteBatchStarted = true;
                page.drawMap(mapSpriteBatch, false, 1f);
            }
            finally
            {
                try
                {
                    if (spriteBatchStarted)
                        mapSpriteBatch.End();
                }
                finally
                {
                    if (stateCaptured)
                    {
                        page.MapBounds = originalMapBounds;
                        try
                        {
                            graphicsDevice.SetRenderTargets(previousTargets!);
                        }
                        finally
                        {
                            graphicsDevice.Viewport = previousViewport;
                            graphicsDevice.ScissorRectangle = previousScissor;
                        }
                    }
                }
            }
        }
        catch
        {
            texture?.Dispose();
            throw;
        }

        minimapMapTexture = texture!;
        minimapMapSourcePage = page;
    }

    private void InvalidateMinimapMapTexture()
    {
        minimapMapTexture?.Dispose();
        minimapMapTexture = null;
        minimapMapSourcePage = null;
    }

    private static bool IsMinimapDragButton(SButton button)
    {
        return button == SButton.MouseRight
            || (OperatingSystem.IsAndroid() && button == SButton.MouseLeft);
    }

    private bool IsMinimapDragZoneHovered()
    {
        return !getConfig().LockMinimapPosition && GetMinimapBounds().Contains(Game1.getMousePosition());
    }

    private string GetHoveredNames(NpcMapPage page)
    {
        Point mouse = Game1.getMousePosition();
        List<string> names = new();
        foreach ((string name, NpcMarker marker) in npcMarkers)
        {
            if (marker.IsHidden || marker.Position?.RegionId != page.RegionId)
                continue;

            int x = page.MapBounds.X + marker.Position.X;
            int y = page.MapBounds.Y + marker.Position.Y;
            if (Math.Abs(mouse.X - x) <= 18 && Math.Abs(mouse.Y - y) <= 18)
                names.Add(marker.DisplayName);
        }

        if (names.Count == 0)
            return string.Empty;

        return string.Join(", ", names.Distinct().OrderBy(name => name, StringComparer.Ordinal));
    }

    private void DrawPlayerMarkers(NpcMapPage page, SpriteBatch batch, float alpha)
    {
        foreach (Farmer farmer in Game1.getOnlineFarmers())
        {
            MapPosition? position = GetCachedWorldMapPosition(farmer, farmer.currentLocation, farmer.TilePoint);
            if (position is null || position.RegionId != page.RegionId)
                continue;

            float scale = farmer.IsLocalPlayer ? getConfig().CurrentPlayerMarkerScale : getConfig().OtherPlayerMarkerScale;
            farmer.FarmerRenderer.drawMiniPortrat(
                batch,
                new Vector2(page.MapBounds.X + position.X - 16f * scale, page.MapBounds.Y + position.Y - 15f * scale),
                0.00011f,
                2f * scale,
                1,
                farmer,
                alpha);
        }
    }

    private void DrawMarkers(NpcMapPage page, SpriteBatch batch, float alpha)
    {
        DrawBuildings(page, batch, alpha);
        string regionId = page.RegionId;
        ModConfig config = getConfig();
        Point iconSize = config.NpcIconStyle == NpcIconStyle.Vanilla ? new Point(36, 34) : new Point(32, 30);

        foreach (NpcMarker marker in orderedNpcMarkers)
        {
            if (marker.Position?.RegionId != regionId
                || marker.IsHidden
                || marker.Sprite is null
                || marker.Position is null)
                continue;

            Rectangle source = marker.GetSourceRect(config.NpcIconStyle);
            if (source.Width <= 0 || source.Height <= 0)
                continue;

            float scale = Math.Min((float)iconSize.X / source.Width, (float)iconSize.Y / source.Height) * config.NpcMarkerScale;
            int width = Math.Max(1, (int)(source.Width * scale));
            int height = Math.Max(1, (int)(source.Height * scale));
            Rectangle destination = new(
                page.MapBounds.X + marker.Position.X - width / 2,
                page.MapBounds.Y + marker.Position.Y - height / 2,
                width,
                height);
            batch.Draw(marker.Sprite, destination, source, Color.White * alpha);

            if (!config.ShowQuests)
                continue;
            if (marker.IsBirthday)
                batch.Draw(Game1.mouseCursors, new Vector2(destination.X + 20, destination.Y), new Rectangle(147, 412, 10, 11), Color.White * alpha, 0f, Vector2.Zero, 1.8f, SpriteEffects.None, 0f);
            if (marker.HasQuest)
                batch.Draw(Game1.mouseCursors, new Vector2(destination.X + 22, destination.Y - 3), new Rectangle(403, 496, 5, 14), Color.White * alpha, 0f, Vector2.Zero, 1.8f, SpriteEffects.None, 0f);
        }
    }

    private void DrawBuildings(NpcMapPage page, SpriteBatch batch, float alpha)
    {
        if (!getConfig().ShowFarmBuildings || page.RegionId != "Valley")
            return;

        foreach (BuildingMarker marker in buildingMarkers.Values)
        {
            if (marker.Position.RegionId != page.RegionId)
                continue;

            string commonName = marker.CommonName;
            if (commonName.StartsWith("Big ", StringComparison.OrdinalIgnoreCase))
                commonName = commonName[4..];
            if (commonName.StartsWith("Deluxe ", StringComparison.OrdinalIgnoreCase))
                commonName = commonName[7..];
            if (!BuildingMarkerSizes.TryGetValue(commonName, out Point size))
                size = new Point(4, 4);

            Rectangle destination = new(
                page.MapBounds.X + marker.Position.X - size.X * 2,
                page.MapBounds.Y + marker.Position.Y - size.Y * 2,
                Math.Max(4, size.X * 4),
                Math.Max(4, size.Y * 4));
            batch.Draw(Game1.staminaRect, destination, Color.Sienna * alpha);
        }
    }

    private void NormalizeConfig()
    {
        ModConfig config = getConfig();
        config.MinimapExclusions ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        config.MinimapExclusions = new HashSet<string>(config.MinimapExclusions, StringComparer.OrdinalIgnoreCase);
        config.NpcVisibility ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        config.NpcVisibility = new Dictionary<string, bool>(config.NpcVisibility, StringComparer.OrdinalIgnoreCase);
        config.NpcMarkerOffsets ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        config.NpcMarkerOffsets = new Dictionary<string, int>(config.NpcMarkerOffsets, StringComparer.OrdinalIgnoreCase);
        config.MinimapToggleKey ??= new KeybindList(Array.Empty<Keybind>());
    }

    private sealed class NpcMapPage : MapPage
    {
        private readonly NpcMapLocationsFeature feature;

        internal NpcMapPage(int x, int y, int width, int height, NpcMapLocationsFeature feature)
            : base(x, y, width, height)
        {
            this.feature = feature;
            ViewportWidth = width;
            ViewportHeight = height;
        }

        internal int ViewportWidth { get; }
        internal int ViewportHeight { get; }
        internal string RegionId => mapRegion.Id;

        internal Rectangle MapBounds
        {
            get => mapBounds;
            set => mapBounds = value;
        }

        public override void performHoverAction(int x, int y)
        {
            base.performHoverAction(x, y);
            string names = feature.GetHoveredNames(this);
            if (!string.IsNullOrEmpty(names))
                hoverText = names;
        }

        public override void drawMiniPortraits(SpriteBatch b, float alpha = 1f)
        {
            feature.DrawPlayerMarkers(this, b, alpha);
            feature.DrawMarkers(this, b, alpha);
        }
    }

    private sealed record FarmerMapPosition(GameLocation? Location, Point Tile, MapPosition? Position);
    private sealed record NpcMapPositionCache(GameLocation? Location, Point Tile, MapPosition? Position);

    private sealed class NpcMarker
    {
        internal NpcMarker(string displayName, Texture2D sprite, Rectangle? sourceRect, MapPosition? position, CharacterType type)
        {
            DisplayName = displayName;
            Sprite = sprite;
            SourceRect = sourceRect;
            Position = position;
            Type = type;
        }

        internal string DisplayName { get; }
        internal Texture2D? Sprite { get; }
        internal Rectangle? SourceRect { get; }
        internal Rectangle? VanillaSourceRect { get; set; }
        internal MapPosition? Position { get; set; }
        internal string? LocationName { get; set; }
        internal bool IsOutdoors { get; set; }
        internal bool IsBirthday { get; set; }
        internal bool HasQuest { get; set; }
        internal bool IsHidden { get; set; }
        internal int CropOffset { get; set; }
        internal int Layer { get; set; } = 4;
        internal CharacterType Type { get; }

        internal Rectangle GetSourceRect(NpcIconStyle style)
        {
            if (style == NpcIconStyle.Vanilla && VanillaSourceRect.HasValue)
                return VanillaSourceRect.Value;
            if (SourceRect.HasValue)
                return SourceRect.Value;
            if (Type == CharacterType.Horse)
                return new Rectangle(17, 104, 16, 14);
            if (Type == CharacterType.Raccoon)
                return new Rectangle(11, 17, 11, 10);
            return new Rectangle(0, Math.Max(0, CropOffset), 16, 15);
        }
    }

    private sealed class SyncedNpcMarker
    {
        public string? DisplayName { get; set; }
        public string? LocationName { get; set; }
        public bool IsOutdoors { get; set; }
        public string? RegionId { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public bool IsBirthday { get; set; }
        public bool HasQuest { get; set; }
        public bool IsHidden { get; set; }
        public int CropOffset { get; set; }
        public CharacterType Type { get; set; }
    }

    private sealed record BuildingMarker(string CommonName, MapPosition Position);

    private sealed record MapPosition(string RegionId, int X, int Y);

    private sealed record MinimapLayoutKey(
        string RegionId,
        string LocationName,
        Point PlayerTile,
        Rectangle Bounds,
        int ViewportWidth,
        int ViewportHeight);

    private enum CharacterType
    {
        Villager,
        Horse,
        Child,
        Raccoon,
        Special
    }
}
