using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TTT.API;

namespace TTT.Plugin;

public class TTT(IServiceProvider provider) : BasePlugin {
  private readonly List<ITerrorModule> loadedModules = [];
  private IServiceScope scope = null!;
  public override string ModuleName => "TTT";

  public override string ModuleVersion
    => $"{GitVersionInformation.BranchName}-{GitVersionInformation.FullSemVer}-{GitVersionInformation.BuildMetaData}";

  public override void Load(bool hotReload) {
    Logger.LogInformation($"{ModuleName} {ModuleVersion} Starting... ");

    // TEMP crash instrumentation: surface anything currently swallowed
    // (process-terminating throws + unobserved async exceptions). Remove after.
    AppDomain.CurrentDomain.UnhandledException += (_, e) => {
      Console.WriteLine("[TTT-DBG] UNHANDLED " + e.ExceptionObject);
      Console.Out.Flush();
    };
    System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) => {
      Console.WriteLine("[TTT-DBG] UNOBSERVED-TASK " + e.Exception);
      Console.Out.Flush();
      e.SetObserved();
    };

    scope = provider.CreateScope();
    var modules = scope.ServiceProvider.GetServices<ITerrorModule>().ToList();
    Logger.LogInformation($"Found {modules.Count} base modules to load.");

    foreach (var module in modules) {
      if (module is IPluginModule) continue;
      module.Start();
      loadedModules.Add(module);
      Logger.LogInformation(
        $"Loaded {module.Version} {module.Id} {module.GetType().Namespace}");
    }

    var pluginModules = modules.Where(m => m is IPluginModule)
     .Cast<IPluginModule>()
     .ToList();

    Logger.LogInformation(
      $"Found {pluginModules.Count} plugin modules, loading...");

    foreach (var module in pluginModules) {
      Logger.LogInformation(
        $"Registering {module.Version} {module.Id} {module.GetType().Namespace}");
      module.Start(this, hotReload);
      RegisterAllAttributes(module);
      loadedModules.Add(module);
      Logger.LogInformation(
        $"Registered {module.Version} {module.Id} {module.GetType().Namespace}");
    }

    Logger.LogInformation("All modules loaded successfully.");
  }

  override protected void Dispose(bool disposing) {
    if (!disposing) {
      base.Dispose(disposing);
      return;
    }

    foreach (var module in loadedModules)
      try {
        Logger.LogInformation($"Unloading {module.Id} ({module.Version})");
        module.Dispose();
      } catch (Exception e) {
        Logger.LogError(e, $"Error unloading module {module.Id}");
      }

    base.Dispose(disposing);
  }
}