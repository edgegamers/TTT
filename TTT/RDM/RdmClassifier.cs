using TTT.API.Role;
using TTT.Game.Roles;

namespace TTT.RDM;

public static class RdmClassifier {
  /// <summary>
  ///   A kill is "suspect" (worth an RDM report) when the parties share a role
  ///   (teamkill) or neither is a Traitor. A Traitor killing a non-Traitor (or
  ///   vice versa) is legitimate and not suspect.
  /// </summary>
  public static bool IsSuspectKill(IRole killer, IRole victim) {
    var killerIsTraitor = killer is TraitorRole;
    var victimIsTraitor = victim is TraitorRole;

    if (killer.GetType() == victim.GetType()) return true; // same role
    return !killerIsTraitor && !victimIsTraitor;           // neither is traitor
  }
}
