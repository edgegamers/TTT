using Microsoft.Extensions.DependencyInjection;
using TTT.API.Extensions;
using TTT.RDM.Commands;

namespace TTT.RDM;

public static class RdmServiceCollection {
  public static void AddRdmService(this IServiceCollection collection) {
    // Persistence (production: SQLite). Config is resolved lazily so the
    // connection string comes from IStorage<RdmConfig> when present.
    collection.AddSingleton<IRdmStore>(provider => {
      var config = provider.GetService<TTT.API.Storage.IStorage<RdmConfig>>()
       ?.Load().GetAwaiter().GetResult() ?? new RdmConfig();
      return new SqliteRdmStore(config.DbString);
    });

    collection.AddSingleton<ICaseManager, CaseManager>();
    collection.AddSingleton<ISlayService, SlayService>();

    // Listeners
    collection.AddModBehavior<DeathLogListener>();
    collection.AddModBehavior<SlayQueueListener>();

    // Commands
    collection.AddModBehavior<RdmCommand>();
    collection.AddModBehavior<CasesCommand>();
    collection.AddModBehavior<InfoCommand>();
    collection.AddModBehavior<HandleCommand>();
    collection.AddModBehavior<VerdictCommand>();
  }
}
