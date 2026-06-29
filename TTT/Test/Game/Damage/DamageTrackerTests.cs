using Microsoft.Extensions.DependencyInjection;
using TTT.API.Events;
using TTT.API.Game;
using TTT.API.Player;
using TTT.Game.Damage;
using TTT.Game.Events.Player;
using Xunit;

namespace TTT.Test.Game.Damage;

public class DamageTrackerTests {
  private readonly IEventBus bus;
  private readonly IGameManager games;
  private readonly IPlayerFinder players;
  private readonly IDamageTracker tracker;

  public DamageTrackerTests(IServiceProvider provider) {
    bus     = provider.GetRequiredService<IEventBus>();
    games   = provider.GetRequiredService<IGameManager>();
    players = provider.GetRequiredService<IPlayerFinder>();
    tracker = provider.GetRequiredService<IDamageTracker>();
    bus.RegisterListener(tracker);
  }

  [Fact]
  public void GetFault_NoDamage_ReturnsUnknown() {
    Assert.Equal(KillFault.Unknown, tracker.GetFault("a", "b"));
  }

  [Fact]
  public void OnHurt_DuringGame_RecordsKillerGuilty() {
    var victim   = TestPlayer.Random();
    var attacker = TestPlayer.Random();
    players.AddPlayers(victim, attacker);
    games.CreateGame()?.Start();

    bus.Dispatch(new PlayerDamagedEvent(victim, attacker, 100));

    Assert.Equal(KillFault.KillerGuilty,
      tracker.GetFault(attacker.Id, victim.Id));
    Assert.Equal(KillFault.VictimGuilty,
      tracker.GetFault(victim.Id, attacker.Id));
  }

  [Fact]
  public void RoundStart_ClearsPriorDamage() {
    var victim   = TestPlayer.Random();
    var attacker = TestPlayer.Random();
    players.AddPlayers(victim, attacker);
    var game = games.CreateGame();
    game?.Start();
    bus.Dispatch(new PlayerDamagedEvent(victim, attacker, 100));

    // New round start clears first-damage state.
    bus.Dispatch(new TTT.Game.Events.Game.GameStateUpdateEvent(game!,
      State.IN_PROGRESS));

    Assert.Equal(KillFault.Unknown,
      tracker.GetFault(attacker.Id, victim.Id));
  }
}
