using Microsoft.Extensions.DependencyInjection;
using TTT.API.Command;
using TTT.API.Player;
using TTT.Game.Damage;
using TTT.Locale;
using TTT.RDM;
using TTT.RDM.Commands;
using TTT.RDM.lang;
using TTT.RDM.Models;
using TTT.Test.Fakes;
using TTT.Test.Game.Command;
using Xunit;

namespace TTT.Test.RDM;

public class VerdictFlowTests {
  private readonly IServiceProvider provider;
  private readonly ICommandManager commands;
  private readonly ICaseManager manager;
  private readonly IRdmStore store;
  private readonly IMsgLocalizer locale;
  private readonly IPlayerFinder players;
  private readonly FakePermissionManager perms;

  public VerdictFlowTests(IServiceProvider provider) {
    this.provider = provider;
    commands = provider.GetRequiredService<ICommandManager>();
    manager  = provider.GetRequiredService<ICaseManager>();
    store    = provider.GetRequiredService<IRdmStore>();
    locale   = provider.GetRequiredService<IMsgLocalizer>();
    players  = provider.GetRequiredService<IPlayerFinder>();
    perms    = (FakePermissionManager)provider
     .GetRequiredService<IPermissionManager>();
    commands.RegisterCommand(new HandleCommand(provider));
    commands.RegisterCommand(new VerdictCommand(provider));
  }

  private async Task<(TestPlayer offender, TestPlayer admin, RdmCase c)>
    SeedReportedCase() {
    var victim   = TestPlayer.Random();
    var offender = TestPlayer.Random();
    var admin    = TestPlayer.Random();
    offender.IsAlive = false;
    players.AddPlayers(victim, offender, admin);
    perms.SetFlags(admin, "@ttt/admin");

    var deathId = await store.AddDeath(new DeathRecord {
      Round = 0, VictimId = victim.Id, VictimName = victim.Name,
      VictimRole = "Innocent", AttackerId = offender.Id,
      AttackerName = offender.Name, AttackerRole = "Innocent",
      Weapon = "ak47", Timestamp = DateTime.UtcNow, IsSuspect = true,
      Fault = KillFault.KillerGuilty
    });
    var c = (await manager.FileReport(victim, deathId, "rdm"))!;
    return (offender, admin, c);
  }

  [Fact]
  public async Task Handle_ThenGuilty_QueuesSlays() {
    var (offender, admin, c) = await SeedReportedCase();

    Assert.Equal(CommandResult.SUCCESS, await commands.ProcessCommand(
      new TestCommandInfo(provider, admin, "handle")));
    Assert.Equal(CommandResult.SUCCESS, await commands.ProcessCommand(
      new TestCommandInfo(provider, admin, "verdict", "guilty")));

    Assert.Equal(3, await store.GetSlayDebt(offender.Id)); // innocent victim
    Assert.Empty(await manager.GetOpen());                 // case resolved
  }

  [Fact]
  public async Task Handle_ThenForgive_NoSlays() {
    var (offender, admin, _) = await SeedReportedCase();
    await commands.ProcessCommand(
      new TestCommandInfo(provider, admin, "handle"));
    Assert.Equal(CommandResult.SUCCESS, await commands.ProcessCommand(
      new TestCommandInfo(provider, admin, "verdict", "forgive")));

    Assert.Equal(0, await store.GetSlayDebt(offender.Id));
    Assert.Empty(await manager.GetOpen());
  }

  [Fact]
  public async Task Verdict_WithoutClaimedCase_Errors() {
    var admin = TestPlayer.Random();
    players.AddPlayer(admin);
    perms.SetFlags(admin, "@ttt/admin");
    var result = await commands.ProcessCommand(
      new TestCommandInfo(provider, admin, "verdict", "guilty"));
    Assert.Equal(CommandResult.ERROR, result);
    Assert.Contains(locale[RdmMsgs.RDM_NO_CLAIMED_CASE()], admin.Messages);
  }
}
