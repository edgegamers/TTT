using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.DependencyInjection;
using TTT.Detective;
using TTT.Logs;
using TTT.Manager;
using TTT.Player;
using TTT.Roles;
using TTT.Round;

namespace TTT;

public class TTTServiceCollection : IPluginServiceCollection<TTTPlugin> {
  public void ConfigureServices(IServiceCollection serviceCollection) {
    serviceCollection.AddTTTRoles();
    serviceCollection.AddRoundService();
    serviceCollection.AddPlayerService();
    serviceCollection.AddDetectiveBehavior();
    serviceCollection.AddLogsService();
    serviceCollection.AddManagerService();
  }
}