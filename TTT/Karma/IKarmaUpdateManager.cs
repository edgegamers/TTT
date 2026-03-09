using TTT.API.Events;
using TTT.API.Player;

namespace TTT.Karma;

public interface IKarmaUpdateManager {
  public void QueueUpdate(KarmaUpdate update);

  public void QueueUpdate(IPlayer player, int delta, Event? sourceEvent = null, string? reason = "Unknown");

  public void IgnoreReason(string reason);
  public void IgnoreEvent(Event sourceEvent);
  public void IgnoreIf(Func<KarmaUpdate, bool> predicate);
  public void ClearIgnores();

  public Task ProcessUpdatesAsync();
}
