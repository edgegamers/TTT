using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.DependencyInjection;
using TTT.Public.Behaviors;
using TTT.Public.Extensions;
using TTT.Public.Formatting;
using TTT.Public.Mod.Logs;
using TTT.Public.Mod.Role;
using TTT.Public.Mod.Round;

namespace TTT.Round;

public class RoundBehavior(IServiceProvider provider, ILogService logs)
  : IRoundService, IPluginBehavior {
  private Round? _round;
  private RoundStatus _roundStatus = RoundStatus.Paused;
  private IRoleService _roleService = null!;

  public void Start(BasePlugin plugin) {
    _roleService = provider.GetRequiredService<IRoleService>();
    plugin.RegisterListener<Listeners.OnTick>(TickWaiting);
    plugin.AddCommandListener("jointeam", OnTeamJoin);
    VirtualFunctions.CBaseEntity_TakeDamageOldFunc.Hook(BlockDamage,
      HookMode.Pre);
    plugin.AddTimer(3, EndRound, TimerFlags.REPEAT);
  }

  public void Dispose() {
    VirtualFunctions.CBaseEntity_TakeDamageOldFunc.Unhook(BlockDamage,
      HookMode.Pre);
  }

  public RoundStatus GetRoundStatus() { return _roundStatus; }

  public void SetRoundStatus(RoundStatus roundStatus) {
    switch (roundStatus) {
      case RoundStatus.Ended:
        ForceEnd();
        break;
      case RoundStatus.Waiting:
        _round = new Round(_roleService, null, logs.CreateRound());
        break;
      case RoundStatus.Started:
        ForceStart();
        break;
    }

    _roundStatus = roundStatus;
  }

  public void TickWaiting() {
    if (_round == null) {
      _round = new Round(_roleService, null, logs.CreateRound());
      return;
    }

    if (_roundStatus != RoundStatus.Waiting) return;

    _round.Tick();

    if (_round.GraceTime() != 0) return;

    if (Utilities.GetPlayers()
     .Where(player => player is { IsValid: true, PawnIsAlive: true })
     .ToList()
     .Count <= 2) {
      Server.PrintToChatAll(StringUtils.FormatTTT(
        "Not enough players to start the round. We will wait for more players."));

      SetRoundStatus(RoundStatus.Paused);
      return;
    }

    SetRoundStatus(RoundStatus.Started);
  }

  public void ForceStart() {
    foreach (var player in Utilities.GetPlayers().ToList())
      player.VoiceFlags = VoiceFlags.Normal;
    _round!.Start();
    ServerExtensions.GetGameRules().RoundTime = 360;
    Utilities.SetStateChanged(ServerExtensions.GetGameRulesProxy(),
      "CCSGameRulesProxy", "m_pGameRules");
  }

  public void ForceEnd() {
    if (_roundStatus == RoundStatus.Ended) return;
    _roundStatus = RoundStatus.Ended;
    Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
     .First()
     .GameRules!.TerminateRound(5, RoundEndReason.RoundDraw);
  }

  private void EndRound() {
    if (_roundStatus == RoundStatus.Started
      && Utilities.GetPlayers().Count(player => player.PawnIsAlive) == 1)
      ForceEnd();

    var traitorCount =
      _roleService.GetTraitors().Count(player => player.PawnIsAlive);
    var innocentCount =
      _roleService.GetInnocents().Count(player => player.PawnIsAlive);
    var detectiveCount = _roleService.GetDetectives()
     .Count(player => player.PawnIsAlive);

    if (_roundStatus == RoundStatus.Started
      && (traitorCount == 0 || innocentCount + detectiveCount == 0))
      ForceEnd();
  }

  private HookResult BlockDamage(DynamicHook hook) {
    if (hook.GetParam<CEntityInstance>(0).DesignerName is not "player")
      return HookResult.Continue;
    return _roundStatus != RoundStatus.Waiting ?
      HookResult.Continue :
      HookResult.Stop;
  }

  private HookResult
    OnTeamJoin(CCSPlayerController? executor, CommandInfo info) {
    if (_roundStatus != RoundStatus.Started) return HookResult.Continue;
    if (executor == null) return HookResult.Continue;
    if (_roleService.GetRole(executor) != Role.Unassigned)
      return HookResult.Continue;
    Server.NextFrame(() => executor.CommitSuicide(false, true));

    return HookResult.Continue;
  }
}