using Microsoft.Extensions.DependencyInjection;
using TTT.API.Command;
using TTT.API.Player;
using TTT.Locale;
using TTT.RDM.lang;

namespace TTT.RDM.Commands;

public class RdmCommand(IServiceProvider provider) : ICommand {
  private readonly ICaseManager cases =
    provider.GetRequiredService<ICaseManager>();

  private readonly IRdmStore store = provider.GetRequiredService<IRdmStore>();

  private readonly IMsgLocalizer locale =
    provider.GetRequiredService<IMsgLocalizer>();

  private readonly DeathLogListener deathLog =
    provider.GetRequiredService<DeathLogListener>();

  public void Dispose() { }
  public void Start() { }
  public string Id => "rdm";
  public string? Description => "List suspect deaths this round or file a report.";
  public string[] Usage => ["[number] [reason]"];

  public async Task<CommandResult> Execute(IOnlinePlayer? executor,
    ICommandInfo info) {
    if (executor == null) return CommandResult.PLAYER_ONLY;

    var round  = deathLog.CurrentRound;
    var deaths = await store.GetSuspectDeathsForVictim(executor.Id, round);

    // ArgCount == 1 means just the command name, no extra arguments
    if (info.ArgCount == 1) {
      if (deaths.Count == 0) {
        info.ReplySync(locale[RdmMsgs.RDM_LIST_EMPTY()]);
        return CommandResult.SUCCESS;
      }

      info.ReplySync(locale[RdmMsgs.RDM_LIST_HEADER()]);
      for (var i = 0; i < deaths.Count; i++)
        info.ReplySync(
          locale[RdmMsgs.RDM_LIST_ENTRY(i + 1, deaths[i].AttackerName)]);
      return CommandResult.SUCCESS;
    }

    // Args[1] is the first real argument (the death index number)
    if (!int.TryParse(info.Args[1], out var index) || index < 1
      || index > deaths.Count) {
      info.ReplySync(locale[RdmMsgs.RDM_REPORT_REJECTED()]);
      return CommandResult.INVALID_ARGS;
    }

    var reason = info.ArgCount > 2
      ? string.Join(' ', info.Args.Skip(2))
      : null;
    var filed = await cases.FileReport(executor, deaths[index - 1].Id, reason);

    info.ReplySync(filed == null
      ? locale[RdmMsgs.RDM_REPORT_REJECTED()]
      : locale[RdmMsgs.RDM_REPORT_FILED(filed.Id)]);
    return filed == null ? CommandResult.ERROR : CommandResult.SUCCESS;
  }
}
