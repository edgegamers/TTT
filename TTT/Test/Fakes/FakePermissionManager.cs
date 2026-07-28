using TTT.API.Player;

namespace TTT.Test.Fakes;

public class FakePermissionManager : IPermissionManager {
  private readonly Dictionary<string, HashSet<string>> playerFlags = new();

  public void SetFlags(IPlayer player, params string[] grant) {
    playerFlags[player.Id] = [..grant];
  }

  public bool HasFlags(IPlayer player, params string[] flags) {
    if (flags.Length == 0) return true;
    return playerFlags.TryGetValue(player.Id, out var f) && flags.All(f.Contains);
  }

  public bool InGroups(IPlayer player, params string[] groups) { return true; }
}