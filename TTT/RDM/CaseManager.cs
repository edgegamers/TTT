using Microsoft.Extensions.DependencyInjection;
using TTT.API.Messages;
using TTT.API.Player;
using TTT.API.Storage;
using TTT.Locale;
using TTT.RDM.lang;
using TTT.RDM.Models;

namespace TTT.RDM;

public class CaseManager(IServiceProvider provider) : ICaseManager {
  private readonly IRdmStore store = provider.GetRequiredService<IRdmStore>();

  private readonly IPlayerFinder finder =
    provider.GetRequiredService<IPlayerFinder>();

  private readonly IPermissionManager perms =
    provider.GetRequiredService<IPermissionManager>();

  private readonly IMessenger messenger =
    provider.GetRequiredService<IMessenger>();

  private readonly IMsgLocalizer locale =
    provider.GetRequiredService<IMsgLocalizer>();

  private RdmConfig config
    => provider.GetService<IStorage<RdmConfig>>()?.Load().GetAwaiter()
      .GetResult() ?? new RdmConfig();

  public async Task<RdmCase?> FileReport(IOnlinePlayer reporter, int deathId,
    string? reason) {
    var death = await store.GetDeath(deathId);
    if (death == null) return null;
    if (death.VictimId != reporter.Id) return null;          // only the victim
    if (await store.HasReport(reporter.Id, deathId)) return null; // duplicate

    var cfg = config;
    if (await store.CountReportsByVictim(reporter.Id, death.Round)
      >= cfg.MaxReportsPerVictimPerRound) return null;       // per-round cap

    var age = DateTime.UtcNow - death.Timestamp;
    if (age.TotalSeconds > cfg.ReportWindowSeconds) return null; // window

    var id = await store.AddCase(new RdmCase {
      DeathId = deathId, ReporterId = reporter.Id, Reason = reason,
      State = CaseState.Open, CreatedAt = DateTime.UtcNow
    });
    var created = await store.GetCase(id);

    if (cfg.NotifyAdmins && created != null)
      NotifyStaff(death, created);
    return created;
  }

  private void NotifyStaff(DeathRecord death, RdmCase rdmCase) {
    var staffFlag = config.StaffFlag;
    foreach (var online in finder.GetOnline())
      if (perms.HasFlags(online, staffFlag))
        messenger.Message(online,
          locale[RdmMsgs.RDM_STAFF_NEW_REPORT(death.VictimName,
            death.AttackerName, rdmCase.Id)]);
  }

  public async Task<RdmCase?> ClaimNext(IPlayer admin) {
    var open = await store.GetOpenCases();
    var next = open.FirstOrDefault(c => c.State == CaseState.Open);
    if (next == null) return null;
    return await Claim(admin, next.Id);
  }

  public async Task<RdmCase?> Claim(IPlayer admin, int caseId) {
    var c = await store.GetCase(caseId);
    if (c == null || c.State == CaseState.Resolved) return null;
    var claimed = c with {
      State = CaseState.Claimed, HandlerAdminId = admin.Id
    };
    await store.UpdateCase(claimed);
    return claimed;
  }

  public async Task Resolve(int caseId, Verdict verdict, IPlayer admin) {
    var c = await store.GetCase(caseId);
    if (c == null) return;
    await store.UpdateCase(c with {
      State = CaseState.Resolved, Verdict = verdict,
      HandlerAdminId = admin.Id
    });
  }

  public Task<IReadOnlyList<RdmCase>> GetOpen() { return store.GetOpenCases(); }
}
