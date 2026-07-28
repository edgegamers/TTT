using TTT.RDM.Models;

namespace TTT.RDM;

public interface IRdmStore {
  Task<int> AddDeath(DeathRecord death);
  Task<DeathRecord?> GetDeath(int id);
  Task<IReadOnlyList<DeathRecord>> GetSuspectDeathsForVictim(string victimId,
    int round);

  Task<int> AddCase(RdmCase rdmCase);
  Task<RdmCase?> GetCase(int id);
  Task<IReadOnlyList<RdmCase>> GetOpenCases();
  Task UpdateCase(RdmCase rdmCase);
  Task<bool> HasReport(string reporterId, int deathId);
  Task<int> CountReportsByVictim(string reporterId, int round);

  Task SetSlayDebt(string playerId, int remaining, int sourceCaseId);
  Task<int> GetSlayDebt(string playerId);
  Task<IReadOnlyList<SlayDebt>> GetAllSlayDebts();
}
