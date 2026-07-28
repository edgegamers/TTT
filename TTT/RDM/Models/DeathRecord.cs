using TTT.Game.Damage;

namespace TTT.RDM.Models;

public record DeathRecord {
  public int Id { get; init; }
  public required int Round { get; init; }
  public required string VictimId { get; init; }
  public required string VictimName { get; init; }
  public required string VictimRole { get; init; }
  public required string AttackerId { get; init; }
  public required string AttackerName { get; init; }
  public required string AttackerRole { get; init; }
  public string? Weapon { get; init; }
  public required DateTime Timestamp { get; init; }
  public required bool IsSuspect { get; init; }
  public required KillFault Fault { get; init; }
}
