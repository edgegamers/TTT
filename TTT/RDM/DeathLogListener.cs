using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using TTT.API.Events;
using TTT.API.Game;
using TTT.API.Storage;
using TTT.Game.Damage;
using TTT.Game.Events.Game;
using TTT.Game.Events.Player;
using TTT.Game.Listeners;
using TTT.RDM.lang;

namespace TTT.RDM;

public class DeathLogListener(IServiceProvider provider)
  : BaseListener(provider) {
  private readonly IRdmStore store = provider.GetRequiredService<IRdmStore>();

  private readonly IDamageTracker damage =
    provider.GetRequiredService<IDamageTracker>();

  public int CurrentRound { get; private set; }

  private RdmConfig config
    => Provider.GetService<IStorage<RdmConfig>>()?.Load().GetAwaiter()
      .GetResult() ?? new RdmConfig();

  [EventHandler]
  [UsedImplicitly]
  public void OnRoundStart(GameStateUpdateEvent ev) {
    if (ev.NewState == State.IN_PROGRESS) CurrentRound++;
  }

  [EventHandler]
  [UsedImplicitly]
  public void OnKill(PlayerDeathEvent ev) {
    if (Games.ActiveGame is not { State: State.IN_PROGRESS }) return;
    var victim = ev.Victim;
    var killer = ev.Killer;
    if (killer == null || victim.Id == killer.Id) return;

    var victimRole = Roles.GetRoles(victim).First();
    var killerRole = Roles.GetRoles(killer).First();
    var suspect    = RdmClassifier.IsSuspectKill(killerRole, victimRole);
    var fault      = damage.GetFault(killer.Id, victim.Id);

    var record = new Models.DeathRecord {
      Round = CurrentRound,
      VictimId = victim.Id, VictimName = victim.Name,
      VictimRole = victimRole.Name,
      AttackerId = killer.Id, AttackerName = killer.Name,
      AttackerRole = killerRole.Name, Weapon = ev.Weapon,
      Timestamp = DateTime.UtcNow, IsSuspect = suspect, Fault = fault
    };

    _ = store.AddDeath(record);

    if (suspect && config.AutoPromptOnSuspectKill)
      Messenger.Message(victim, Locale[RdmMsgs.RDM_PROMPT(killer.Name)]);
  }
}
