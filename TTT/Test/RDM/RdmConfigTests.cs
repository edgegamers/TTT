using TTT.RDM;
using Xunit;

namespace TTT.Test.RDM;

public class RdmConfigTests {
  [Fact]
  public void Defaults_AreSourceModParity() {
    var config = new RdmConfig();
    Assert.Equal(5, config.TraitorSlays);
    Assert.Equal(5, config.DetectiveSlays);
    Assert.Equal(3, config.InnocentSlays);
    Assert.Equal("@ttt/admin", config.StaffFlag);
  }

  [Theory]
  [InlineData(" Traitor", 5)]
  [InlineData(" Detective", 5)]
  [InlineData(" Innocent", 3)]
  public void SlaysForRole_MapsByRoleName(string roleName, int expected) {
    Assert.Equal(expected, new RdmConfig().SlaysForRole(roleName));
  }
}
