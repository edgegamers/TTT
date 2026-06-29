using Microsoft.Extensions.DependencyInjection;
using TTT.API.Player;
using TTT.RDM;
using Xunit;

namespace TTT.Test.RDM;

public class SlayServiceTests {
  private readonly ISlayService slay;
  private readonly IRdmStore store;
  private readonly IPlayerFinder players;

  public SlayServiceTests(IServiceProvider provider) {
    slay    = provider.GetRequiredService<ISlayService>();
    store   = provider.GetRequiredService<IRdmStore>();
    players = provider.GetRequiredService<IPlayerFinder>();
  }

  [Fact]
  public async Task ApplyGuilty_AliveOffender_SlaysNowAndQueuesRest() {
    var offender = TestPlayer.Random();
    offender.IsAlive = true;
    offender.Health  = 100;
    players.AddPlayer(offender);

    await slay.ApplyGuilty(offender, "Innocent", caseId: 1); // 3 slays

    Assert.Equal(0, offender.Health);          // immediate slay applied
    Assert.Equal(2, await store.GetSlayDebt(offender.Id)); // 3 - 1
  }

  [Fact]
  public async Task ApplyGuilty_DeadOffender_QueuesAll() {
    var offender = TestPlayer.Random();
    offender.IsAlive = false;
    players.AddPlayer(offender);

    await slay.ApplyGuilty(offender, "Traitor", caseId: 1); // 5 slays
    Assert.Equal(5, await store.GetSlayDebt(offender.Id));
  }

  [Fact]
  public async Task PayRoundStart_SlaysAliveDebtorsOncePerRound() {
    var offender = TestPlayer.Random();
    offender.IsAlive = false;
    players.AddPlayer(offender);
    await slay.ApplyGuilty(offender, "Innocent", caseId: 1); // queues 3

    // Round 1: offender respawns alive, pays one slay.
    offender.IsAlive = true;
    offender.Health  = 100;
    var applied = await slay.PayRoundStart();
    Assert.Equal(1, applied);
    Assert.Equal(0, offender.Health);
    Assert.Equal(2, await store.GetSlayDebt(offender.Id));
  }
}
