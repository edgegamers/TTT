using System.Drawing;
using TTT.API.Player;

namespace TTT.Game.Roles;

public abstract class RatioBasedRole(IServiceProvider provider)
  : BaseRole(provider) {
  private static readonly Random rng = new();

  public override abstract string Id { get; }
  public override abstract string Name { get; }
  public override abstract Color Color { get; }

  abstract protected Func<int, int> TargetCount { get; }

  public override IOnlinePlayer?
    FindPlayerToAssign(ISet<IOnlinePlayer> players) {
    var currentCount =
      players.Count(p => Roles.GetRoles(p).Any(r => r.Id == Id));
    if (currentCount >= TargetCount(players.Count)) return null;

    var candidates =
      players.Where(p => Roles.GetRoles(p).Count == 0).ToList();
    if (candidates.Count == 0) return null;

    // Prefer players who did NOT hold this role last round so roles rotate
    // (classic TTT behaviour). 1-in-6 chance to ignore the preference so we
    // never starve when few "fresh" candidates remain.
    var fresh = candidates
     .Where(p => Roles.GetPreviousRoles(p).All(r => r.Id != Id))
     .ToList();
    var pool = fresh.Count > 0 && rng.Next(6) != 0 ? fresh : candidates;

    return pool[rng.Next(pool.Count)];
  }
}