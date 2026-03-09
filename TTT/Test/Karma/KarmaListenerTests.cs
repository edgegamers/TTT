using Microsoft.Extensions.DependencyInjection;
using TTT.API.Events;
using TTT.API.Game;
using TTT.API.Player;
using TTT.API.Role;
using TTT.Game.Events.Player;
using TTT.Game.Roles;
using TTT.Karma;
using Xunit;

namespace TTT.Test.Karma;

public class KarmaListenerTests {
  public enum RoleEnum {
    Innocent, Traitor, Detective
  }

  private readonly IEventBus bus;
  private readonly IGameManager games;
  private readonly IKarmaService karma;
  private readonly IPlayerFinder players;
  private readonly IKarmaUpdateManager karmaUpdateManager;
  private readonly IRoleAssigner roles;

  private readonly IList<IRole> roleSet;

  public KarmaListenerTests(IServiceProvider provider) {
    games   = provider.GetRequiredService<IGameManager>();
    roles   = provider.GetRequiredService<IRoleAssigner>();
    bus     = provider.GetRequiredService<IEventBus>();
    karma   = provider.GetRequiredService<IKarmaService>();
    karmaUpdateManager = provider.GetRequiredService<IKarmaUpdateManager>();
    players = provider.GetRequiredService<IPlayerFinder>();
    roleSet = new List<IRole> {
      new InnocentRole(provider),
      new TraitorRole(provider),
      new DetectiveRole(provider)
    };

    var listener = new KarmaListener(provider);
    listener.GiveKarmaOnRoundEnd = false;

    bus.RegisterListener(listener);
  }

  [Fact]
  public async Task OnKill_WithoutGame_DoesNothing() {
    var victim   = TestPlayer.Random();
    var attacker = TestPlayer.Random();

    roles.AssignRoles(new HashSet<IOnlinePlayer>([victim, attacker]), roleSet);

    var damageEvent = new PlayerDamagedEvent(victim, attacker, 100);
    bus.Dispatch(damageEvent);
    
    var deathEvent = new PlayerDeathEvent(victim);
    deathEvent.WithKiller(attacker);
    
    bus.Dispatch(deathEvent);

    var victimKarma   = await karma.Load(victim);
    var attackerKarma = await karma.Load(attacker);

    Assert.Equal(50, victimKarma);
    Assert.Equal(50, attackerKarma);
  }

  [Theory]
  [InlineData(RoleEnum.Innocent, RoleEnum.Innocent, 46, 51)]
  [InlineData(RoleEnum.Innocent, RoleEnum.Traitor, 55, 50)]
  [InlineData(RoleEnum.Innocent, RoleEnum.Detective, 44, 51)]
  [InlineData(RoleEnum.Traitor, RoleEnum.Innocent, 50, 50)]
  [InlineData(RoleEnum.Traitor, RoleEnum.Traitor, 45, 51)]
  [InlineData(RoleEnum.Traitor, RoleEnum.Detective, 51, 50)]
  [InlineData(RoleEnum.Detective, RoleEnum.Innocent, 46, 51)]
  [InlineData(RoleEnum.Detective, RoleEnum.Traitor, 55, 50)]
  [InlineData(RoleEnum.Detective, RoleEnum.Detective, 44, 51)]
  public async Task OnKill_Guilty_AffectsKarma(RoleEnum attackerRole,
    RoleEnum victimRole, int expAttackerKarma, int expVictimKarma) {
    var victim   = TestPlayer.Random();
    var attacker = TestPlayer.Random();

    players.AddPlayer(victim);
    players.AddPlayer(attacker);

    var game = games.CreateGame();
    Assert.NotNull(game);
    game.Start();

    roles.SetRole(victim, roleSet[(int)victimRole]);
    roles.SetRole(attacker, roleSet[(int)attackerRole]);

    var damageEvent = new PlayerDamagedEvent(victim, attacker, 100);
    bus.Dispatch(damageEvent);

    var deathEvent = new PlayerDeathEvent(victim);
    deathEvent.WithKiller(attacker);

    bus.Dispatch(deathEvent);
    game.EndGame();

    await Task.Delay(TimeSpan.FromMilliseconds(50),
      TestContext.Current
       .CancellationToken); // Wait for the karma update to process

    var victimKarma   = await karma.Load(victim);
    var attackerKarma = await karma.Load(attacker);

    Assert.Equal(expVictimKarma, victimKarma);
    Assert.Equal(expAttackerKarma, attackerKarma);
  }
  
  [Theory]
  [InlineData(RoleEnum.Innocent, RoleEnum.Innocent, 50, 48)]
  [InlineData(RoleEnum.Detective, RoleEnum.Innocent, 50, 48)]
  [InlineData(RoleEnum.Traitor, RoleEnum.Traitor, 47, 48)]
  [InlineData(RoleEnum.Innocent, RoleEnum.Detective, 46, 49)]
  [InlineData(RoleEnum.Detective, RoleEnum.Detective, 46, 49)]
  public async Task OnKill_Innocent_AffectsKarma(RoleEnum attackerRole,
    RoleEnum victimRole, int expAttackerKarma, int expVictimKarma) {
    var victim   = TestPlayer.Random();
    var attacker = TestPlayer.Random();

    players.AddPlayer(victim);
    players.AddPlayer(attacker);

    var game = games.CreateGame();
    Assert.NotNull(game);
    game.Start();

    roles.SetRole(victim, roleSet[(int)victimRole]);
    roles.SetRole(attacker, roleSet[(int)attackerRole]);

    bus.Dispatch(new PlayerDamagedEvent(attacker, victim, 1));
    bus.Dispatch(new PlayerDamagedEvent(victim, attacker, 100));

    var deathEvent = new PlayerDeathEvent(victim);
    deathEvent.WithKiller(attacker);

    bus.Dispatch(deathEvent);
    game.EndGame();

    await Task.Delay(TimeSpan.FromMilliseconds(50),
      TestContext.Current
       .CancellationToken); // Wait for the karma update to process

    var victimKarma   = await karma.Load(victim);
    var attackerKarma = await karma.Load(attacker);

    Assert.Equal(expVictimKarma, victimKarma);
    Assert.Equal(expAttackerKarma, attackerKarma);
  }

