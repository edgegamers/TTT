using Microsoft.Extensions.DependencyInjection;
using TTT.API.Events;
using TTT.API.Messages;
using TTT.API.Player;
using TTT.API.Role;
using TTT.Game.Events.Player;

namespace TTT.Game.Roles;

public class RoleAssigner(IServiceProvider provider) : IRoleAssigner {
  private readonly IDictionary<string, ICollection<IRole>> assignedRoles =
    new Dictionary<string, ICollection<IRole>>();

  private IDictionary<string, ICollection<IRole>> lastRoles =
    new Dictionary<string, ICollection<IRole>>();

  private readonly IEventBus bus = provider.GetRequiredService<IEventBus>();

  private readonly IMessenger? onlineMessenger =
    provider.GetService<IMessenger>();

  public void AssignRoles(ISet<IOnlinePlayer> players, IList<IRole> roles) {
    // Snapshot the previous round's assignments before clearing so role
    // selection can avoid handing a player the same role twice in a row.
    lastRoles = new Dictionary<string, ICollection<IRole>>(assignedRoles);
    assignedRoles.Clear();

    // Selection is randomized per-role in RatioBasedRole.FindPlayerToAssign,
    // so no pre-shuffle is needed (the old OrderBy(...).ToHashSet() shuffle was
    // discarded by ToHashSet anyway, making picks deterministic by SteamID).
    var pool = players.ToHashSet();
    bool roleAssigned;
    do { roleAssigned = tryAssignRole(pool, roles); } while (roleAssigned);
  }

  public ICollection<IRole> GetPreviousRoles(IPlayer player) {
    return lastRoles.TryGetValue(player.Id, out var roles) ? roles : [];
  }

  public Task<ICollection<IRole>?> Load(IPlayer key) {
    assignedRoles.TryGetValue(key.Id, out var roles);
    return Task.FromResult(roles);
  }

  public Task Write(IPlayer key, ICollection<IRole> newData) {
    assignedRoles[key.Id] = newData;
    return Task.CompletedTask;
  }

  private bool tryAssignRole(HashSet<IOnlinePlayer> players,
    IList<IRole> roles) {
    foreach (var role in roles) {
      var player = role.FindPlayerToAssign(players);
      if (player is null) continue;

      var ev = new PlayerRoleAssignEvent(player, role);
      bus.Dispatch(ev);

      if (ev.IsCanceled) continue;

      if (!assignedRoles.ContainsKey(player.Id))
        assignedRoles[player.Id] = new List<IRole>();
      assignedRoles[player.Id].Add(ev.Role);
      ev.Role.OnAssign(player);

      onlineMessenger?.Debug(
        $"{player.Name} was assigned the role of {role.Name}.");
      return true;
    }

    return false;
  }
}