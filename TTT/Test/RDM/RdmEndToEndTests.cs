using Microsoft.Extensions.DependencyInjection;
using TTT.API.Command;
using TTT.API.Events;
using TTT.API.Game;
using TTT.API.Player;
using TTT.API.Role;
using TTT.Game.Damage;
using TTT.Game.Events.Player;
using TTT.Game.Roles;
using TTT.RDM;
using TTT.RDM.Commands;
using TTT.Test.Fakes;
using TTT.Test.Game.Command;
using Xunit;

namespace TTT.Test.RDM;

public class RdmEndToEndTests {
  private readonly IServiceProvider provider;
  private readonly IEventBus bus;
  private readonly IGameManager games;
  private readonly IPlayerFinder players;
  private readonly IRoleAssigner roles;
  private readonly ICommandManager commands;
  private readonly IRdmStore store;
  private readonly FakePermissionManager perms;
  private readonly IList<IRole> roleSet;

  public RdmEndToEndTests(IServiceProvider provider) {
    this.provider = provider;
    bus      = provider.GetRequiredService<IEventBus>();
    games    = provider.GetRequiredService<IGameManager>();
    players  = provider.GetRequiredService<IPlayerFinder>();
    roles    = provider.GetRequiredService<IRoleAssigner>();
    commands = provider.GetRequiredService<ICommandManager>();
    store    = provider.GetRequiredService<IRdmStore>();
    perms    = (FakePermissionManager)provider
     .GetRequiredService<IPermissionManager>();
    roleSet  = new List<IRole> {
      new InnocentRole(provider), new TraitorRole(provider),
      new DetectiveRole(provider)
    };
    bus.RegisterListener(provider.GetRequiredService<IDamageTracker>());
    bus.RegisterListener(provider.GetRequiredService<DeathLogListener>());
    commands.RegisterCommand(new RdmCommand(provider));
    commands.RegisterCommand(new HandleCommand(provider));
    commands.RegisterCommand(new VerdictCommand(provider));
  }

  [Fact]
  public async Task SuspectKill_Report_Handle_Guilty_QueuesSlays() {
    var victim   = TestPlayer.Random();
    var offender = TestPlayer.Random();
    var admin    = TestPlayer.Random();
    players.AddPlayers(victim, offender, admin);
    perms.SetFlags(admin, "@ttt/admin");

    var game = games.CreateGame();
    bus.Dispatch(new TTT.Game.Events.Game.GameStateUpdateEvent(game!,
      State.IN_PROGRESS)); // round 1
    game!.Start();
    roles.SetRole(victim, roleSet[0]);   // innocent
    roles.SetRole(offender, roleSet[0]); // innocent -> suspect

    bus.Dispatch(new PlayerDamagedEvent(victim, offender, 100));
    bus.Dispatch(new PlayerDeathEvent(victim).WithKiller(offender)
     .WithWeapon("ak47"));
    victim.IsAlive   = false; // victim died from the kill
    offender.IsAlive = false; // offender died later that round

    // Give the fire-and-forget AddDeath a beat to land.
    await Task.Delay(50, TestContext.Current.CancellationToken);

    // Victim reports the only listed suspect death.
    Assert.Equal(CommandResult.SUCCESS, await commands.ProcessCommand(
      new TestCommandInfo(provider, victim, "rdm", "1")));

    // Staff handles + rules guilty.
    Assert.Equal(CommandResult.SUCCESS, await commands.ProcessCommand(
      new TestCommandInfo(provider, admin, "handle")));
    Assert.Equal(CommandResult.SUCCESS, await commands.ProcessCommand(
      new TestCommandInfo(provider, admin, "verdict", "guilty")));

    Assert.Equal(3, await store.GetSlayDebt(offender.Id));
  }
}
