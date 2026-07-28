using Microsoft.Extensions.DependencyInjection;
using TTT.API.Command;
using TTT.API.Player;
using TTT.API.Storage;
using TTT.Locale;
using TTT.RDM.lang;

namespace TTT.RDM.Commands;

public class InfoCommand(IServiceProvider provider) : ICommand {
  private readonly IRdmStore store = provider.GetRequiredService<IRdmStore>();

  private readonly IMsgLocalizer locale =
    provider.GetRequiredService<IMsgLocalizer>();

  private RdmConfig config
    => provider.GetService<IStorage<RdmConfig>>()?.Load().GetAwaiter()
      .GetResult() ?? new RdmConfig();

  public void Dispose() { }
  public void Start() { }
  public string Id => "info";
  public string? Description => "Show details for an RDM case";
  public string[] Usage => ["<caseId>"];
  public string[] RequiredFlags => [config.StaffFlag];

  public async Task<CommandResult> Execute(IOnlinePlayer? executor,
    ICommandInfo info) {
    // Args[0] is the command name; Args[1] is the first real user argument
    if (info.ArgCount < 2 || !int.TryParse(info.Args[1], out var caseId))
      return CommandResult.PRINT_USAGE;

    var c = await store.GetCase(caseId);
    var death = c == null ? null : await store.GetDeath(c.DeathId);
    if (c == null || death == null) {
      info.ReplySync(locale[RdmMsgs.RDM_CASE_NOT_FOUND()]);
      return CommandResult.ERROR;
    }

    info.ReplySync(locale[RdmMsgs.RDM_INFO(c.Id, death.VictimName,
      death.VictimRole, death.AttackerName, death.AttackerRole,
      death.Weapon ?? "unknown", c.Reason ?? "(none)")]);
    return CommandResult.SUCCESS;
  }
}
