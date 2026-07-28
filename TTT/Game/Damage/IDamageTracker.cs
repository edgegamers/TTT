using TTT.API.Events;

namespace TTT.Game.Damage;

public interface IDamageTracker : IListener {
  /// <summary>Record that attacker dealt the first damage to victim this round.</summary>
  void RecordFirstDamage(string attackerId, string victimId);

  /// <summary>Who threw the first punch between these two this round.</summary>
  KillFault GetFault(string killerId, string victimId);

  /// <summary>Clear all first-damage state (called at round start).</summary>
  void Clear();
}