  [Fact]
  public async Task OnKill_WithMultiple_StacksKarma() {
    var attacker     = TestPlayer.Random();
    var victim1      = TestPlayer.Random();
    var victim2      = TestPlayer.Random();
    var victim3      = TestPlayer.Random();
    var guiltyVictim1 = TestPlayer.Random();
    var guiltyVictim2 = TestPlayer.Random();

    players.AddPlayers(attacker, victim1, victim2, victim3, guiltyVictim1, guiltyVictim2);

    var game = games.CreateGame();
    Assert.NotNull(game);
    game.Start();

    roles.SetRole(attacker, roleSet[(int)RoleEnum.Innocent]);
    roles.SetRole(victim1, roleSet[(int)RoleEnum.Innocent]);
    roles.SetRole(victim2, roleSet[(int)RoleEnum.Innocent]);
    roles.SetRole(victim3, roleSet[(int)RoleEnum.Detective]);
    roles.SetRole(guiltyVictim1, roleSet[(int)RoleEnum.Innocent]);
    roles.SetRole(guiltyVictim2, roleSet[(int)RoleEnum.Detective]);

    bus.Dispatch(new PlayerDamagedEvent(victim1, attacker, 100));
    bus.Dispatch(new PlayerDamagedEvent(victim2, attacker, 100));
    bus.Dispatch(new PlayerDamagedEvent(victim3, attacker, 100));
    bus.Dispatch(new PlayerDamagedEvent(attacker, guiltyVictim1, 1));
    bus.Dispatch(new PlayerDamagedEvent(guiltyVictim1, attacker, 100));
    bus.Dispatch(new PlayerDamagedEvent(attacker, guiltyVictim2, 1));
    bus.Dispatch(new PlayerDamagedEvent(guiltyVictim2, attacker, 100));

    var deathEvent1 = new PlayerDeathEvent(victim1);
    deathEvent1.WithKiller(attacker);
    var deathEvent2 = new PlayerDeathEvent(victim2);
    deathEvent2.WithKiller(attacker);
    var deathEvent3 = new PlayerDeathEvent(victim3);
    deathEvent3.WithKiller(attacker);
    var deathEventInnocent1 = new PlayerDeathEvent(guiltyVictim1);
    deathEventInnocent1.WithKiller(attacker);
    var deathEventInnocent2 = new PlayerDeathEvent(guiltyVictim2);
    deathEventInnocent2.WithKiller(attacker);

    bus.Dispatch(deathEvent1); // First kill => 50 - (4*1) = 46
    bus.Dispatch(deathEvent2); // Second kill => 46 - (4*2) = 38
    bus.Dispatch(deathEvent3); // Third kill (detective) => 38 - (6*3) = 20
    bus.Dispatch(deathEventInnocent1); // Fourth kill (guilty inno) => 20 - (0*3) = 20
    bus.Dispatch(deathEventInnocent2); // Fifth kill (guilty detective) => 20 - (4*3) = 8

    game.EndGame();

    await Task.Delay(TimeSpan.FromMilliseconds(20),
      TestContext.Current
       .CancellationToken); // Wait for the karma update to process

    var killerKarma = await karma.Load(attacker);
    Assert.Equal(8, killerKarma);
  }

  [Fact]
  public async Task OnKill_WithValidKills_DoesNotStack() {
    var attacker = TestPlayer.Random();
    var victim1  = TestPlayer.Random();
    var victim2  = TestPlayer.Random();
    var victim3  = TestPlayer.Random();

    players.AddPlayers(attacker, victim1, victim2, victim3);

    var game = games.CreateGame();
    Assert.NotNull(game);
    game.Start();

    roles.SetRole(attacker, roleSet[(int)RoleEnum.Innocent]);
    roles.SetRole(victim1, roleSet[(int)RoleEnum.Traitor]);
    roles.SetRole(victim2, roleSet[(int)RoleEnum.Traitor]);
    roles.SetRole(victim3, roleSet[(int)RoleEnum.Innocent]);

    var damageEvent1 = new PlayerDamagedEvent(victim1, attacker, 100);
    var damageEvent2 = new PlayerDamagedEvent(victim2, attacker, 100);
    var damageEvent3 = new PlayerDamagedEvent(victim3, attacker, 100);
    bus.Dispatch(damageEvent1);
    bus.Dispatch(damageEvent2);
    bus.Dispatch(damageEvent3);

    var deathEvent1 = new PlayerDeathEvent(victim1);
    deathEvent1.WithKiller(attacker);
    var deathEvent2 = new PlayerDeathEvent(victim2);
    deathEvent2.WithKiller(attacker);
    var deathEvent3 = new PlayerDeathEvent(victim3);
    deathEvent3.WithKiller(attacker);

    bus.Dispatch(deathEvent1); // First kill => 50 + 2 = 52
    bus.Dispatch(deathEvent2); // Second kill => 52 + 2 = 54
    bus.Dispatch(deathEvent3); // Third kill (inno) => 54 - 4 = 50

    var killerKarma = await karma.Load(attacker);
    Assert.Equal(50, killerKarma);
  }
}