using System.Linq;
using System.Text;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.DependencyInjection;
using ShopAPI;
using TTT.API;
using TTT.API.Command;
using TTT.API.Player;

namespace TTT.CS2.Command;

public class PlayerPingShopAlias(IServiceProvider provider) : IPluginModule {
  private readonly IPlayerConverter<CCSPlayerController> converter =
    provider.GetRequiredService<IPlayerConverter<CCSPlayerController>>();

  private readonly IItemSorter itemSorter =
    provider.GetRequiredService<IItemSorter>();

  private readonly IShop shop = provider.GetRequiredService<IShop>();

  // slot -> (rendered html, expiry). Refreshed on a timer so it stays on screen.
  private readonly Dictionary<int, (string html, DateTime expiry)> open = new();

  public void Dispose() { }
  public void Start() { }

  public void Start(BasePlugin? plugin) {
    plugin?.AddCommandListener("player_ping", onPlayerPing, HookMode.Post);

    // Center HTML only shows for a moment, so re-send it while the menu is open.
    plugin?.AddTimer(0.25f, refresh, TimerFlags.REPEAT);

    for (var i = 0; i < 10; i++) {
      var index = i; // Capture variable
      plugin?.AddCommand($"css_{index}", "",
        (player, _) => { onButton(player, index); });
    }
  }

  private void refresh() {
    if (open.Count == 0) return;
    var now = DateTime.Now;
    foreach (var slot in open.Keys.ToList()) {
      var (html, expiry) = open[slot];
      var controller     = Utilities.GetPlayerFromSlot(slot);
      if (now > expiry || controller is not { IsValid: true, PawnIsAlive: true }) {
        open.Remove(slot);
        continue;
      }

      controller.PrintToCenterHtml(html);
    }
  }

  // Ping opens an on-screen (center HTML) list. Buying is unchanged: the
  // css_0..css_9 quick-buy commands below stay armed for 20s.
  private HookResult onPlayerPing(CCSPlayerController? player,
    CommandInfo commandInfo) {
    if (player == null || !player.IsValid) return HookResult.Continue;
    if (converter.GetPlayer(player) is not IOnlinePlayer apiPlayer)
      return HookResult.Continue;

    // Snapshot + arm the css_N order, then render once we have the balance.
    var items = itemSorter.GetSortedItems(apiPlayer, true);
    var slot  = player.Slot;
    Task.Run(async () => {
      var balance = await shop.Load(apiPlayer);
      Server.NextWorldUpdate(() => {
        open[slot] = (buildHtml(apiPlayer, items, balance),
          DateTime.Now.AddSeconds(12));
      });
    });

    return HookResult.Continue;
  }

  private string buildHtml(IOnlinePlayer apiPlayer, List<IShopItem> items,
    int balance) {
    var sb = new StringBuilder();
    sb.Append(
      $"<font color='#4ea1ff'>=(eGO)= SHOP</font>  <font color='#cccccc'>{balance} credits</font><br>");

    for (var i = 0; i < items.Count; i++) {
      var item   = items[i];
      var canBuy = item.CanPurchase(apiPlayer) == PurchaseResult.SUCCESS
        && item.Config.Price <= balance;
      var color = canBuy ? "#7CFC00" : "#888888";
      sb.Append(
        $"<font color='{color}'>{i + 1}. {item.Name} — {item.Config.Price}</font><br>");
    }

    sb.Append("<font color='#aaaaaa'>press 1-9 (bound to css_N) to buy</font>");
    return sb.ToString();
  }

  private void onButton(CCSPlayerController? player, int index) {
    if (player == null) return;
    if (converter.GetPlayer(player) is not IOnlinePlayer apiPlayer) return;

    var lastUpdated = itemSorter.GetLastUpdate(apiPlayer);
    if (lastUpdated == null
      || DateTime.Now - lastUpdated > TimeSpan.FromSeconds(20))
      return;
    var cmdInfo = new CS2CommandInfo(provider, apiPlayer, 0, "css_shop", "buy",
      (index - 1).ToString()) { CallingContext = CommandCallingContext.Chat };
    provider.GetRequiredService<ICommandManager>().ProcessCommand(cmdInfo);
    itemSorter.InvalidateOrder(apiPlayer);
  }
}
