using System.Diagnostics.CodeAnalysis;
using System.Reactive.Concurrency;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using RayTraceAPI;
using ShopAPI;
using ShopAPI.Configs;
using ShopAPI.Configs.Traitor;
using TTT.API;
using TTT.API.Events;
using TTT.API.Extensions;
using TTT.API.Game;
using TTT.API.Player;
using TTT.API.Storage;
using TTT.CS2.API.Items;
using TTT.CS2.Extensions;
using TTT.CS2.ThirdParties.eGO;
using TTT.Game.Events.Game;
using TTT.Game.Roles;

namespace TTT.CS2.Items.Tripwire;

public static class TripwireServiceCollection {
  public static void AddTripwireServices(this IServiceCollection services) {
    services.AddModBehavior<ITripwireTracker, TripwireItem>();
    services.AddModBehavior<ITripwireActivator, TripwireMovementListener>();
    services.AddModBehavior<TripwireDamageListener>();
    services.AddModBehavior<TripwireDefuserListener>();
  }
}

public class TripwireItem(IServiceProvider provider)
  : RoleRestrictedItem<TraitorRole>(provider), IPluginModule, ITripwireTracker {
  private readonly IPlayerConverter<CCSPlayerController> converter =
    provider.GetRequiredService<IPlayerConverter<CCSPlayerController>>();

  private readonly IScheduler scheduler =
    provider.GetRequiredService<IScheduler>();

  private TripwireConfig config
    => Provider.GetService<IStorage<TripwireConfig>>()
    ?.Load()
     .GetAwaiter()
     .GetResult() ?? new TripwireConfig();

  public override string Name => Locale[TripwireMsgs.SHOP_ITEM_TRIPWIRE];

  public override string Description
    => Locale[TripwireMsgs.SHOP_ITEM_TRIPWIRE_DESC];

  public override ShopItemConfig Config => config;

  public void Start(BasePlugin? plugin) {
    Start();
    plugin
    ?.RegisterListener<
        CounterStrikeSharp.API.Core.Listeners.OnServerPrecacheResources>(
        onPrecache);
  }

  public List<TripwireInstance> ActiveTripwires { get; } = [];

  public void RemoveTripwire(TripwireInstance instance) {
    instance.Beam.Remove();
    instance.TripwireProp.Remove();
    ActiveTripwires.Remove(instance);
  }

  private void onPrecache(ResourceManifest manifest) {
    manifest.AddResource(
      "models/generic/conveyor_control_panel_01/conveyor_button_02.vmdl");
  }

  [UsedImplicitly]
  [EventHandler]
  public void OnGameEvent(GameStateUpdateEvent ev) {
    if (ev.NewState != State.FINISHED) return;
    ActiveTripwires.Clear();
  }

  public override void OnPurchase(IOnlinePlayer player) {
    Server.NextWorldUpdate(() => {
      if (!placeTripwire(player, out var originTrace, out var endTrace,
        out var tripwire))
        return;

      scheduler.Schedule(config.TripwireInitiationTime,
        () => {
          Server.NextWorldUpdate(() => {
            createTripwireBeam(player, tripwire,
              originTrace.Value.EndPos.toVector(),
              endTrace.Value.EndPos.toVector());
          });
        });
    });
  }

  private bool placeTripwire(IOnlinePlayer player,
    [NotNullWhen(true)] out TraceResult? originTrace,
    [NotNullWhen(true)] out TraceResult? endTrace,
    [NotNullWhen(true)] out CDynamicProp? tripwire) {
    tripwire    = null;
    originTrace = null;
    endTrace    = null;
    var gamePlayer = converter.GetPlayer(player);
    var playerPawn = gamePlayer?.PlayerPawn.Value;
    if (gamePlayer == null || playerPawn == null) return false;

    originTrace = gamePlayer.GetGameTraceByEyePosition(new TraceOptions {
      DrawBeam         = 0,
      InteractsWith    = (ulong)InteractionLayers.MASK_SHOT_FULL,
      InteractsExclude = (ulong)InteractionLayers.NoDraw});
    var origin = gamePlayer.GetEyePosition();
    if (origin == null) return false;

    if (origin.DistanceSquared(originTrace.Value.EndPos.toVector())
      > config.MaxPlacementDistanceSquared) {
      Shop.AddBalance(player, config.Price, "Refund");
      Messenger.Message(player, Locale[TripwireMsgs.SHOP_ITEM_TRIPWIRE_TOOFAR]);
      return false;
    }

    var angles = originTrace.Value.Normal.toVector().toAngle();

    // Ignore the player's PAWN, not the controller. TraceShape dereferences the
    // ignore entity's collidable to build the trace filter; a CCSPlayerController
    // has no collidable, so passing it crashes the server inside
    // CRayTrace::TraceShapeInternal. The pawn is the body we want excluded anyway.
    var isSuccess = EgoApi.RAY_TRACE.Get()!
     .TraceShape(originTrace.Value.EndPos.toVector(), angles, playerPawn,
        new TraceOptions {
          DrawBeam         = 0,
          InteractsWith    = (ulong)InteractionLayers.MASK_SHOT_FULL,
          InteractsExclude = (ulong)InteractionLayers.NoDraw
        }, out var result);
    if (!isSuccess) return false;
    endTrace = result;

    tripwire = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
    if (tripwire == null) return false;

    // DispatchSpawn must run before SetModel: SetModel -> SetupModel asserts the
    // entity is no longer in the staging list (EF_IN_STAGING_LIST), which is
    // only cleared by DispatchSpawn. Setting the (skeletal) conveyor model while
    // still staged hard-crashes the server.
    tripwire.DispatchSpawn();
    tripwire.SetModel(
      "models/generic/conveyor_control_panel_01/conveyor_button_02.vmdl");

    tripwire.Teleport(originTrace.Value.EndPos.toVector(),
      originTrace.Value.Normal.toVector().toAngle());
    tripwire.EmitSound("Weapon_ELITE.Clipout");
    return true;
  }

  private void createTripwireBeam(IOnlinePlayer owner, CDynamicProp prop,
    Vector start, Vector end) {
    prop.EmitSound("C4.ExplodeTriggerTrip");
    var beam = createBeamEnt(start, end);
    if (beam == null) return;

    var instance = new TripwireInstance(owner, beam, prop, start, end, DateTime.UtcNow);
    ActiveTripwires.Add(instance);
  }

  private CEnvBeam? createBeamEnt(Vector start, Vector end) {
    var beam = Utilities.CreateEntityByName<CEnvBeam>("env_beam");
    if (beam == null) return null;
    beam.RenderMode = RenderMode_t.kRenderTransAlpha;
    beam.Width      = config.TripwireThickness;
    beam.Render     = config.TripwireColor;
    beam.EndPos.X   = end.X;
    beam.EndPos.Y   = end.Y;
    beam.EndPos.Z   = end.Z;
    beam.Teleport(start);
    return beam;
  }
}