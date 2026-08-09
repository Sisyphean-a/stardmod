using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Menus;

namespace HorseFollower;

internal enum HorseNavigationState
{
    Idle,
    Planning,
    Navigating,
    WaitingForWarp,
    Paused,
    Completed,
    Canceled,
    Failed
}

internal sealed class HorseNavigationService
{
    private const float ParkingStoppingDistancePixels = 8f;
    private const int DefaultSearchNodesPerUpdate = 32;
    private const int WarpWaitTimeoutTicks = 180;

    private readonly IModHelper helper;
    private readonly IMonitor monitor;
    private readonly ModConfig config;
    private readonly HorseWalkAnimator horseAnimator = new();
    private readonly HashSet<string> blockedEdges = new(StringComparer.Ordinal);

    private OutdoorRouteGraph? routeGraph;
    private OutdoorRoutePlan? routePlan;
    private RiderPathSearch? pathSearch;
    private PathSearchTarget? pathTarget;
    private RiderNavigationController? riderController;
    private Horse? activeHorse;
    private HorseNavigationDestination? destination;
    private HorseNavigationState pausedState;
    private HorseNavigationState state;
    private int edgeIndex;
    private int parkingIndex;
    private int warpWaitTicks;
    private string statusText = "";

    internal HorseNavigationService(IModHelper helper, ModConfig config, IMonitor monitor)
    {
        if (config.NavigationSearchNodesPerUpdate <= 0)
            throw new ArgumentOutOfRangeException(nameof(config.NavigationSearchNodesPerUpdate));

        this.helper = helper;
        this.config = config;
        this.monitor = monitor;
        state = HorseNavigationState.Idle;
    }

    internal HorseNavigationState State => state;

