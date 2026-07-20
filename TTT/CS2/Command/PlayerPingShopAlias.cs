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
//   W / S (or arrows) -> move the highlight,  E (use) -> buy,  R / ping -> close.
// The player is frozen in place while the menu is open, so W/S drive the menu
// without walking them around. css_0..css_9 remain an optional quick-buy.
//
// The menu is drawn with PrintToCenterHtml (the survival-respawn HUD panel).
// A world-text entity would allow an instant, boxless close, but point_worldtext
// parented to a player's own pawn is not rendered for that player (verified on
// dev 2026-07-20), so it cannot back an owner-facing menu. The trade-off here is
// that the panel lingers a couple of seconds after close before it times out.
public class PlayerPingShopAlias(IServiceProvider provider) : IPluginModule {
  private readonly IPlayerConverter<CCSPlayerController> converter =
    provider.GetRequiredService<IPlayerConverter<CCSPlayerController>>();

  private readonly IItemSorter itemSorter =
    provider.GetRequiredService<IItemSorter>();

  private readonly IShop shop = provider.GetRequiredService<IShop>();

  private sealed class Menu {
    public required List<IShopItem> Items;
    public required IOnlinePlayer   Player;
    public          int             Balance;
    public          int             Selected;
    public          DateTime        Expiry;
  }

  // slot -> open menu. Re-rendered on a timer; navigated via the buttons hook.
  private readonly Dictionary<int, Menu> open        = new();
  private const    int                   MenuSeconds = 15;
  private const    float                 RefreshSeconds = 0.25f;

  public void Dispose() { }
  public void Start() { }

  public void Start(BasePlugin? plugin) {
    plugin?.AddCommandListener("player_ping", onPlayerPing, HookMode.Post);
    plugin?.RegisterListener<
      CounterStrikeSharp.API.Core.Listeners.OnPlayerButtonsChanged>(onButtons);

    // Center HTML only shows for a moment, so re-send it while the menu is open.
    plugin?.AddTimer(RefreshSeconds, refresh, TimerFlags.REPEAT);

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
    if (closeMenu(slot)) return HookResult.Continue; // re-ping closes it

    if (converter.GetPlayer(player) is not IOnlinePlayer apiPlayer)
      return HookResult.Continue;

    // Snapshot + lock the sorted order, then open the menu once we have balance.
    var items = itemSorter.GetSortedItems(apiPlayer, true);
    Task.Run(async () => {
      var balance = await shop.Load(apiPlayer);
      Server.NextWorldUpdate(() => {
        var controller = Utilities.GetPlayerFromSlot(slot);
        if (controller is not { IsValid: true, PawnIsAlive: true }) return;

        open[slot] = new Menu {
          Items  = items, Player = apiPlayer, Balance = balance, Selected = 0,
          Expiry = DateTime.Now.AddSeconds(MenuSeconds)
        };
        setFrozen(controller, true); // hold still so W/S drives the menu
      });
    });

    return HookResult.Continue;
  }

  // W / S (or the arrow turn-keys) move the highlight; E (use) buys the
  // highlighted item; R (reload) closes. Only active while the menu is open.
  private void onButtons(CCSPlayerController player, PlayerButtons pressed,
    PlayerButtons released) {
    if (!player.IsValid || !open.TryGetValue(player.Slot, out var menu)) return;
    if (menu.Items.Count == 0) return;

    if (pressed.HasFlag(PlayerButtons.Forward)
      || pressed.HasFlag(PlayerButtons.Left)) {
      menu.Selected = (menu.Selected - 1 + menu.Items.Count) % menu.Items.Count;
      menu.Expiry   = DateTime.Now.AddSeconds(MenuSeconds);
    } else if (pressed.HasFlag(PlayerButtons.Back)
      || pressed.HasFlag(PlayerButtons.Right)) {
      menu.Selected = (menu.Selected + 1) % menu.Items.Count;
      menu.Expiry   = DateTime.Now.AddSeconds(MenuSeconds);
    } else if (pressed.HasFlag(PlayerButtons.Reload)) {
      // Cooldown-immune close: a re-ping can be swallowed by CS2's ping cooldown.
      closeMenu(player.Slot);
    } else if (pressed.HasFlag(PlayerButtons.Use)) {
      // Buy from the menu's own snapshot so the item purchased is exactly the
      // one highlighted, regardless of what the sorter cache has done since.
      var item = menu.Items[menu.Selected];
      closeMenu(player.Slot);
      buyItem(player, item);
    }
  }

  // Close by ceasing to re-send (the last frame times out on its own) and
  // unfreeze the player. We deliberately do NOT push a blank/empty frame — an
  // empty message wedges the survival-respawn panel on screen permanently.
  private bool closeMenu(int slot) {
    if (!open.Remove(slot)) return false;
    setFrozen(Utilities.GetPlayerFromSlot(slot), false);
    return true;
  }

  // Freeze/unfreeze the pawn's movement. Every close path funnels through
  // closeMenu, and the 15s expiry is a backstop, so the player can never be
  // left frozen. Looking around still works — only translation is blocked.
  private static void setFrozen(CCSPlayerController? controller, bool frozen) {
    var pawn = controller?.PlayerPawn.Value;
    if (pawn is not { IsValid: true }) return;
    pawn.MoveType = frozen ? MoveType_t.MOVETYPE_NONE : MoveType_t.MOVETYPE_WALK;
    Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
  }

  private void refresh() {
    if (open.Count == 0) return;
    var now = DateTime.Now;
    foreach (var slot in open.Keys.ToList()) {
      var menu       = open[slot];
      var controller = Utilities.GetPlayerFromSlot(slot);
      if (now > menu.Expiry
        || controller is not { IsValid: true, PawnIsAlive: true }) {
        closeMenu(slot);
        continue;
      }

      controller.PrintToCenterHtml(buildHtml(menu));
    }
  }

  private string buildHtml(Menu menu) {
    var sb = new StringBuilder();
    sb.Append(
      $"<font color='#4ea1ff'>=(eGO)= SHOP</font>  <font color='#cccccc'>{menu.Balance} credits</font><br>");

    for (var i = 0; i < menu.Items.Count; i++) {
      var item     = menu.Items[i];
      var selected = i == menu.Selected;
      var canBuy   = item.CanPurchase(menu.Player) == PurchaseResult.SUCCESS
        && item.Config.Price <= menu.Balance;
      var color  = selected ? "#ffd700" : canBuy ? "#7CFC00" : "#888888";
      var cursor = selected ? "&#9654; " : "&nbsp;&nbsp;&nbsp;";
      sb.Append(
        $"<font color='{color}'>{cursor}{item.Name} — {item.Config.Price}</font><br>");
    }

    sb.Append(
      "<font color='#aaaaaa'>W / S move &nbsp;•&nbsp; E buy &nbsp;•&nbsp; R to close</font>");
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

  // Menu-driven purchase: buy by exact name (order-independent, so no
  // staleness window) and let BuyCommand handle round/health/funds checks.
  private void buyItem(CCSPlayerController player, IShopItem item) {
    if (converter.GetPlayer(player) is not IOnlinePlayer apiPlayer) return;

    var cmdInfo = new CS2CommandInfo(provider, apiPlayer, 0, "css_shop", "buy",
      item.Name) { CallingContext = CommandCallingContext.Chat };
    provider.GetRequiredService<ICommandManager>().ProcessCommand(cmdInfo);
    itemSorter.InvalidateOrder(apiPlayer);
  }
}
