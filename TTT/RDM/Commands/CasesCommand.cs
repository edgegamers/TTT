using Microsoft.Extensions.DependencyInjection;
using TTT.API.Command;
using TTT.API.Player;
using TTT.API.Storage;
using TTT.Locale;
using TTT.RDM.lang;

namespace TTT.RDM.Commands;

public class CasesCommand(IServiceProvider provider) : ICommand {
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
  public string Id => "cases";
  public string? Description => "List open RDM cases";
  public string[] RequiredFlags => [config.StaffFlag];

  public async Task<CommandResult> Execute(IOnlinePlayer? executor,
    ICommandInfo info) {
    var open = await cases.GetOpen();
    info.ReplySync(locale[RdmMsgs.RDM_CASES_COUNT(open.Count)]);
    foreach (var c in open) {
      var death = await store.GetDeath(c.DeathId);
      if (death != null)
        info.ReplySync(locale[RdmMsgs.RDM_CASES_ENTRY(c.Id, death.VictimName,
          death.AttackerName)]);
    }

    return CommandResult.SUCCESS;
  }
}
