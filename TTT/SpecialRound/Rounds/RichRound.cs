using CounterStrikeSharp.API;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using ShopAPI;
using ShopAPI.Events;
using SpecialRound.lang;
using SpecialRoundAPI;
using SpecialRoundAPI.Configs;
using TTT.API.Events;
using TTT.API.Storage;
using TTT.Game.Events.Game;
using TTT.Locale;

namespace SpecialRound.Rounds;

public class RichRound(IServiceProvider provider)
  : AbstractSpecialRound(provider) {
  private const string BONUS_REASON = "Rich Round Bonus";

  private readonly IShop shop = provider.GetRequiredService<IShop>();

  public override string Name => "Rich";
  public override IMsg Description => RoundMsgs.SPECIAL_ROUND_RICH;
  public override SpecialRoundConfig Config => config;

  private RichRoundConfig config
    => Provider.GetService<IStorage<RichRoundConfig>>()
    ?.Load()
     .GetAwaiter()
     .GetResult() ?? new RichRoundConfig();

  // The "Round Start" credits are granted (RoleAssignCreditor, partly on a
  // background task) BEFORE this round is registered active, so intercepting
  // that balance event never worked for the starting bonus. Instead, once the
  // starting credits have landed (a few ticks after enable), double them
  // directly. In-round gains are still multiplied via OnBalanceChange below.
  public override void ApplyRoundEffects() {
    Server.RunOnTick(Server.TickCount + 10, () => {
      if (!Tracker.ActiveRounds.Contains(this)) return;
      var mult = config.BonusCreditsMultiplier;
      foreach (var player in Finder.GetOnline()) {
        var bal = shop.Load(player).GetAwaiter().GetResult();
        if (bal <= 0) continue;
        var bonus = (int)(bal * (mult - 1f));
        if (bonus != 0) shop.AddBalance(player, bonus, BONUS_REASON, false);
      }
    });
  }

  [UsedImplicitly]
  [EventHandler]
  public void OnBalanceChange(PlayerBalanceEvent ev) {
    if (!Tracker.ActiveRounds.Contains(this)) return;
    if (ev.Reason == BONUS_REASON) return; // our own grant — don't re-multiply
    if (ev.NewBalance <= ev.OldBalance) return; // only multiply gains

    var gain = ev.NewBalance - ev.OldBalance;
    gain          = (int)(gain * config.AdditiveCreditsMultiplier);
    ev.NewBalance = ev.OldBalance + gain;
  }

  public override bool ConflictsWith(AbstractSpecialRound other) {
    return other is VanillaRound;
  }

  public override void OnGameState(GameStateUpdateEvent ev) { }
}
