using TTT.Game.Damage;
using TTT.RDM;
using TTT.RDM.Models;
using Xunit;

namespace TTT.Test.RDM;

public class SqliteRdmStoreTests {
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

  // Unique in-memory DB per test (shared cache so the schema persists across
  // the store's internal connection lifetime).
  private static SqliteRdmStore NewStore() {
    return new SqliteRdmStore("Data Source=:memory:");
  }

  [Fact]
  public async Task Death_RoundTrip_PreservesFields() {
    using var store = NewStore();
    var id = await store.AddDeath(SampleDeath());
    var got = await store.GetDeath(id);
    Assert.NotNull(got);
    Assert.Equal("Killer", got!.AttackerName);
    Assert.Equal(KillFault.KillerGuilty, got.Fault);
    Assert.True(got.IsSuspect);
  }

  [Fact]
  public async Task SuspectDeaths_FilterByVictimRoundSuspect() {
    using var store = NewStore();
    await store.AddDeath(SampleDeath("v", true, 1));
    await store.AddDeath(SampleDeath("v", false, 1));
    await store.AddDeath(SampleDeath("v", true, 2));
    Assert.Single(await store.GetSuspectDeathsForVictim("v", 1));
  }

  [Fact]
  public async Task Cases_OpenFilter_And_Update() {
    using var store = NewStore();
    var deathId = await store.AddDeath(SampleDeath());
    var id = await store.AddCase(new RdmCase {
      DeathId = deathId, ReporterId = "v", CreatedAt = new DateTime(2026, 1, 1)
    });
    Assert.Single(await store.GetOpenCases());
    await store.UpdateCase((await store.GetCase(id))! with {
      State = CaseState.Resolved, Verdict = Verdict.Guilty,
      HandlerAdminId = "admin"
    });
    Assert.Empty(await store.GetOpenCases());
    Assert.Equal(Verdict.Guilty, (await store.GetCase(id))!.Verdict);
  }

  [Fact]
  public async Task SlayDebt_Persisted_AcrossNewStoreSameFile() {
    var file = $"Data Source=rdm-test-{Guid.NewGuid():N}.db";
    try {
      using (var store = new SqliteRdmStore(file))
        await store.SetSlayDebt("p", 4, 1);
      using (var store2 = new SqliteRdmStore(file))
        Assert.Equal(4, await store2.GetSlayDebt("p"));
    } finally {
      Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
      var path = file.Replace("Data Source=", "");
      if (File.Exists(path)) File.Delete(path);
    }
  }

  [Fact]
  public async Task HasReport_And_CountReportsByVictim() {
    using var store = NewStore();
    var deathId = await store.AddDeath(SampleDeath());
    Assert.False(await store.HasReport("v", deathId));
    await store.AddCase(new RdmCase {
      DeathId = deathId, ReporterId = "v", CreatedAt = new DateTime(2026, 1, 1)
    });
    Assert.True(await store.HasReport("v", deathId));
    Assert.Equal(1, await store.CountReportsByVictim("v", 1));
  }
}
