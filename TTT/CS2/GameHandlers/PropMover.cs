using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.DependencyInjection;
using RayTraceAPI;
using TTT.API;
using TTT.API.Events;
using TTT.API.Player;
using TTT.CS2.Events;
using TTT.CS2.Extensions;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace TTT.CS2.GameHandlers;

public class PropMover(IServiceProvider provider) : IPluginModule {
  // TODO: Make this configurable
  public static readonly float MAX_DISTANCE = 200;
  public static readonly float MIN_HOLDING_DISTANCE = 80;
  public static readonly float MAX_HOLDING_DISTANCE = 150;

  private static readonly QAngle DEAD_ANGLE = new(90, 45, 90);
  private readonly IEventBus bus = provider.GetRequiredService<IEventBus>();

  private readonly IPlayerConverter<CCSPlayerController> converter =
    provider.GetRequiredService<IPlayerConverter<CCSPlayerController>>();

  private readonly Dictionary<CCSPlayerController, MovementInfo>
    playersPressingE = new();

  public void Dispose() { }

  public void Start() { }

  public void Start(BasePlugin? plugin, bool hotReload) {
    plugin?.AddTimer(Server.TickInterval, refreshLines, TimerFlags.REPEAT);
    plugin?.RegisterListener<CounterStrikeSharp.API.Core.Listeners.OnTick>(
      refreshHeld);
    plugin
    ?.RegisterListener<
        CounterStrikeSharp.API.Core.Listeners.OnPlayerButtonsChanged>(
        onButtonsChanged);
  }

  private void onButtonsChanged(CCSPlayerController player,
    PlayerButtons pressed, PlayerButtons released) {
    if (playersPressingE.TryGetValue(player, out var heldItem)) {
      onCeaseUse(player, released, heldItem);
      return;
    }

    if (!pressed.HasFlag(PlayerButtons.Use)
      && !pressed.HasFlag(PlayerButtons.Inspect))
      return;

    onStartUse(player);
  }

  private void onStartUse(CCSPlayerController player) {
    var playerPos = player.PlayerPawn.Value?.AbsOrigin;
    if (playerPos == null) return;
    var result = player.GetGameTraceByEyePosition(new TraceOptions {
      DrawBeam         = 0,
      InteractsWith    = (ulong)InteractionLayers.MASK_SHOT_FULL,
      InteractsExclude = (ulong)InteractionLayers.NoDraw
    });

    if (!result.DidHit) return;

    // A body is a prop_ragdoll, not a "player" entity, so don't pre-filter the
    // hit on a "player" designer name (that guard blocked all body pickup and
    // identification). The ragdoll lookups below are guarded inside the
    // extension, and the body tracker is the real "is this a body?" filter.
    result.TryGetHitEntityByDesignerName<CBaseEntity>("prop_ragdoll",
      out var hitEntity);

    if (hitEntity == null || !hitEntity.IsValid)
      result.TryGetHitEntityByDesignerName<CBaseEntity>(
        "prop_physics_multiplayer", out hitEntity);

    var playerDist = playerPos.Distance(result.EndPos.toVector());
    if (playerDist > MAX_DISTANCE) return;
    if (hitEntity == null) return;

    var apiPlayer   = converter.GetPlayer(player);
    var pickupEvent = new PropPickupEvent(apiPlayer, hitEntity);
    bus.Dispatch(pickupEvent);
    if (pickupEvent.IsCanceled) return;

    playersPressingE[player] = new MovementInfo(playerDist, pickupEvent.Prop) {
      Beam = createBeam(playerPos, pickupEvent.Prop.AbsOrigin ?? Vector.Zero)
    };
    pickupEvent.Prop.AcceptInput("DisableMotion");
  }

  private void onCeaseUse(CCSPlayerController player, PlayerButtons released,
    MovementInfo heldItem) {
    if (!released.HasFlag(PlayerButtons.Use)) return;
    playersPressingE.Remove(player);
    if (!heldItem.Ragdoll.IsValid) return;
    if (heldItem.Beam != null && heldItem.Beam.IsValid)
      heldItem.Beam.Remove();
    // Check if any other players are still holding this ragdoll
    foreach (var (_, info) in playersPressingE)
      if (info.Ragdoll == heldItem.Ragdoll)
        return;
    heldItem.Ragdoll.AcceptInput("EnableMotion");
  }

  private void refreshHeld() {
    foreach (var (player, info) in playersPressingE) refreshHeld(player, info);
  }

