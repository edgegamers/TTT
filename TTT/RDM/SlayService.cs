using Microsoft.Extensions.DependencyInjection;
using TTT.API.Player;
using TTT.API.Storage;

namespace TTT.RDM;

public class SlayService(IServiceProvider provider) : ISlayService {
  private readonly IRdmStore store = provider.GetRequiredService<IRdmStore>();

  private readonly IPlayerFinder finder =
    provider.GetRequiredService<IPlayerFinder>();

  private RdmConfig config
    => provider.GetService<IStorage<RdmConfig>>()?.Load().GetAwaiter()
      .GetResult() ?? new RdmConfig();

  public async Task ApplyGuilty(IPlayer offender, string victimRole,
    int caseId) {
    var owed     = config.SlaysForRole(victimRole);
    var existing = await store.GetSlayDebt(offender.Id);
    var total    = existing + owed;

    var online = finder.GetPlayerById(offender.Id);
    if (online is { IsAlive: true }) {
      online.Health = 0; // immediate slay
      total--;
    }

    await store.SetSlayDebt(offender.Id, total, caseId);
  }

  public async Task<int> PayRoundStart() {
    var applied = 0;
    foreach (var debt in await store.GetAllSlayDebts()) {
      var online = finder.GetPlayerById(debt.PlayerId);
      if (online is not { IsAlive: true }) continue;
      online.Health = 0;
      await store.SetSlayDebt(debt.PlayerId, debt.RemainingSlays - 1,
        debt.SourceCaseId);
      applied++;
    }

    return applied;
  }
}
