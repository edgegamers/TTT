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

public class StaffQueryCommandsTests {
  private readonly IServiceProvider provider;
  private readonly ICommandManager commands;
  private readonly ICaseManager manager;
  private readonly IRdmStore store;
  private readonly IMsgLocalizer locale;
  private readonly IPlayerFinder players;
  private readonly FakePermissionManager perms;

  public StaffQueryCommandsTests(IServiceProvider provider) {
    this.provider = provider;
    commands = provider.GetRequiredService<ICommandManager>();
    manager  = provider.GetRequiredService<ICaseManager>();
    store    = provider.GetRequiredService<IRdmStore>();
    locale   = provider.GetRequiredService<IMsgLocalizer>();
    players  = provider.GetRequiredService<IPlayerFinder>();
    perms    = (FakePermissionManager)provider
     .GetRequiredService<IPermissionManager>();
    commands.RegisterCommand(new CasesCommand(provider));
    commands.RegisterCommand(new InfoCommand(provider));
  }

  private async Task<RdmCase> SeedOpenCase(IOnlinePlayer victim) {
    var deathId = await store.AddDeath(new DeathRecord {
      Round = 0, VictimId = victim.Id, VictimName = victim.Name,
      VictimRole = "Innocent", AttackerId = "k", AttackerName = "Killer",
      AttackerRole = "Innocent", Weapon = "ak47", Timestamp = DateTime.UtcNow,
      IsSuspect = true, Fault = KillFault.KillerGuilty
    });
    return (await manager.FileReport(victim, deathId, "spawn kill"))!;
  }

  [Fact]
  public async Task Cases_AsStaff_ShowsCount() {
    var victim = TestPlayer.Random();
    var admin  = TestPlayer.Random();
    players.AddPlayers(victim, admin);
    perms.SetFlags(admin, "@ttt/admin");
    await SeedOpenCase(victim);

    var result = await commands.ProcessCommand(
      new TestCommandInfo(provider, admin, "cases"));
    Assert.Equal(CommandResult.SUCCESS, result);
    Assert.Contains(locale[RdmMsgs.RDM_CASES_COUNT(1)], admin.Messages);
  }

  [Fact]
  public async Task Cases_WithoutFlag_NoPermission() {
    var player = TestPlayer.Random();
    players.AddPlayer(player);
    var result = await commands.ProcessCommand(
      new TestCommandInfo(provider, player, "cases"));
    Assert.Equal(CommandResult.NO_PERMISSION, result);
  }

  [Fact]
  public async Task Info_ShowsCaseDetails() {
    var victim = TestPlayer.Random();
    var admin  = TestPlayer.Random();
    players.AddPlayers(victim, admin);
    perms.SetFlags(admin, "@ttt/admin");
    var c = await SeedOpenCase(victim);

    var result = await commands.ProcessCommand(
      new TestCommandInfo(provider, admin, "info", c.Id.ToString()));
    Assert.Equal(CommandResult.SUCCESS, result);
    Assert.Contains(admin.Messages,
      m => m.Contains("Killer") && m.Contains("spawn kill"));
  }
}
