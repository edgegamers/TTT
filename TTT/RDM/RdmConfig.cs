namespace TTT.RDM;

public record RdmConfig {
  public string DbString { get; init; } = "Data Source=rdm.db";

  public int TraitorSlays { get; init; } = 5;
  public int DetectiveSlays { get; init; } = 5;
  public int InnocentSlays { get; init; } = 3;

  public bool NotifyAdmins { get; init; } = true;
  public bool AutoPromptOnSuspectKill { get; init; } = true;
  public int ReportWindowSeconds { get; init; } = 60;
  public int MaxReportsPerVictimPerRound { get; init; } = 3;
  public string StaffFlag { get; init; } = "@ttt/admin";

  /// <summary>
  ///   Number of slays owed when a guilty verdict's victim held the given role.
  ///   roleName is compared against IRole.Name (case-insensitive).
  /// </summary>
  public int SlaysForRole(string roleName) {
    if (roleName.Contains("Traitor", StringComparison.OrdinalIgnoreCase))
      return TraitorSlays;
    if (roleName.Contains("Detective", StringComparison.OrdinalIgnoreCase))
      return DetectiveSlays;
    return InnocentSlays;
  }
}
