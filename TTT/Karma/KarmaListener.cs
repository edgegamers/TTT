using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using TTT.API.Events;
using TTT.API.Game;
using TTT.API.Role;
using TTT.API.Storage;
using TTT.Game.Events.Game;
using TTT.Game.Events.Player;
using TTT.Game.Listeners;
using TTT.Game.Roles;

namespace TTT.Karma;

public class KarmaListener(IServiceProvider provider) : BaseListener(provider) {
  private readonly Dictionary<string, int> badKills = new();
  private readonly List<(string, string)> firstDamage = new();

  private readonly IGameManager games =
    provider.GetRequiredService<IGameManager>();

  private readonly IRoleAssigner roles =
    provider.GetRequiredService<IRoleAssigner>();
  
  private readonly IKarmaUpdateManager karmaUpdateManager = provider.GetRequiredService<IKarmaUpdateManager>();

  public bool GiveKarmaOnRoundEnd = true;

  private KarmaConfig config
    => Provider.GetService<IStorage<KarmaConfig>>()
    ?.Load()
     .GetAwaiter()
     .GetResult() ?? new KarmaConfig();

  [EventHandler]
  [UsedImplicitly]
  public void OnRoundStart(GameStateUpdateEvent ev) {
    karmaUpdateManager.ClearIgnores();
    badKills.Clear();
    firstDamage.Clear();
  }

  [EventHandler]
  [UsedImplicitly]
  public void OnHurt(PlayerDamagedEvent ev) {
    if (games.ActiveGame is not { State: State.IN_PROGRESS }) return;

    var victim   = ev.Player;
    var attacker = ev.Attacker;
    if (attacker == null) return;

    // If the victim already damaged the attacker, don't mark this as first damage.
    if (firstDamage.Contains((victim.Id, attacker.Id))) return;
    
    // Otherwise, mark down that the attacker damaged the victim first.
    var pairing = (attacker.Id, victim.Id);
    if (!firstDamage.Contains(pairing))
      firstDamage.Add(pairing);
  }

  [EventHandler]
  [UsedImplicitly]
  public void OnKill(PlayerDeathEvent ev) {
    if (games.ActiveGame is not { State: State.IN_PROGRESS }) return;

    var victim = ev.Victim;
    var killer = ev.Killer;

    if (killer == null) return;
    if (victim.Id == killer.Id) return;
    
    var killerIsGuilty = firstDamage.Contains((killer.Id, victim.Id));
    var victimIsGuilty = firstDamage.Contains((victim.Id, killer.Id));
    if (!killerIsGuilty && !victimIsGuilty) {
      Console.WriteLine(
        $"Neither {killer.Name} nor {victim.Name} damaged each other on kill -- how did this happen?");
      return;
    }

    var victimRole = roles.GetRoles(victim).First();
    var killerRole = roles.GetRoles(killer).First();

    var victimKarmaDelta = 0;
    var killerKarmaDelta = 0;

    var attackerKarmaMultiplier = 1;

    if (victimRole is TraitorRole == killerRole is TraitorRole) {
      if (killerIsGuilty)
        badKills[killer.Id] = badKills.GetValueOrDefault(killer.Id, 0) + 1;
      attackerKarmaMultiplier = badKills.GetValueOrDefault(killer.Id, 1);
    }

    switch (victimRole) {
      case InnocentRole when killerRole is TraitorRole:
        return;
      case InnocentRole:
        victimKarmaDelta = victimIsGuilty ?
          config.INNO_ON_INNO_VICTIM_GUILTY :
          config.INNO_ON_INNO_VICTIM_INNOCENT;
        killerKarmaDelta = killerIsGuilty ?
          config.INNO_ON_INNO_GUILTY :
          config.INNO_ON_INNO_INNOCENT;
        break;
      case TraitorRole:
        if (killerRole is TraitorRole) {
          victimKarmaDelta = victimIsGuilty ?
            config.TRAITOR_ON_TRAITOR_VICTIM_GUILTY :
            config.TRAITOR_ON_TRAITOR_VICTIM_INNOCENT;
          killerKarmaDelta = killerIsGuilty ?
            config.TRAITOR_ON_TRAITOR_GUILTY :
            config.TRAITOR_ON_TRAITOR_INNOCENT; 
        } else killerKarmaDelta = config.INNO_ON_TRAITOR;
        break;
      case DetectiveRole:
        if (killerRole is TraitorRole) killerKarmaDelta = config.TRAITOR_ON_DETECTIVE;
        else {
          victimKarmaDelta = victimIsGuilty ?
            config.INNO_ON_DETECTIVE_VICTIM_GUILTY :
            config.INNO_ON_DETECTIVE_VICTIM_INNOCENT;
          killerKarmaDelta = killerIsGuilty ?
            config.INNO_ON_DETECTIVE_GUILTY :
            config.INNO_ON_DETECTIVE_INNOCENT;
        }
        break;
    }

    killerKarmaDelta *= attackerKarmaMultiplier;
    
    karmaUpdateManager.QueueUpdate(killer, killerKarmaDelta, ev, $"Killed {victimRole.Name}");
    karmaUpdateManager.QueueUpdate(victim, victimKarmaDelta, ev, $"Killed by {killerRole.Name}");
  }

  [UsedImplicitly]
  [EventHandler]
  public void OnRoundEnd(GameStateUpdateEvent ev) {
    if (ev.NewState != State.FINISHED) return;

    var winner = ev.Game.WinningRole;
    if (GiveKarmaOnRoundEnd)
      foreach (var player in ev.Game.Players)
        if (Roles.GetRoles(player).Any(r => r.GetType() == winner?.GetType()))
          karmaUpdateManager.QueueUpdate(player, config.KarmaPerRoundWin, ev, "Round Won");
        else
          karmaUpdateManager.QueueUpdate(player, config.KarmaPerRound, ev, "Round Played");

    Task.Run(async () => await karmaUpdateManager.ProcessUpdatesAsync());
  }
}