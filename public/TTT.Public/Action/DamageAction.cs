using CounterStrikeSharp.API.Core;
using TTT.Public.Formatting;
using TTT.Public.Mod.Role;

namespace TTT.Public.Action;

public class DamageAction : Action {
  private readonly Tuple<CCSPlayerController, Role>? _actor;
  private readonly Tuple<CCSPlayerController, Role> victim;
  private readonly int _damage;
  private readonly int _roundTime;

  public DamageAction(Tuple<CCSPlayerController, Role>? actor,
    Tuple<CCSPlayerController, Role> victim, int damage, int roundTime) {
    _actor     = actor;
    victim     = victim;
    _damage    = damage;
    _roundTime = roundTime;
  }

  public override CCSPlayerController Actor() { return _actor.Item1; }

  private string GetActorName() {
    return _actor?.Item2.FormatStringFullAfter(" " + _actor?.Item1.PlayerName)
      ?? "World";
  }

  public override string ActionMessage() {
    var attackedPlayerRole = victim.Item2;
    return $"[TTT] {GetActorName()}"
      + $" damaged {attackedPlayerRole.FormatStringFullAfter(" " + victim.Item1.PlayerName)}"
      + $" for {_damage} hp.";
  }
}