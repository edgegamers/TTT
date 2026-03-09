using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using TTT.API.Events;
using TTT.API.Player;

namespace TTT.Karma;

public sealed class KarmaUpdateManager(IServiceProvider provider) : IKarmaUpdateManager {
  private readonly IKarmaService karmaService = provider.GetRequiredService<IKarmaService>();
  private readonly ConcurrentQueue<KarmaUpdate> updateQueue = new();
  private readonly HashSet<string> ignoredReasons = [];
  private readonly HashSet<Event> ignoredSourceEvents = [];
  private readonly List<Func<KarmaUpdate, bool>> ignorePredicates = [];
  
  public void QueueUpdate(KarmaUpdate update) => updateQueue.Enqueue(update);
  public void QueueUpdate(IPlayer player, int delta, Event? sourceEvent = null, string? reason = "Unknown") {
    var update = new KarmaUpdate(player, delta, sourceEvent, reason);
    QueueUpdate(update);
  }

  public void IgnoreReason(string reason) => ignoredReasons.Add(reason);
  public void IgnoreEvent(Event sourceEvent) => ignoredSourceEvents.Add(sourceEvent);
  public void IgnoreIf(Func<KarmaUpdate, bool> predicate) => ignorePredicates.Add(predicate);
  public void ClearIgnores() {
    ignoredReasons.Clear();
    ignoredSourceEvents.Clear();
    ignorePredicates.Clear();
  }

  public async Task ProcessUpdatesAsync() {
    if (updateQueue.IsEmpty) return;
    
    var finalDeltas = new Dictionary<IPlayer, int>();
    while (updateQueue.TryDequeue(out var update)) {
      if (update.Reason != null && ignoredReasons.Contains(update.Reason))
        continue;
      if (update.SourceEvent != null && ignoredSourceEvents.Contains(update.SourceEvent))
        continue;
      if (ignorePredicates.Any(pred => pred(update)))
        continue;
      finalDeltas[update.Player] =
        finalDeltas.GetValueOrDefault(update.Player, 0) + update.Delta;
    }
    
    foreach (var (player, delta) in finalDeltas) {
      var newKarma = await karmaService.Load(player) + delta;
      await karmaService.Write(player, newKarma);
    }
  }
}