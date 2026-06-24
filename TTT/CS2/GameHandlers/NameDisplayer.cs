using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using RayTraceAPI;
using TTT.API;
using TTT.CS2.Extensions;

namespace TTT.CS2.GameHandlers;

public class NameDisplayer : IPluginModule {
  public void Dispose() { }
  public void Start() { }

  public void Start(BasePlugin? plugin) {
    if (OperatingSystem.IsWindows()) return;
    plugin?.AddTimer(0.25f, showNames, TimerFlags.REPEAT);
  }

  private void showNames() {
    foreach (var player in Utilities.GetPlayers()) {
      if (player.GetHealth() <= 0) continue;

      var result = player.GetGameTraceByEyePosition(new TraceOptions {
        DrawBeam         = 0,
        InteractsWith    = (ulong)InteractionLayers.Player,
        InteractsExclude = (ulong)InteractionLayers.NoDraw
      });

      // The ray hits the player PAWN, not the controller. Reading PlayerName
      // off the pawn pointer yields garbage (a stray glyph), so resolve the
      // controller from the pawn first. Use continue, not return, so every
      // player is processed (return bailed the whole loop on the first miss).
      if (!result.TryGetHitEntityByDesignerName<CCSPlayerPawn>("player",
        out var pawn) || pawn == null)
        continue;

      var hitPlayer = pawn.Controller.Value?.As<CCSPlayerController>();
      if (hitPlayer == null || !hitPlayer.IsValid) continue;

      player.PrintToCenterAlert(hitPlayer.PlayerName);
    }
  }
}