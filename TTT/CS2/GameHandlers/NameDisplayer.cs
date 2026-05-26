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

      if (!result.TryGetHitEntityByDesignerName<CCSPlayerController>(
        "player",
        out var hitPlayer))
        return;
      
      if (hitPlayer == null) return;
      
      player.PrintToCenterAlert($"{hitPlayer.PlayerName}");
    }
  }
}