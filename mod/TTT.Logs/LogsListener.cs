using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using TTT.Public.Action;
using TTT.Public.Behaviors;
using TTT.Public.Extensions;
using TTT.Public.Mod.Logs;
using TTT.Public.Mod.Role;
using TTT.Public.Player;

namespace TTT.Logs;

public class LogsListener(ILogService logService, IPlayerService playerService)
  : IPluginBehavior {
  public void Start(BasePlugin plugin) { }

  [GameEventHandler]
  public HookResult OnPlayerDamage(EventPlayerHurt @event, GameEventInfo info) {
    var victim = @event.Userid;

    if (victim == null) return HookResult.Continue;

    var attackedRole = playerService.GetPlayer(victim).PlayerRole();

    var attacker = @event.Attacker == null ?
      null :
      new Tuple<CCSPlayerController, Role>(@event.Attacker,
        playerService.GetPlayer(@event.Attacker).PlayerRole());

    logService.AddLog(new DamageAction(attacker,
      new Tuple<CCSPlayerController, Role>(victim, attackedRole),
      @event.DmgHealth, ServerExtensions.GetGameRules().RoundTime));

    return HookResult.Continue;
  }

  [GameEventHandler]
  public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info) {
    var victim = @event.Userid;
    var killer = @event.Attacker;

    if (victim == null) return HookResult.Continue;

    var victimRole = playerService.GetPlayer(victim).PlayerRole();

    if (killer == null) {
      logService.AddLog(new DeathAction(
        new Tuple<CCSPlayerController, Role>(victim, victimRole)));
      return HookResult.Continue;
    }

    var killerRole = playerService.GetPlayer(killer).PlayerRole();

    logService.AddLog(new KillAction(
      new Tuple<CCSPlayerController, Role>(killer, killerRole),
      new Tuple<CCSPlayerController, Role>(victim, victimRole)));
    return HookResult.Continue;
  }
}