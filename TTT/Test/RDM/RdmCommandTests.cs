using Microsoft.Extensions.DependencyInjection;
using TTT.API.Command;
using TTT.API.Player;
using TTT.Game.Damage;
using TTT.Locale;
using TTT.RDM;
using TTT.RDM.Commands;
using TTT.RDM.lang;
using TTT.RDM.Models;
using TTT.Test.Game.Command;
using Xunit;

namespace TTT.Test.RDM;

public class RdmCommandTests : CommandTest {
  private readonly IRdmStore store;
  private readonly IMsgLocalizer locale;
  private readonly IPlayerFinder players;

  public RdmCommandTests(IServiceProvider provider) : base(provider,
    new RdmCommand(provider)) {
    store   = provider.GetRequiredService<IRdmStore>();
    locale  = provider.GetRequiredService<IMsgLocalizer>();
    players = provider.GetRequiredService<IPlayerFinder>();
  }

  private async Task<int> SeedSuspectDeath(IPlayer victim) {
    return await store.AddDeath(new DeathRecord {
      Round = 0, VictimId = victim.Id, VictimName = victim.Name,
      VictimRole = "Innocent", AttackerId = "k", AttackerName = "Killer",
      AttackerRole = "Innocent", Timestamp = DateTime.UtcNow,
      IsSuspect = true, Fault = KillFault.KillerGuilty
    });
  }

  [Fact]
  public async Task NoArgs_NoDeaths_ShowsEmpty() {
    var victim = TestPlayer.Random();
    players.AddPlayer(victim);
    var info = new TestCommandInfo(Provider, victim, "rdm");
    var result = await Commands.ProcessCommand(info);
    Assert.Equal(CommandResult.SUCCESS, result);
    Assert.Contains(locale[RdmMsgs.RDM_LIST_EMPTY()], victim.Messages);
  }

  [Fact]
  public async Task NoArgs_WithDeaths_ListsThem() {
    var victim = TestPlayer.Random();
    players.AddPlayer(victim);
    await SeedSuspectDeath(victim);
    var info = new TestCommandInfo(Provider, victim, "rdm");
    Assert.Equal(CommandResult.SUCCESS, await Commands.ProcessCommand(info));
    Assert.Contains(locale[RdmMsgs.RDM_LIST_ENTRY(1, "Killer")],
      victim.Messages);
  }

  [Fact]
  public async Task WithIndex_FilesReport() {
    var victim = TestPlayer.Random();
    players.AddPlayer(victim);
    await SeedSuspectDeath(victim);
    var info = new TestCommandInfo(Provider, victim, "rdm", "1");
    Assert.Equal(CommandResult.SUCCESS, await Commands.ProcessCommand(info));
    Assert.Single(await store.GetOpenCases());
    Assert.Contains(locale[RdmMsgs.RDM_REPORT_FILED(1)], victim.Messages);
  }
}
