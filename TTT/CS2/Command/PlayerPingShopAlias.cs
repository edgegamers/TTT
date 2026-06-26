using System.Linq;
using System.Text;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.DependencyInjection;
using ShopAPI;
using TTT.API;
using TTT.API.Command;
using TTT.API.Player;

namespace TTT.CS2.Command;

// Ping opens an on-screen, navigable shop menu — no key binds required:
//   W / S  -> move the highlight,  E (use) -> buy it,  ping again -> close.
// css_0..css_9 are kept as an optional quick-buy for anyone who does bind keys.
public class PlayerPingShopAlias(IServiceProvider provider) : IPluginModule {
  private readonly IPlayerConverter<CCSPlayerController> converter =
    provider.GetRequiredService<IPlayerConverter<CCSPlayerController>>();

  private readonly IItemSorter itemSorter =
    provider.GetRequiredService<IItemSorter>();

  private readonly IShop shop = provider.GetRequiredService<IShop>();

  private sealed class Menu {
    public required List<IShopItem> Items;
    public          int            Balance;
    public          int            Selected;
    public          DateTime       Expiry;
  }

  // slot -> open menu. Re-rendered on a timer; navigated via the buttons hook.
  private readonly Dictionary<int, Menu> open       = new();
  private const    int                   MenuSeconds = 15;

  public void Dispose() { }
  public void Start() { }

  public void Start(BasePlugin? plugin) {
    plugin?.AddCommandListener("player_ping", onPlayerPing, HookMode.Post);
    plugin?.RegisterListener<
      CounterStrikeSharp.API.Core.Listeners.OnPlayerButtonsChanged>(onButtons);

    // Center HTML only shows for a moment, so re-send it while the menu is open.
    plugin?.AddTimer(0.25f, refresh, TimerFlags.REPEAT);

    for (var i = 0; i < 10; i++) {
      var index = i; // capture
      plugin?.AddCommand($"css_{index}", "",
        (player, _) => { onButton(player, index); });
    }
  }

  // Ping toggles the menu open/closed.
  private HookResult onPlayerPing(CCSPlayerController? player,
    CommandInfo commandInfo) {
    if (player == null || !player.IsValid) return HookResult.Continue;

    var slot = player.Slot;
    if (open.Remove(slot)) return HookResult.Continue; // re-ping closes it

    if (converter.GetPlayer(player) is not IOnlinePlayer apiPlayer)
      return HookResult.Continue;

    // Snapshot + lock the sorted order, then open the menu once we have balance.
    var items = itemSorter.GetSortedItems(apiPlayer, true);
    Task.Run(async () => {
      var balance = await shop.Load(apiPlayer);
      Server.NextWorldUpdate(() => {
        open[slot] = new Menu {
          Items = items, Balance = balance, Selected = 0,
          Expiry = DateTime.Now.AddSeconds(MenuSeconds)
        };
      });
    });

    return HookResult.Continue;
  }

  // W/S move the highlight; E (use) buys the highlighted item. Only active while
  // the player has the menu open, so normal movement is unaffected otherwise.
  private void onButtons(CCSPlayerController player, PlayerButtons pressed,
    PlayerButtons released) {
    if (!player.IsValid || !open.TryGetValue(player.Slot, out var menu)) return;
    if (menu.Items.Count == 0) return;

    if (pressed.HasFlag(PlayerButtons.Forward)) {
      menu.Selected = (menu.Selected - 1 + menu.Items.Count) % menu.Items.Count;
      menu.Expiry   = DateTime.Now.AddSeconds(MenuSeconds);
    } else if (pressed.HasFlag(PlayerButtons.Back)) {
      menu.Selected = (menu.Selected + 1) % menu.Items.Count;
      menu.Expiry   = DateTime.Now.AddSeconds(MenuSeconds);
    } else if (pressed.HasFlag(PlayerButtons.Use)) {
      var index = menu.Selected;
      open.Remove(player.Slot);
      buyIndex(player, index);
    }
  }

  private void refresh() {
    if (open.Count == 0) return;
    var now = DateTime.Now;
    foreach (var slot in open.Keys.ToList()) {
      var menu       = open[slot];
      var controller = Utilities.GetPlayerFromSlot(slot);
      if (now > menu.Expiry
        || controller is not { IsValid: true, PawnIsAlive: true }) {
        open.Remove(slot);
        continue;
      }

      if (converter.GetPlayer(controller) is IOnlinePlayer apiPlayer)
        controller.PrintToCenterHtml(buildHtml(apiPlayer, menu));
    }
  }

  private string buildHtml(IOnlinePlayer apiPlayer, Menu menu) {
    var sb = new StringBuilder();
    sb.Append(
      $"<font color='#4ea1ff'>=(eGO)= SHOP</font>  <font color='#cccccc'>{menu.Balance} credits</font><br>");

    for (var i = 0; i < menu.Items.Count; i++) {
      var item     = menu.Items[i];
      var selected = i == menu.Selected;
      var canBuy   = item.CanPurchase(apiPlayer) == PurchaseResult.SUCCESS
        && item.Config.Price <= menu.Balance;
      var color  = selected ? "#ffd700" : canBuy ? "#7CFC00" : "#888888";
      var cursor = selected ? "&#9654; " : "&nbsp;&nbsp;&nbsp;";
      sb.Append(
        $"<font color='{color}'>{cursor}{item.Name} — {item.Config.Price}</font><br>");
    }

    sb.Append(
      "<font color='#aaaaaa'>W / S move &nbsp;•&nbsp; E buy &nbsp;•&nbsp; ping again to close</font>");
    return sb.ToString();
  }

  private void onButton(CCSPlayerController? player, int index) {
    // css_N quick-buy: css_1 -> item 1, etc. (item index-1).
    if (player != null) buyIndex(player, index - 1);
  }

  private void buyIndex(CCSPlayerController player, int index) {
    if (index < 0) return;
    if (converter.GetPlayer(player) is not IOnlinePlayer apiPlayer) return;

    var lastUpdated = itemSorter.GetLastUpdate(apiPlayer);
    if (lastUpdated == null
      || DateTime.Now - lastUpdated > TimeSpan.FromSeconds(20))
      return;

    var cmdInfo = new CS2CommandInfo(provider, apiPlayer, 0, "css_shop", "buy",
      index.ToString()) { CallingContext = CommandCallingContext.Chat };
    provider.GetRequiredService<ICommandManager>().ProcessCommand(cmdInfo);
    itemSorter.InvalidateOrder(apiPlayer);
  }
}
