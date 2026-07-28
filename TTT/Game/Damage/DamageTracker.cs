using JetBrains.Annotations;
using TTT.API.Events;
using TTT.API.Game;
using TTT.Game.Events.Game;
using TTT.Game.Events.Player;
using TTT.Game.Listeners;

namespace TTT.Game.Damage;

public class DamageTracker(IServiceProvider provider)
  : BaseListener(provider), IDamageTracker {
  // Ordered pairs (attackerId, victimId) of who damaged whom first this round.
  private readonly HashSet<(string, string)> firstDamage = [];

  public void RecordFirstDamage(string attackerId, string victimId) {
    // If the victim already hit the attacker first, this is not first damage.
    if (firstDamage.Contains((victimId, attackerId))) return;
    firstDamage.Add((attackerId, victimId));
  }

  public KillFault GetFault(string killerId, string victimId) {
    if (firstDamage.Contains((killerId, victimId)))
      return KillFault.KillerGuilty;
    if (firstDamage.Contains((victimId, killerId)))
      return KillFault.VictimGuilty;
    return KillFault.Unknown;
  }

  public void Clear() { firstDamage.Clear(); }

  [EventHandler]
  [UsedImplicitly]
  public void OnHurt(PlayerDamagedEvent ev) {
    if (Games.ActiveGame is not { State: State.IN_PROGRESS }) return;
    var attacker = ev.Attacker;
    if (attacker == null) return;
    RecordFirstDamage(attacker.Id, ev.Player.Id);
  }

  [EventHandler]
  [UsedImplicitly]
  public void OnRoundStart(GameStateUpdateEvent ev) {
    if (ev.NewState == State.IN_PROGRESS) Clear();
  }
}
