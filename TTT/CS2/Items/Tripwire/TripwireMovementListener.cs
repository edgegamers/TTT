using System.Diagnostics.CodeAnalysis;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using RayTraceAPI;
using ShopAPI.Configs.Traitor;
using TTT.API;
using TTT.API.Events;
using TTT.API.Game;
using TTT.API.Player;
using TTT.API.Storage;
using TTT.CS2.API.Items;
using TTT.CS2.Extensions;
using TTT.CS2.ThirdParties.eGO;
using TTT.Game.Events.Body;
using TTT.Game.Events.Game;
using TTT.Game.Events.Player;
using TTT.Game.Listeners;
using TTT.Game.Roles;
using TTT.Karma;

namespace TTT.CS2.Items.Tripwire;

public class TripwireMovementListener(IServiceProvider provider)
  : BaseListener(provider), IPluginModule, ITripwireActivator {
  private readonly IKarmaUpdateManager karmaUpdateManager =
    provider.GetRequiredService<IKarmaUpdateManager>();

  private readonly IPlayerConverter<CCSPlayerController> converter =
    provider.GetRequiredService<IPlayerConverter<CCSPlayerController>>();

  private readonly Dictionary<string, TripwireInstance> killedWithTripwire =
    new();

  private readonly ITripwireTracker? tripwireTracker =
    provider.GetService<ITripwireTracker>();

  private TripwireConfig config
    => Provider.GetService<IStorage<TripwireConfig>>()
    ?.Load()
     .GetAwaiter()
     .GetResult() ?? new TripwireConfig();

  public void Start(BasePlugin? plugin) {
    if (tripwireTracker == null) return;
    plugin?.AddTimer(0.2f, checkTripwires, TimerFlags.REPEAT);
  }

  public void ActivateTripwire(TripwireInstance instance) {
    tripwireTracker?.RemoveTripwire(instance);
    instance.TripwireProp.EmitSound("Flashbang.ExplodeDistant");

    foreach (var player in Finder.GetOnline()) {
      if (!dealTripwireDamage(instance, player, out var gamePlayer)) continue;
      gamePlayer.EmitSound("Player.BurnDamage");
    }
  }

  private void checkTripwires() {
    if (tripwireTracker == null) return;
    foreach (var wire in new List<TripwireInstance>(tripwireTracker
     .ActiveTripwires)) {
      var direction = wire.EndPos - wire.StartPos;
      var angle     = direction.Normalized().toAngle();

      EgoApi.RAY_TRACE.Get()!.TraceShape(wire.StartPos, angle, null,
        new TraceOptions {
          DrawBeam         = 0,
          InteractsWith    = (ulong)InteractionLayers.MASK_SHOT_FULL,
          InteractsExclude = (ulong)InteractionLayers.NoDraw
        }, out var result);

      // The ray hits the player PAWN; resolve the controller from it before
      // looking up the player (SteamID/Index read off the pawn are garbage).
      if (!result.DidHit
        || !result.TryGetHitEntityByDesignerName<CCSPlayerPawn>("player",
          out var pawn) || pawn == null)
        continue;

      var hitController = pawn.Controller.Value?.As<CCSPlayerController>();

      if (!config.FriendlyFireTriggers && hitController is { IsValid: true }) {
        var apiPlayer = converter.GetPlayer(hitController);
        var role      = Roles.GetRoles(apiPlayer);
        if (role.Any(r => r is TraitorRole)) continue;
      }

      ActivateTripwire(wire);
    }
  }

  private float getDamage(float distance) {
    return config.ExplosionPower
      * MathF.Pow(MathF.E, -distance * config.FalloffDelay);
  }

  private int getDamage(CCSPlayerController gamePlayer, IOnlinePlayer player,
    Vector tripwire) {
    var origin = gamePlayer.Pawn.Value?.AbsOrigin;
    if (origin == null) return 0;
    var distance = tripwire.Distance(origin);
    var damage   = getDamage(distance);
    if (Roles.GetRoles(player).Any(r => r is TraitorRole))
      damage *= config.FriendlyFireMultiplier;

    return (int)Math.Floor(damage);
  }

  [UsedImplicitly]
  [EventHandler]
  public void OnGameEnd(GameStateUpdateEvent ev) {
    if (ev.NewState != State.FINISHED) return;

    killedWithTripwire.Clear();
  }

  [UsedImplicitly]
  [EventHandler]
  public void OnRagdollSpawn(BodyCreateEvent ev) {
    if (!killedWithTripwire.TryGetValue(ev.Body.Id, out var info)) return;
    if (ev.Body.Killer != null && ev.Body.Killer.Id != ev.Body.OfPlayer.Id)
      return;
    ev.Body.Killer = info.owner;
  }

  private bool dealTripwireDamage(TripwireInstance instance,
    IOnlinePlayer player,
    [NotNullWhen(true)] out CCSPlayerController? gamePlayer) {
    gamePlayer = null;
    if (!player.IsAlive) return false;

    gamePlayer = converter.GetPlayer(player);
    if (gamePlayer == null) return false;
    var damage = getDamage(gamePlayer, player, instance.StartPos);

    if (damage < 1) return false;

    Event ev;
    if (player.Health - damage <= 0) {
      killedWithTripwire[player.Id] = instance;
      ev = new PlayerDeathEvent(player).WithKiller(instance.owner)
       .WithWeapon("[Tripwire]");

      if (config.FriendlyFireKarmaPenaltyTime != -1
        && Roles.GetRoles(player).Any(r => r is TraitorRole)
        && (DateTime.Now - instance.placedAt).TotalSeconds
        > config.FriendlyFireKarmaPenaltyTime) {
        karmaUpdateManager.IgnoreEvent(ev);
      }
    } else {
      ev = new PlayerDamagedEvent(player, instance.owner, damage) {
        Weapon = "[Tripwire]"
      };
    }

    Bus.Dispatch(ev);

    player.Health -= damage;
    return true;
  }
}