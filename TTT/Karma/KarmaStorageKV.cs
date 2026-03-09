using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using TTT.API.Events;
using TTT.API.Player;
using TTT.API.Storage;
using TTT.Karma.Events;

namespace TTT.Karma;

public sealed class KarmaStorageKV(IServiceProvider provider) : IKarmaService {
  private readonly ConcurrentDictionary<string, int> data = new();
  private readonly IEventBus? eventBus = provider.GetService<IEventBus>();

  private KarmaConfig config
    => provider.GetService<IStorage<KarmaConfig>>()
      ?.Load()
       .GetAwaiter()
       .GetResult() ?? new KarmaConfig();

  public Task<int> Load(IPlayer key) {
    var karma = data.GetValueOrDefault(key.Id, config.DefaultKarma);
    return Task.FromResult(karma);
  }

  public async Task Write(IPlayer key, int newData) {
    var oldKarma = await Load(key);
    var karmaUpdateEvent = new KarmaUpdateEvent(key, oldKarma, newData);
    eventBus?.Dispatch(karmaUpdateEvent);
    if (karmaUpdateEvent.IsCanceled) return;

    data[key.Id] = karmaUpdateEvent.Karma;
  }

  public void Dispose() { }
  public void Start() { }
}
