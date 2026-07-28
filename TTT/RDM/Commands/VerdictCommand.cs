using Microsoft.Extensions.DependencyInjection;
using TTT.API.Command;
using TTT.API.Player;
using TTT.API.Storage;
using TTT.Locale;
using TTT.RDM.lang;
using TTT.RDM.Models;

namespace TTT.RDM.Commands;

public class VerdictCommand(IServiceProvider provider) : ICommand {
  private readonly ICaseManager cases =
    provider.GetRequiredService<ICaseManager>();

  private readonly IRdmStore store = provider.GetRequiredService<IRdmStore>();

  private readonly ISlayService slay =
    provider.GetRequiredService<ISlayService>();

  private readonly IPlayerFinder finder =
    provider.GetRequiredService<IPlayerFinder>();

  private readonly IMsgLocalizer locale =
    provider.GetRequiredService<IMsgLocalizer>();

  private RdmConfig config
    => provider.GetService<IStorage<RdmConfig>>()?.Load().GetAwaiter()
      .GetResult() ?? new RdmConfig();

  public void Dispose() { }
  public void Start() { }
  public string Id => "verdict";
  public string? Description => "Decide your claimed RDM case";
  public string[] Usage => ["<guilty|forgive>"];
  public string[] RequiredFlags => [config.StaffFlag];

  public async Task<CommandResult> Execute(IOnlinePlayer? executor,
    ICommandInfo info) {
    if (executor == null) return CommandResult.PLAYER_ONLY;

    var claimed = await cases.GetClaimedBy(executor.Id);
    if (claimed == null) {
      info.ReplySync(locale[RdmMsgs.RDM_NO_CLAIMED_CASE()]);
      return CommandResult.ERROR;
    }

    // Args[0] is the command name; Args[1] is the verdict choice
    if (info.ArgCount < 2) {
      info.ReplySync(locale[RdmMsgs.RDM_VERDICT_USAGE()]);
      return CommandResult.PRINT_USAGE;
    }

    var choice = info.Args[1].ToLowerInvariant();
    var death  = await store.GetDeath(claimed.DeathId);

    switch (choice) {
      case "forgive":
      case "forgiven":
        await cases.Resolve(claimed.Id, Verdict.Forgiven, executor);
        info.ReplySync(locale[RdmMsgs.RDM_VERDICT_FORGIVEN(claimed.Id)]);
        return CommandResult.SUCCESS;
      case "guilty":
        await cases.Resolve(claimed.Id, Verdict.Guilty, executor);
        if (death != null) {
          var offender = (IPlayer?)finder.GetPlayerById(death.AttackerId)
            ?? new OfflineRef(death.AttackerId);
          var slays    = config.SlaysForRole(death.VictimRole);
          await slay.ApplyGuilty(
            offender, death.VictimRole,
            claimed.Id);
          info.ReplySync(
            locale[RdmMsgs.RDM_VERDICT_GUILTY(claimed.Id, slays)]);
        }

        return CommandResult.SUCCESS;
      default:
        info.ReplySync(locale[RdmMsgs.RDM_VERDICT_USAGE()]);
        return CommandResult.INVALID_ARGS;
    }
  }

  // Minimal IPlayer for an offline offender so slay debt can be persisted by id.
  private sealed record OfflineRef(string Id) : IPlayer {
    public string Name { get; set; } = Id;
  }
}
