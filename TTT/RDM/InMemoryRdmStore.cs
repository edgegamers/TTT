using System.Collections.Concurrent;
using TTT.RDM.Models;

namespace TTT.RDM;

public sealed class InMemoryRdmStore : IRdmStore {
  private readonly ConcurrentDictionary<int, DeathRecord> deaths = new();
  private readonly ConcurrentDictionary<int, RdmCase> cases = new();
  private readonly ConcurrentDictionary<string, SlayDebt> slays = new();
  private int nextDeathId;
  private int nextCaseId;

  public Task<int> AddDeath(DeathRecord death) {
    var id = Interlocked.Increment(ref nextDeathId);
    deaths[id] = death with { Id = id };
    return Task.FromResult(id);
  }

  public Task<DeathRecord?> GetDeath(int id) {
    return Task.FromResult(deaths.GetValueOrDefault(id));
  }

  public Task<IReadOnlyList<DeathRecord>> GetSuspectDeathsForVictim(
    string victimId, int round) {
    IReadOnlyList<DeathRecord> result = deaths.Values
     .Where(d => d.IsSuspect && d.VictimId == victimId && d.Round == round)
     .OrderBy(d => d.Id)
     .ToList();
    return Task.FromResult(result);
  }

  public Task<int> AddCase(RdmCase rdmCase) {
    var id = Interlocked.Increment(ref nextCaseId);
    cases[id] = rdmCase with { Id = id };
    return Task.FromResult(id);
  }

  public Task<RdmCase?> GetCase(int id) {
    return Task.FromResult(cases.GetValueOrDefault(id));
  }

  public Task<IReadOnlyList<RdmCase>> GetOpenCases() {
    IReadOnlyList<RdmCase> result = cases.Values
     .Where(c => c.State != CaseState.Resolved)
     .OrderBy(c => c.Id)
     .ToList();
    return Task.FromResult(result);
  }

  public Task UpdateCase(RdmCase rdmCase) {
    cases[rdmCase.Id] = rdmCase;
    return Task.CompletedTask;
  }

  public Task<bool> HasReport(string reporterId, int deathId) {
    return Task.FromResult(cases.Values.Any(c
      => c.ReporterId == reporterId && c.DeathId == deathId));
  }

  public Task<int> CountReportsByVictim(string reporterId, int round) {
    var count = cases.Values.Count(c
      => c.ReporterId == reporterId
      && deaths.TryGetValue(c.DeathId, out var d) && d.Round == round);
    return Task.FromResult(count);
  }

  public Task SetSlayDebt(string playerId, int remaining, int sourceCaseId) {
    if (remaining <= 0)
      slays.TryRemove(playerId, out _);
    else
      slays[playerId] = new SlayDebt {
        PlayerId = playerId, RemainingSlays = remaining,
        SourceCaseId = sourceCaseId
      };
    return Task.CompletedTask;
  }

  public Task<int> GetSlayDebt(string playerId) {
    return Task.FromResult(
      slays.TryGetValue(playerId, out var d) ? d.RemainingSlays : 0);
  }

  public Task<IReadOnlyList<SlayDebt>> GetAllSlayDebts() {
    IReadOnlyList<SlayDebt> result =
      slays.Values.Where(s => s.RemainingSlays > 0).ToList();
    return Task.FromResult(result);
  }
}
