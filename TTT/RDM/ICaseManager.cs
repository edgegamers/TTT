using TTT.API.Player;
using TTT.RDM.Models;

namespace TTT.RDM;

public interface ICaseManager {
  Task<RdmCase?> FileReport(IOnlinePlayer reporter, int deathId,
    string? reason);
  Task<RdmCase?> ClaimNext(IPlayer admin);
  Task<RdmCase?> Claim(IPlayer admin, int caseId);
  Task Resolve(int caseId, Verdict verdict, IPlayer admin);
  Task<IReadOnlyList<RdmCase>> GetOpen();
}
