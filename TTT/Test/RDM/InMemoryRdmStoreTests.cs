using TTT.Game.Damage;
using TTT.RDM;
using TTT.RDM.Models;
using Xunit;

namespace TTT.Test.RDM;

public class InMemoryRdmStoreTests {
  private static DeathRecord SampleDeath(string victimId = "v",
    bool suspect = true, int round = 1) {
    return new DeathRecord {
      Round = round, VictimId = victimId, VictimName = "Victim",
      VictimRole = "Innocent", AttackerId = "k", AttackerName = "Killer",
      AttackerRole = "Innocent", Weapon = "ak47",
      Timestamp = new DateTime(2026, 1, 1), IsSuspect = suspect,
      Fault = KillFault.KillerGuilty
    };
  }

  private static IRdmStore NewStore() { return new InMemoryRdmStore(); }

  [Fact]
  public async Task AddDeath_AssignsIncrementingIds() {
    var store = NewStore();
    var id1   = await store.AddDeath(SampleDeath());
    var id2   = await store.AddDeath(SampleDeath());
    Assert.True(id2 > id1);
    Assert.Equal(id1, (await store.GetDeath(id1))!.Id);
  }

  [Fact]
  public async Task GetSuspectDeathsForVictim_FiltersBySuspectAndRound() {
    var store = NewStore();
    await store.AddDeath(SampleDeath("v", true, 1));
    await store.AddDeath(SampleDeath("v", false, 1)); // not suspect
    await store.AddDeath(SampleDeath("v", true, 2));  // wrong round
    await store.AddDeath(SampleDeath("w", true, 1));  // wrong victim

    var result = await store.GetSuspectDeathsForVictim("v", 1);
    Assert.Single(result);
  }

  [Fact]
  public async Task Cases_RoundTripAndOpenFilter() {
    var store  = NewStore();
    var deathId = await store.AddDeath(SampleDeath());
    var id = await store.AddCase(new RdmCase {
      DeathId = deathId, ReporterId = "v", CreatedAt = new DateTime(2026, 1, 1)
    });

    var open = await store.GetOpenCases();
    Assert.Single(open);

    await store.UpdateCase((await store.GetCase(id))! with {
      State = CaseState.Resolved, Verdict = Verdict.Forgiven
    });
    Assert.Empty(await store.GetOpenCases());
  }

  [Fact]
  public async Task HasReport_And_CountReportsByVictim() {
    var store   = NewStore();
    var deathId = await store.AddDeath(SampleDeath());
    Assert.False(await store.HasReport("v", deathId));

    await store.AddCase(new RdmCase {
      DeathId = deathId, ReporterId = "v", CreatedAt = new DateTime(2026, 1, 1)
    });
    Assert.True(await store.HasReport("v", deathId));
    Assert.Equal(1, await store.CountReportsByVictim("v", 1));
  }

  [Fact]
  public async Task SlayDebt_SetGetAndList() {
    var store = NewStore();
    Assert.Equal(0, await store.GetSlayDebt("p"));
    await store.SetSlayDebt("p", 3, 7);
    Assert.Equal(3, await store.GetSlayDebt("p"));
    Assert.Single(await store.GetAllSlayDebts());

    await store.SetSlayDebt("p", 0, 7);
    Assert.Empty(await store.GetAllSlayDebts());
  }
}
