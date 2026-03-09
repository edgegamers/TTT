using Microsoft.Extensions.DependencyInjection;
using TTT.API.Extensions;

namespace TTT.Karma;

public static class KarmaServiceCollection {
  public static void AddKarmaService(this IServiceCollection collection) {
    collection.AddSingleton<IKarmaUpdateManager, KarmaUpdateManager>();
    // collection.AddModBehavior<IKarmaService, KarmaStorage>();
    collection.AddModBehavior<IKarmaService, KarmaStorageKV>();
    collection.AddModBehavior<KarmaListener>();
    collection.AddModBehavior<KarmaCommand>();
  }
}