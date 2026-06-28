using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Cvars.Validators;
using TTT.API;
using TTT.API.Storage;
using TTT.RDM;

namespace TTT.CS2.Configs;

public class CS2RdmConfig : IStorage<RdmConfig>, IPluginModule {
  public static readonly FakeConVar<string> CV_DB_STRING = new(
    "css_ttt_rdm_db_string", "Database connection string for RDM storage",
    "Data Source=rdm.db");

  public static readonly FakeConVar<int> CV_TRAITOR_SLAYS = new(
    "css_ttt_rdm_traitor_slays", "Slays when the RDM victim was a Traitor", 5,
    ConVarFlags.FCVAR_NONE, new RangeValidator<int>(0, 100));

  public static readonly FakeConVar<int> CV_DETECTIVE_SLAYS = new(
    "css_ttt_rdm_detective_slays", "Slays when the RDM victim was a Detective",
    5, ConVarFlags.FCVAR_NONE, new RangeValidator<int>(0, 100));

  public static readonly FakeConVar<int> CV_INNOCENT_SLAYS = new(
    "css_ttt_rdm_innocent_slays", "Slays when the RDM victim was an Innocent",
    3, ConVarFlags.FCVAR_NONE, new RangeValidator<int>(0, 100));

  public static readonly FakeConVar<bool> CV_NOTIFY_ADMINS = new(
    "css_ttt_rdm_notify_admins", "Message online staff when a report is filed",
    true);

  public static readonly FakeConVar<bool> CV_AUTO_PROMPT = new(
    "css_ttt_rdm_auto_prompt", "Prompt victims to report after a suspect kill",
    true);

  public static readonly FakeConVar<int> CV_REPORT_WINDOW = new(
    "css_ttt_rdm_report_window_seconds",
    "Seconds after a death during which a victim may report it", 60,
    ConVarFlags.FCVAR_NONE, new RangeValidator<int>(1, 3600));

  public static readonly FakeConVar<int> CV_MAX_REPORTS = new(
    "css_ttt_rdm_max_reports_per_round",
    "Max reports a single victim may file per round", 3,
    ConVarFlags.FCVAR_NONE, new RangeValidator<int>(1, 100));

  public static readonly FakeConVar<string> CV_STAFF_FLAG = new(
    "css_ttt_rdm_staff_flag", "Admin flag required for RDM staff commands",
    "@ttt/admin");

  public void Dispose() { }
  public void Start() { }

  public Task<RdmConfig?> Load() {
    return Task.FromResult<RdmConfig?>(new RdmConfig {
      DbString = CV_DB_STRING.Value,
      TraitorSlays = CV_TRAITOR_SLAYS.Value,
      DetectiveSlays = CV_DETECTIVE_SLAYS.Value,
      InnocentSlays = CV_INNOCENT_SLAYS.Value,
      NotifyAdmins = CV_NOTIFY_ADMINS.Value,
      AutoPromptOnSuspectKill = CV_AUTO_PROMPT.Value,
      ReportWindowSeconds = CV_REPORT_WINDOW.Value,
      MaxReportsPerVictimPerRound = CV_MAX_REPORTS.Value,
      StaffFlag = CV_STAFF_FLAG.Value
    });
  }
}
