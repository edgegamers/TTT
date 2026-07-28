using Microsoft.Extensions.DependencyInjection;
using TTT.API.Role;
using TTT.Game.Roles;
using TTT.RDM;
using Xunit;

namespace TTT.Test.RDM;

public class RdmClassifierTests {
  public enum R { Innocent, Traitor, Detective }

  private readonly IList<IRole> roles;

  public RdmClassifierTests(IServiceProvider provider) {
    roles = new List<IRole> {
      new InnocentRole(provider),
      new TraitorRole(provider),
      new DetectiveRole(provider)
    };
  }

  [Theory]
  // same role -> suspect
  [InlineData(R.Innocent, R.Innocent, true)]
  [InlineData(R.Traitor, R.Traitor, true)]
  [InlineData(R.Detective, R.Detective, true)]
  // different roles, neither traitor -> suspect
  [InlineData(R.Innocent, R.Detective, true)]
  [InlineData(R.Detective, R.Innocent, true)]
  // different roles, one is traitor -> NOT suspect
  [InlineData(R.Traitor, R.Innocent, false)]
  [InlineData(R.Traitor, R.Detective, false)]
  [InlineData(R.Innocent, R.Traitor, false)]
  [InlineData(R.Detective, R.Traitor, false)]
  public void IsSuspectKill_MatchesBadKillTable(R killer, R victim,
    bool expected) {
    Assert.Equal(expected,
      RdmClassifier.IsSuspectKill(roles[(int)killer], roles[(int)victim]));
  }
}
