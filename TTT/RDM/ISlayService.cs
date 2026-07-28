using TTT.API.Player;

namespace TTT.RDM;

public interface ISlayService {
  /// <summary>Apply a guilty verdict: slay now if alive, queue the remainder.</summary>
  Task ApplyGuilty(IPlayer offender, string victimRole, int caseId);

  /// <summary>Pay one slay for each alive debtor at round start. Returns slays applied.</summary>
  Task<int> PayRoundStart();
}
