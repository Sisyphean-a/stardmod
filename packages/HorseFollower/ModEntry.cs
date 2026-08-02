using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace HorseFollower;

public sealed class ModEntry : Mod
{
    private HorseFollowerService Service = null!;

    public override void Entry(IModHelper helper)
    {
        ModConfig config = helper.ReadConfig<ModConfig>();
        Service = new HorseFollowerService(config, Monitor);

        helper.Events.GameLoop.DayStarted += Service.OnDayStarted;
        helper.Events.GameLoop.UpdateTicking += Service.OnUpdateTicking;
        helper.Events.GameLoop.UpdateTicked += Service.OnUpdateTicked;
        helper.Events.Player.Warped += Service.OnPlayerWarped;
    }
}
