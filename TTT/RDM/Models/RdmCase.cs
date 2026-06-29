namespace TTT.RDM.Models;

public record RdmCase {
  public int Id { get; init; }
  public required int DeathId { get; init; }
  public required string ReporterId { get; init; }
  public string? Reason { get; init; }
  public CaseState State { get; init; } = CaseState.Open;
  public string? HandlerAdminId { get; init; }
  public Verdict Verdict { get; init; } = Verdict.None;
  public required DateTime CreatedAt { get; init; }
}
