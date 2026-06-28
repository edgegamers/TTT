using Microsoft.Extensions.DependencyInjection;
using TTT.API.Player;
using TTT.Game.Damage;
using TTT.RDM;
using TTT.RDM.Models;
using Xunit;

namespace TTT.Test.RDM;

public class CaseManagerTests {
  private readonly ICaseManager manager;
  private readonly IRdmStore store;
  private readonly IPlayerFinder players;

  public CaseManagerTests(IServiceProvider provider) {
    manager = provider.GetRequiredService<ICaseManager>();
    store   = provider.GetRequiredService<IRdmStore>();
    players = provider.GetRequiredService<IPlayerFinder>();
  }

  private async Task<int> SeedSuspectDeath(IPlayer victim) {
    return await store.AddDeath(new DeathRecord {
      Round = 1, VictimId = victim.Id, VictimName = victim.Name,
      VictimRole = "Innocent", AttackerId = "k", AttackerName = "Killer",
      AttackerRole = "Innocent", Timestamp = DateTime.UtcNow,
      IsSuspect = true, Fault = KillFault.KillerGuilty
    });
  }

  [Fact]
  public async Task FileReport_CreatesOpenCase() {
    var victim  = TestPlayer.Random();
    players.AddPlayer(victim);
    var deathId = await SeedSuspectDeath(victim);

    var c = await manager.FileReport(victim, deathId, "shot me in spawn");
    Assert.NotNull(c);
    Assert.Equal(CaseState.Open, c!.State);
    Assert.Single(await manager.GetOpen());
  }

  [Fact]
  public async Task FileReport_ByNonVictim_Rejected() {
    var victim   = TestPlayer.Random();
    var other    = TestPlayer.Random();
    players.AddPlayers(victim, other);
    var deathId  = await SeedSuspectDeath(victim);
    Assert.Null(await manager.FileReport(other, deathId, null));
  }

  [Fact]
  public async Task FileReport_Duplicate_Rejected() {
    var victim  = TestPlayer.Random();
    players.AddPlayer(victim);
    var deathId = await SeedSuspectDeath(victim);
    Assert.NotNull(await manager.FileReport(victim, deathId, null));
    Assert.Null(await manager.FileReport(victim, deathId, null));
  }

  [Fact]
  public async Task ClaimNext_MovesOldestToClaimed() {
    var victim  = TestPlayer.Random();
    players.AddPlayer(victim);
    var deathId = await SeedSuspectDeath(victim);
    await manager.FileReport(victim, deathId, null);

    var admin = TestPlayer.Random();
    var c     = await manager.ClaimNext(admin);
    Assert.NotNull(c);
    Assert.Equal(CaseState.Claimed, c!.State);
    Assert.Equal(admin.Id, c.HandlerAdminId);
  }

  [Fact]
  public async Task ClaimNext_PicksOldestOpenCase() {
    var v1 = TestPlayer.Random();
    var v2 = TestPlayer.Random();
    players.AddPlayers(v1, v2);
    var death1 = await SeedSuspectDeath(v1);
    var death2 = await SeedSuspectDeath(v2);
    var first  = await manager.FileReport(v1, death1, null);
    await manager.FileReport(v2, death2, null);

    var admin   = TestPlayer.Random();
    var claimed = await manager.ClaimNext(admin);
    Assert.NotNull(claimed);
    Assert.Equal(first!.Id, claimed!.Id); // oldest filed is claimed first
  }

  [Fact]
  public async Task Resolve_ClosesCase() {
    var victim  = TestPlayer.Random();
    players.AddPlayer(victim);
    var deathId = await SeedSuspectDeath(victim);
    var c       = await manager.FileReport(victim, deathId, null);
    var admin   = TestPlayer.Random();

    await manager.Resolve(c!.Id, Verdict.Forgiven, admin);
    Assert.Empty(await manager.GetOpen());
    Assert.Equal(Verdict.Forgiven, (await store.GetCase(c.Id))!.Verdict);
  }
}
