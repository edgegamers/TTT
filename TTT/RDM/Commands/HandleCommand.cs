using Microsoft.Extensions.DependencyInjection;
using TTT.API.Command;
using TTT.API.Player;
using TTT.API.Storage;
using TTT.Locale;
using TTT.RDM.lang;

namespace TTT.RDM.Commands;

public class HandleCommand(IServiceProvider provider) : ICommand {
  private readonly ICaseManager cases =
    provider.GetRequiredService<ICaseManager>();

  private readonly IRdmStore store = provider.GetRequiredService<IRdmStore>();

  private readonly IMsgLocalizer locale =
    provider.GetRequiredService<IMsgLocalizer>();

  private RdmConfig config
    => provider.GetService<IStorage<RdmConfig>>()?.Load().GetAwaiter()
      .GetResult() ?? new RdmConfig();

  public void Dispose() { }
  public void Start() { }
  public string Id => "handle";
  public string? Description => "Claim the next (or a specific) RDM case";
  public string[] Usage => ["[caseId]"];
  public string[] RequiredFlags => [config.StaffFlag];

  public async Task<CommandResult> Execute(IOnlinePlayer? executor,
    ICommandInfo info) {
    if (executor == null) return CommandResult.PLAYER_ONLY;

    // Args[0] is the command name; Args[1] is the optional case id
    var claimed = info.ArgCount >= 2 && int.TryParse(info.Args[1], out var id)
      ? await cases.Claim(executor, id)
      : await cases.ClaimNext(executor);

    if (claimed == null) {
      info.ReplySync(locale[RdmMsgs.RDM_NO_OPEN_CASES()]);
      return CommandResult.ERROR;
    }

    var death = await store.GetDeath(claimed.DeathId);
    info.ReplySync(locale[RdmMsgs.RDM_HANDLED(claimed.Id,
      death?.VictimName ?? "?", death?.AttackerName ?? "?")]);
    return CommandResult.SUCCESS;
  }
}
