namespace TTT.RDM.Models;

public record SlayDebt {
  public required string PlayerId { get; init; }
  public required int RemainingSlays { get; init; }
  public int SourceCaseId { get; init; }
}
