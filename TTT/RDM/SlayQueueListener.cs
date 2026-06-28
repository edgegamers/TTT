using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using TTT.API.Events;
using TTT.API.Game;
using TTT.Game.Events.Game;
using TTT.Game.Listeners;

namespace TTT.RDM;

public class SlayQueueListener(IServiceProvider provider)
  : BaseListener(provider) {
  private readonly ISlayService slay =
    provider.GetRequiredService<ISlayService>();

  [EventHandler]
  [UsedImplicitly]
  public void OnRoundStart(GameStateUpdateEvent ev) {
    if (ev.NewState != State.IN_PROGRESS) return;
    Task.Run(async () => await slay.PayRoundStart());
  }
}
