using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace HorseFollower;

public sealed class ModEntry : Mod
{
    private HorseFollowerService Service = null!;
    private HorseNavigationService Navigation = null!;

    public override void Entry(IModHelper helper)
    {
        ModConfig config = helper.ReadConfig<ModConfig>();
        Service = new HorseFollowerService(config, Monitor);
        Navigation = new HorseNavigationService(helper, config, Monitor);

        helper.Events.GameLoop.DayStarted += Service.OnDayStarted;
        helper.Events.GameLoop.UpdateTicking += Service.OnUpdateTicking;
        helper.Events.GameLoop.UpdateTicked += Service.OnUpdateTicked;
        helper.Events.Player.Warped += Service.OnPlayerWarped;

        helper.Events.GameLoop.DayStarted += Navigation.OnDayStarted;
        helper.Events.GameLoop.ReturnedToTitle += Navigation.OnReturnedToTitle;
        helper.Events.GameLoop.UpdateTicking += Navigation.OnUpdateTicking;
        helper.Events.GameLoop.UpdateTicked += Navigation.OnUpdateTicked;
        helper.Events.Player.Warped += Navigation.OnPlayerWarped;
        helper.Events.Display.MenuChanged += Navigation.OnMenuChanged;
        helper.Events.Display.RenderedHud += Navigation.OnRenderedHud;
        helper.Events.Input.ButtonPressed += Navigation.OnButtonPressed;
    }
}
