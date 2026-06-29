using Microsoft.Extensions.DependencyInjection;
using TTT.API.Events;
using TTT.API.Game;
using TTT.API.Player;
using TTT.API.Role;
using TTT.Game.Damage;
using TTT.Game.Events.Player;
using TTT.Game.Roles;
using TTT.Locale;
using TTT.RDM;
using TTT.RDM.lang;
using Xunit;

namespace TTT.Test.RDM;

public class DeathLogListenerTests {
  private readonly IEventBus bus;
  private readonly IGameManager games;
  private readonly IPlayerFinder players;
  private readonly IRoleAssigner roles;
  private readonly IRdmStore store;
  private readonly IMsgLocalizer locale;
  private readonly IList<IRole> roleSet;

  public DeathLogListenerTests(IServiceProvider provider) {
    bus     = provider.GetRequiredService<IEventBus>();
    games   = provider.GetRequiredService<IGameManager>();
    players = provider.GetRequiredService<IPlayerFinder>();
    roles   = provider.GetRequiredService<IRoleAssigner>();
    store   = provider.GetRequiredService<IRdmStore>();
    locale  = provider.GetRequiredService<IMsgLocalizer>();
    roleSet = new List<IRole> {
      new InnocentRole(provider), new TraitorRole(provider),
      new DetectiveRole(provider)
    };
    bus.RegisterListener(provider.GetRequiredService<IDamageTracker>());
    bus.RegisterListener(new DeathLogListener(provider));
  }

  private (TestPlayer victim, TestPlayer killer) StartRoundWith(
    IRole victimRole, IRole killerRole) {
    var victim = TestPlayer.Random();
    var killer = TestPlayer.Random();
    var game = games.CreateGame();
    game?.Start(); // 0 players online → MinimumPlayers check fails → no StartRound
    players.AddPlayers(victim, killer);
    // Set game state: dispatches GameStateUpdateEvent (CurrentRound → 1) AND
    // updates game.State so DeathLogListener.OnKill state-guard passes.
    game!.State = State.IN_PROGRESS;
    roles.SetRole(victim, victimRole);
    roles.SetRole(killer, killerRole);
    return (victim, killer);
  }

  [Fact]
  public async Task SuspectKill_RecordsDeath_AndPromptsVictim() {
    var (victim, killer) =
      StartRoundWith(roleSet[0], roleSet[0]); // inno on inno
    bus.Dispatch(new PlayerDamagedEvent(victim, killer, 100));
    var death = new PlayerDeathEvent(victim).WithKiller(killer)
     .WithWeapon("ak47");
    bus.Dispatch(death);

    var recorded = await store.GetSuspectDeathsForVictim(victim.Id, 1);
    Assert.Single(recorded);
    Assert.Contains(locale[RdmMsgs.RDM_PROMPT(killer.Name)], victim.Messages);
  }

  [Fact]
  public async Task LegitKill_RecordsNonSuspect_NoPrompt() {
    var (victim, killer) =
      StartRoundWith(roleSet[0], roleSet[1]); // traitor kills inno
    bus.Dispatch(new PlayerDamagedEvent(victim, killer, 100));
    bus.Dispatch(new PlayerDeathEvent(victim).WithKiller(killer));

    Assert.Empty(await store.GetSuspectDeathsForVictim(victim.Id, 1));
    Assert.Empty(victim.Messages);
  }
}