  private void refreshHeld(CCSPlayerController player, MovementInfo info) {
    var ent = info.Ragdoll;
    if (!player.IsValid || !ent.IsValid || ent.AbsOrigin == null) {
      playersPressingE.Remove(player);
      return;
    }

    var playerPawn = player.PlayerPawn.Value;
    if (playerPawn == null || !playerPawn.IsValid) return;
    var playerOrigin = player.GetEyePosition();

    if (playerOrigin == null) {
      playersPressingE.Remove(player);
      return;
    }

    var result = player.GetGameTraceByEyePosition(new TraceOptions {
      DrawBeam         = 0,
      InteractsWith    = (ulong)InteractionLayers.MASK_SHOT_FULL,
      InteractsExclude = (ulong)InteractionLayers.NoDraw
    });

    var isOnSelf =
      result.TryGetHitEntityByDesignerName<CBaseEntity>(ent.DesignerName,
        out _);

    var endPos = result.EndPos.toVector();

    if (isOnSelf || result.EndPos.toVector().Distance(playerOrigin)
      > MAX_HOLDING_DISTANCE)
      endPos = playerOrigin
        + playerPawn.EyeAngles.ToForward() * MAX_HOLDING_DISTANCE;

    if (ent.DesignerName == "prop_physics_multiplayer") {
      ent.Teleport(endPos, QAngle.Zero, Vector.Zero);
      return;
    }

    moveBody(endPos, ent);
  }

  private void moveBody(Vector endPos, CBaseEntity ent) {
    var deadRot = DEAD_ANGLE.Clone()!;

    var rotDeg = Server.CurrentTime * 64f % 360;
    var rotRad = (rotDeg + 0) * (MathF.PI / 180);
    deadRot.Y += rotDeg;

    var xOff  = MathF.Cos(rotRad) * 32;
    var yOff  = MathF.Sin(rotRad) * 32;
    var xBias = MathF.Cos(rotRad + MathF.PI / 2) * 16;
    var yBias = MathF.Sin(rotRad + MathF.PI / 2) * 16;
    endPos.X += xBias;
    endPos.Y += yBias;

    endPos -= new Vector(xOff, yOff, 0);

    ent.Teleport(endPos, deadRot, Vector.Zero);
  }

  private void refreshLines() {
    foreach (var (player, info) in playersPressingE) refreshLines(player, info);
  }

  private void refreshLines(CCSPlayerController player, MovementInfo info) {
    var ent = info.Ragdoll;
    if (!player.IsValid || !ent.IsValid) {
      playersPressingE.Remove(player);
      return;
    }

    var playerPawn = player.PlayerPawn.Value;
    if (playerPawn == null || !playerPawn.IsValid) return;
    var playerOrigin = playerPawn.AbsOrigin;
    if (playerOrigin == null) {
      playersPressingE.Remove(player);
      return;
    }

    playerOrigin   =  playerOrigin.Clone()!;
    playerOrigin.Z += 64;

    var eyeAngles = playerPawn.EyeAngles;

    var targetVector = playerOrigin + eyeAngles.Clone()!.ToForward()
      * Math.Clamp(info.Distance, MIN_HOLDING_DISTANCE, MAX_HOLDING_DISTANCE);

    targetVector.Z = Math.Max(targetVector.Z, playerOrigin.Z - 48);

    if (ent.AbsOrigin == null) return;

    if (info.Beam != null && info.Beam.IsValid) {
      info.Beam.Remove();
      info.Beam = createBeam(playerOrigin.With(z: playerOrigin.Z - 16),
        ent.AbsOrigin);
    }

    playersPressingE[player] = info;
  }

  private CEnvBeam? createBeam(Vector start, Vector end) {
    var beam = Utilities.CreateEntityByName<CEnvBeam>("env_beam");
    if (beam == null) return null;
    beam.RenderMode = RenderMode_t.kRenderTransAlpha;
    beam.Width      = 2.0f;
    beam.Render     = Color.FromArgb(32, Color.White);
    beam.EndPos.X   = end.X;
    beam.EndPos.Y   = end.Y;
    beam.EndPos.Z   = end.Z;
    beam.Teleport(start);
    // Spawn the beam so it's a fully-registered entity; otherwise Remove()/Kill
    // don't reliably destroy it and the beam lingers.
    beam.DispatchSpawn();
    return beam;
  }
}