    internal void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        Reset("day-start");
    }

    internal void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        Reset("returned-to-title");
    }

    internal void OnUpdateTicking(object? sender, UpdateTickingEventArgs e)
    {
        if (!Context.IsWorldReady || !IsActiveState(state))
            return;

        if (Game1.player.mount is null)
        {
            Fail("玩家已下马");
            return;
        }

        if (Game1.activeClickableMenu is not null && state != HorseNavigationState.Paused)
            PauseForMenu();
    }

    internal void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady)
        {
            Reset("world-not-ready");
            return;
        }

        if (!IsActiveState(state) && state != HorseNavigationState.Paused)
            return;

        if (Game1.player.mount is null || activeHorse is null || !ReferenceEquals(Game1.player.mount, activeHorse))
        {
            Fail("骑乘状态发生变化");
            return;
        }

        if (!OutdoorWarpTracker.IsSupportedOutdoorLocation(Game1.currentLocation))
        {
            Fail("当前地图不是支持的室外地图");
            return;
        }

        if (Game1.activeClickableMenu is not null)
        {
            if (state != HorseNavigationState.Paused)
                PauseForMenu();
            return;
        }

        if (state == HorseNavigationState.Paused)
        {
            state = pausedState;
            statusText = state == HorseNavigationState.Planning ? "正在继续规划" : "正在继续导航";
            Log($"navigation-resume state={state}");
        }

        if (state == HorseNavigationState.WaitingForWarp && riderController is null)
        {
            warpWaitTicks++;
            if (warpWaitTicks > WarpWaitTimeoutTicks)
            {
                Fail("等待地图切换超时");
                return;
            }
        }

        if (state == HorseNavigationState.Planning)
        {
            AdvancePlanning();
            return;
        }

        if ((state is HorseNavigationState.Navigating or HorseNavigationState.WaitingForWarp)
            && riderController is not null
            && !ReferenceEquals(Game1.player.controller, riderController))
        {
            Fail("玩家移动控制器被其他逻辑接管");
            return;
        }

        if ((state is HorseNavigationState.Navigating or HorseNavigationState.WaitingForWarp)
            && activeHorse is not null
            && riderController is not null
            && ReferenceEquals(Game1.player.controller, riderController))
        {
            horseAnimator.Tick(activeHorse, Game1.currentGameTime);
        }
    }

    internal void OnPlayerWarped(object? sender, WarpedEventArgs e)
    {
        if (!e.IsLocalPlayer
            || state is not (HorseNavigationState.Navigating or HorseNavigationState.WaitingForWarp)
            || routePlan is null)
        {
            return;
        }

        if (edgeIndex >= routePlan.Edges.Count)
        {
            DetachRiderControllerAfterWarp();
            Fail("发生了未计划的玩家传送");
            return;
        }

        OutdoorRouteEdge edge = routePlan.Edges[edgeIndex];
        Log(
            $"navigation-warp observed old={e.OldLocation.NameOrUniqueName} new={e.NewLocation.NameOrUniqueName} "
            + $"landing=({e.Player.TilePoint.X},{e.Player.TilePoint.Y}) expected=({edge.TargetEntryTile.X},{edge.TargetEntryTile.Y}) "
            + $"state={state}");
        bool matches = OutdoorWarpTracker.IsSameLocation(e.OldLocation, edge.SourceLocation)
            && OutdoorWarpTracker.IsSameLocation(e.NewLocation, edge.TargetLocation)
            && IsNearExpectedLanding(e.Player.TilePoint, edge.TargetEntryTile);
        if (!matches)
        {
            DetachRiderControllerAfterWarp();
            Fail(
                $"计划外传送：{e.OldLocation.NameOrUniqueName} -> {e.NewLocation.NameOrUniqueName} "
                + $"落点=({e.Player.TilePoint.X},{e.Player.TilePoint.Y})");
            return;
        }

        DetachRiderControllerAfterWarp();
        edgeIndex++;
        warpWaitTicks = 0;
        pathSearch = null;
        pathTarget = null;
        state = HorseNavigationState.Planning;
        statusText = $"已到达 {e.NewLocation.NameOrUniqueName}，继续规划出口";
        Log($"navigation-warp accepted edge={edge.Key}");
    }

    internal void OnMenuChanged(object? sender, MenuChangedEventArgs e)
    {
        if (e.NewMenu is not null && IsActiveState(state))
        {
            PauseForMenu();
            return;
        }

        if (e.NewMenu is null && state == HorseNavigationState.Paused)
        {
            state = pausedState;
            statusText = state == HorseNavigationState.Planning ? "正在继续规划" : "正在继续导航";
            Log($"navigation-resume state={state}");
        }
    }

    internal void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (e.IsSuppressed())
            return;

        if (IsActiveState(state)
            && Game1.activeClickableMenu is null
            && IsMovementButton(e.Button))
        {
            Cancel("玩家方向键输入");
            return;
        }

        if (e.Button != SButton.MouseLeft
            || Game1.activeClickableMenu is not null
            || !Context.IsWorldReady
            || Game1.player.mount is null
            || !OutdoorWarpTracker.IsSupportedOutdoorLocation(Game1.currentLocation))
        {
            return;
        }

        Rectangle bounds = GetHudButtonBounds();
        if (!bounds.Contains(Game1.getMouseX(true), Game1.getMouseY(true)))
            return;

        OpenDestinationMenu();
        helper.Input.Suppress(e.Button);
    }

    internal void OnRenderedHud(object? sender, RenderedHudEventArgs e)
    {
        if (!Context.IsWorldReady
            || Game1.activeClickableMenu is not null
            || Game1.player.mount is null
            || !OutdoorWarpTracker.IsSupportedOutdoorLocation(Game1.currentLocation))
        {
            return;
        }

        Rectangle bounds = GetHudButtonBounds();
        Color color = state switch
        {
            HorseNavigationState.Navigating or HorseNavigationState.Planning or HorseNavigationState.WaitingForWarp => new Color(62, 111, 151),
            HorseNavigationState.Paused => new Color(168, 124, 52),
            HorseNavigationState.Failed => new Color(155, 76, 65),
            HorseNavigationState.Completed => new Color(75, 133, 83),
            HorseNavigationState.Canceled => new Color(112, 112, 112),
            _ => new Color(92, 75, 54)
        };
        IClickableMenu.drawTextureBox(
            e.SpriteBatch,
            Game1.mouseCursors,
            new Rectangle(403, 383, 6, 6),
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            color,
            4f,
            false);

        string label = state switch
        {
            HorseNavigationState.Navigating or HorseNavigationState.Planning => "导航中",
            HorseNavigationState.WaitingForWarp => "等待换图",
            HorseNavigationState.Paused => "已暂停",
            HorseNavigationState.Completed => "已到达",
            HorseNavigationState.Canceled => "已取消",
            HorseNavigationState.Failed => "导航失败",
            _ => "骑马寻路"
        };
        Vector2 size = Game1.smallFont.MeasureString(label);
        e.SpriteBatch.DrawString(
            Game1.smallFont,
            label,
            new Vector2(bounds.Center.X - size.X / 2f, bounds.Center.Y - size.Y / 2f),
            Color.White);
    }

    private void OpenDestinationMenu()
    {
        if (Game1.activeClickableMenu is not null)
            return;

        Game1.activeClickableMenu = new HorseNavigationMenu(HorseNavigationDestination.All, BeginNavigation);
        Game1.playSound("bigSelect");
    }

    private void BeginNavigation(HorseNavigationDestination selectedDestination)
    {
        if (!selectedDestination.IsAvailable)
        {
            Fail("目的地尚未开放");
            return;
        }

        Horse? horse = Game1.player.mount;
        if (horse is null || !OutdoorWarpTracker.IsSupportedOutdoorLocation(Game1.currentLocation))
        {
            Fail("只有骑乘并处于支持的室外地图时才能导航");
            return;
        }

        if (Game1.player.controller is not null
            && !ReferenceEquals(Game1.player.controller, riderController))
        {
            state = HorseNavigationState.Failed;
            statusText = "玩家当前正由其他逻辑控制，未启动自动导航";
            Log("navigation-reject reason=external-player-controller");
            return;
        }

        StopRiderController();
        activeHorse = horse;
        destination = selectedDestination;
        routeGraph = OutdoorRouteGraph.Build(monitor);
        routePlan = null;
        pathSearch = null;
        pathTarget = null;
        blockedEdges.Clear();
        edgeIndex = 0;
        parkingIndex = 0;
        state = HorseNavigationState.Planning;
        statusText = $"正在规划：{selectedDestination.DisplayName}";
        Log(
            $"navigation-start destination={selectedDestination.Id} map={selectedDestination.MapName} "
            + $"current={Game1.currentLocation.NameOrUniqueName} riderTile=({Game1.player.TilePoint.X},{Game1.player.TilePoint.Y}) "
            + $"horseTile=({horse.TilePoint.X},{horse.TilePoint.Y})");
    }

    private void AdvancePlanning()
    {
        if (routeGraph is null || destination is null || activeHorse is null)
        {
            Fail("导航上下文不完整");
            return;
        }

        if (routePlan is null)
        {
            routePlan = routeGraph.FindPlan(
                Game1.currentLocation,
                activeHorse.TilePoint,
                destination,
                blockedEdges);
            if (routePlan is null)
            {
                Fail($"找不到前往 {destination.DisplayName} 的室外路线");
                return;
            }

            edgeIndex = 0;
            parkingIndex = 0;
            Log(
                $"navigation-plan edges={routePlan.Edges.Count} destination={destination.Id} "
                + $"route={string.Join(" | ", routePlan.Edges.Select(edge => edge.Key))}");
        }

        if (pathSearch is null && !TryStartCurrentSearch())
            return;
        if (pathSearch is null || pathTarget is null)
            return;

        pathSearch.Advance(config.NavigationSearchNodesPerUpdate);
        if (!pathSearch.IsComplete)
        {
            statusText = $"正在规划：已搜索 {pathSearch.SearchedNodeCount} 个节点";
            return;
        }

        RiderPathSearch completedSearch = pathSearch;
        PathSearchTarget completedTarget = pathTarget;
        pathSearch = null;
        pathTarget = null;
        if (completedSearch.Path is null)
        {
            Log($"navigation-search-failed target=({completedTarget.TargetTile.X},{completedTarget.TargetTile.Y}) "
                + $"edge={(completedTarget.PortalEdge?.Key ?? "parking")} searched={completedSearch.SearchedNodeCount}");
            HandleSearchFailure(completedTarget);
            return;
        }

        GameLocation location = Game1.currentLocation;
        if (Game1.player.controller is not null)
        {
            Fail("玩家移动控制器被其他逻辑接管");
            return;
        }

        RiderNavigationController? controller = null;
        controller = new RiderNavigationController(
            Game1.player,
            activeHorse,
            horseAnimator,
            location,
            completedTarget.TargetTile,
            completedSearch.Path,
            completedTarget.PortalEdge,
            () => OnPortalAttempt(completedTarget.PortalEdge),
            reason => OnControllerStopped(controller, reason));
        riderController = controller;
        Game1.player.controller = controller;
        state = HorseNavigationState.Navigating;
        statusText = completedTarget.PortalEdge is null ? "正在前往入口外停车点" : "正在前往下一个室外出口";
        Log($"navigation-controller-created target=({completedTarget.TargetTile.X},{completedTarget.TargetTile.Y}) path={completedSearch.Path.Count}");
    }

    private bool TryStartCurrentSearch()
    {
        if (routePlan is null || destination is null || activeHorse is null)
            return false;

        GameLocation currentLocation = Game1.currentLocation;
        if (edgeIndex < routePlan.Edges.Count)
        {
            OutdoorRouteEdge edge = routePlan.Edges[edgeIndex];
            if (!OutdoorWarpTracker.IsSameLocation(currentLocation, edge.SourceLocation))
            {
                Replan("当前地图与计划出口不一致");
                return false;
            }

            Point targetTile = OutdoorRouteGraph.GetApproachTile(
                currentLocation,
                edge.SourceExitTile,
                activeHorse.TilePoint,
                clearanceTiles: 2);
            pathTarget = new PathSearchTarget(targetTile, edge);
            pathSearch = new RiderPathSearch(
                Game1.player,
                currentLocation,
                GetTileCenter(targetTile),
                ParkingStoppingDistancePixels);
            return true;
        }

        if (!OutdoorWarpTracker.IsSameLocation(currentLocation, routePlan.TargetLocation))
        {
            Replan("尚未到达目的地地图");
            return false;
        }

        while (parkingIndex < destination.ParkingCandidates.Count)
        {
            Point candidate = destination.ParkingCandidates[parkingIndex];
            parkingIndex++;
            if (!IsParkingCandidateValid(currentLocation, activeHorse, destination, candidate))
                continue;

            pathTarget = new PathSearchTarget(candidate, null);
            pathSearch = new RiderPathSearch(
                Game1.player,
                currentLocation,
                GetTileCenter(candidate),
                ParkingStoppingDistancePixels);
            return true;
        }

        Fail("入口外没有可用停车点");
        return false;
    }

    private void HandleSearchFailure(PathSearchTarget failedTarget)
    {
        if (failedTarget.PortalEdge is null)
        {
            if (destination is not null && parkingIndex < destination.ParkingCandidates.Count)
            {
                statusText = "当前停车点不可达，尝试下一个候选";
                return;
            }

            Fail("入口外停车点不可达");
            return;
        }

        blockedEdges.Add(failedTarget.PortalEdge.Key);
        Replan("计划出口不可达");
    }

    private void Replan(string reason)
    {
        StopRiderController();
        routePlan = null;
        pathSearch = null;
        pathTarget = null;
        edgeIndex = 0;
        parkingIndex = 0;
        state = HorseNavigationState.Planning;
        statusText = "路线发生变化，正在重新规划";
        Log($"navigation-replan reason={reason}");
    }

    private void OnPortalAttempt(OutdoorRouteEdge? edge)
    {
        if (edge is null || routePlan is null || edgeIndex >= routePlan.Edges.Count)
            return;
        if (!string.Equals(routePlan.Edges[edgeIndex].Key, edge.Key, StringComparison.Ordinal))
            return;

        state = HorseNavigationState.WaitingForWarp;
        warpWaitTicks = 0;
        statusText = $"正在通过 {edge.TargetLocation.NameOrUniqueName} 出口";
        DetachRiderControllerForWarp();
        Log($"navigation-warp waiting edge={edge.Key}");
    }

    private void OnControllerStopped(RiderNavigationController? controller, RiderNavigationStopReason reason)
    {
        if (controller is null || !ReferenceEquals(riderController, controller))
            return;

        riderController = null;
        if (reason == RiderNavigationStopReason.Finished && pathTarget?.PortalEdge is null)
        {
            Complete("已到达入口外安全停车点");
            return;
        }

        if (reason == RiderNavigationStopReason.InvalidLocation && state == HorseNavigationState.Planning)
            return;

        Fail(reason == RiderNavigationStopReason.Stuck ? "骑乘路线被阻挡" : "玩家地图发生变化");
    }

    private void Complete(string message)
    {
        StopRiderController();
        pathSearch = null;
        pathTarget = null;
        state = HorseNavigationState.Completed;
        statusText = message;
        Log($"navigation-complete message={message}");
    }

    private void Cancel(string reason)
    {
        StopRiderController();
        pathSearch = null;
        pathTarget = null;
        state = HorseNavigationState.Canceled;
        statusText = reason;
        Log($"navigation-cancel reason={reason}");
    }

    private void Fail(string reason)
    {
        if (!IsActiveState(state) && state != HorseNavigationState.Paused)
            return;

        StopRiderController();
        pathSearch = null;
        pathTarget = null;
        state = HorseNavigationState.Failed;
        statusText = reason;
        Log($"navigation-fail reason={reason}");
    }

    private void Reset(string reason)
    {
        StopRiderController();
        routeGraph = null;
        routePlan = null;
        pathSearch = null;
        pathTarget = null;
        destination = null;
        activeHorse = null;
        blockedEdges.Clear();
        edgeIndex = 0;
        parkingIndex = 0;
        warpWaitTicks = 0;
        state = HorseNavigationState.Idle;
        statusText = "";
        Log($"navigation-reset reason={reason}");
    }

    private void StopRiderController()
    {
        if (riderController is not null && ReferenceEquals(Game1.player.controller, riderController))
        {
            Game1.player.controller = null;
            Game1.player.stopWithoutChangingFrame();
        }

        if (activeHorse is not null)
            activeHorse.stopWithoutChangingFrame();
        horseAnimator.Reset();
        riderController = null;
    }

    // Flow: once a Warp is requested, release the controller before the game's delayed transition begins so it cannot request the same Warp again.
    private void DetachRiderControllerForWarp()
    {
        RiderNavigationController? controller = riderController;
        riderController = null;
        if (controller is not null && ReferenceEquals(Game1.player.controller, controller))
            Game1.player.controller = null;
        Game1.player.stopWithoutChangingFrame();
        if (activeHorse is not null)
            activeHorse.stopWithoutChangingFrame();
        horseAnimator.Reset();
    }

    private void DetachRiderControllerAfterWarp()
    {
        RiderNavigationController? controller = riderController;
        riderController = null;
        if (controller is not null && ReferenceEquals(Game1.player.controller, controller))
            Game1.player.controller = null;
        Game1.player.stopWithoutChangingFrame();
        horseAnimator.Reset();
    }

    private void PauseForMenu()
    {
        if (!IsActiveState(state))
            return;

        pausedState = state;
        state = HorseNavigationState.Paused;
        statusText = "菜单暂停导航";
        Log($"navigation-pause state={pausedState}");
    }

    private bool IsParkingCandidateValid(
        GameLocation location,
        Horse horse,
        HorseNavigationDestination target,
        Point candidate)
    {
        if (!location.isTileOnMap(candidate))
            return false;

        foreach (Point entrance in target.EntranceTiles)
        {
            if (Math.Abs(candidate.X - entrance.X) + Math.Abs(candidate.Y - entrance.Y) < 2)
                return false;
        }

        Rectangle bounds = horse.GetBoundingBox();
        Vector2 currentStanding = horse.getStandingPosition();
        Vector2 targetStanding = GetTileCenter(candidate);
        bounds.Offset(
            (int)(targetStanding.X - currentStanding.X),
            (int)(targetStanding.Y - currentStanding.Y));
        return !location.isCollidingPosition(
            bounds,
            Game1.viewport,
            isFarmer: false,
            damagesFarmer: 0,
            glider: false,
            character: horse,
            pathfinding: true,
            projectile: false,
            ignoreCharacterRequirement: false,
            skipCollisionEffects: true);
    }

    private static bool IsActiveState(HorseNavigationState value)
    {
        return value is HorseNavigationState.Planning
            or HorseNavigationState.Navigating
            or HorseNavigationState.WaitingForWarp;
    }

    private static bool IsNearExpectedLanding(Point actual, Point expected)
    {
        return Math.Abs(actual.X - expected.X) <= 2
            && Math.Abs(actual.Y - expected.Y) <= 2;
    }

    private static bool IsMovementButton(SButton button)
    {
        if (button is SButton.DPadUp
            or SButton.DPadDown
            or SButton.DPadLeft
            or SButton.DPadRight
            or SButton.LeftThumbstickUp
            or SButton.LeftThumbstickDown
            or SButton.LeftThumbstickLeft
            or SButton.LeftThumbstickRight)
        {
            return true;
        }

        InputButton[][] bindings =
        {
            Game1.options.moveUpButton,
            Game1.options.moveRightButton,
            Game1.options.moveDownButton,
            Game1.options.moveLeftButton
        };
        foreach (InputButton[] bindingList in bindings)
        {
            foreach (InputButton binding in bindingList)
            {
                if (binding.mouseLeft && button == SButton.MouseLeft)
                    return true;
                if (binding.mouseRight && button == SButton.MouseRight)
                    return true;
                if (binding.key != Microsoft.Xna.Framework.Input.Keys.None
                    && Enum.TryParse(binding.key.ToString(), out SButton mapped)
                    && mapped == button)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static Rectangle GetHudButtonBounds()
    {
        return new Rectangle(
            Math.Max(12, Game1.uiViewport.Width - 224),
            Math.Max(12, Game1.uiViewport.Height - 94),
            190,
            54);
    }

    private static Vector2 GetTileCenter(Point tile)
    {
        return new Vector2((tile.X + 0.5f) * 64f, (tile.Y + 0.5f) * 64f);
    }

    private void Log(string message)
    {
        monitor.Log($"[HorseFollower] {message}", LogLevel.Trace);
    }

    private sealed record PathSearchTarget(Point TargetTile, OutdoorRouteEdge? PortalEdge);
}
