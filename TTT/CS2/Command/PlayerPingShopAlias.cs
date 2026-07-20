using System.Drawing;
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
using TTT.CS2.Extensions;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace TTT.CS2.Command;

// Ping opens an on-screen, navigable shop menu — no key binds required:
//   Left / Right arrow -> move the highlight,  E (use) -> buy,  ping/R -> close.
// The menu is drawn as a world-text entity parented to the player (not the
// survival-respawn HUD panel), so closing kills the entity for an instant,
// boxless dismiss instead of the HUD panel's multi-second lingering box.
// css_0..css_9 remain as an optional quick-buy for anyone who binds keys.
public class PlayerPingShopAlias(IServiceProvider provider) : IPluginModule {
  private readonly IPlayerConverter<CCSPlayerController> converter =
    provider.GetRequiredService<IPlayerConverter<CCSPlayerController>>();

  private readonly IItemSorter itemSorter =
    provider.GetRequiredService<IItemSorter>();

  private readonly IShop shop = provider.GetRequiredService<IShop>();

  private sealed class Menu {
    public required List<IShopItem>  Items;
    public required IOnlinePlayer    Player;
    public          int              Balance;
    public          int              Selected;
    public          DateTime         Expiry;
    public          CPointWorldText? Text;
  }

  // slot -> open menu. The world-text entity persists on its own; the timer
  // only enforces expiry / cleanup, and navigation re-renders on demand.
  private readonly Dictionary<int, Menu> open        = new();
  private const    int                   MenuSeconds = 15;
  private const    float                 TickSeconds = 0.5f;

  public void Dispose() { }
  public void Start() { }

  public void Start(BasePlugin? plugin) {
    plugin?.AddCommandListener("player_ping", onPlayerPing, HookMode.Post);
    plugin?.RegisterListener<
      CounterStrikeSharp.API.Core.Listeners.OnPlayerButtonsChanged>(onButtons);

    // Enforce the 15s timeout and clean up if the player dies / the entity is
    // killed elsewhere (e.g. round end kills all world-text).
    plugin?.AddTimer(TickSeconds, tick, TimerFlags.REPEAT);

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
        if (controller is not { IsValid: true }) return;

        var menu = new Menu {
          Items  = items, Player = apiPlayer, Balance = balance, Selected = 0,
          Expiry = DateTime.Now.AddSeconds(MenuSeconds)
        };
        open[slot] = menu;
        render(controller, menu);
      });
    });

    return HookResult.Continue;
  }

  // Left / Right arrows move the highlight; E (use) buys the highlighted item;
  // R (reload) closes. Only active while the player has the menu open, so
  // normal input is unaffected otherwise.
  private void onButtons(CCSPlayerController player, PlayerButtons pressed,
    PlayerButtons released) {
    if (!player.IsValid || !open.TryGetValue(player.Slot, out var menu)) return;
    if (menu.Items.Count == 0) return;

    if (pressed.HasFlag(PlayerButtons.Left)) {
      menu.Selected = (menu.Selected - 1 + menu.Items.Count) % menu.Items.Count;
      menu.Expiry   = DateTime.Now.AddSeconds(MenuSeconds);
      render(player, menu);
    } else if (pressed.HasFlag(PlayerButtons.Right)) {
      menu.Selected = (menu.Selected + 1) % menu.Items.Count;
      menu.Expiry   = DateTime.Now.AddSeconds(MenuSeconds);
      render(player, menu);
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

  // Instant, boxless close: killing the world-text entity removes it at once.
  private bool closeMenu(int slot) {
    if (!open.Remove(slot, out var menu)) return false;
    killText(menu);
    return true;
  }

  private void tick() {
    if (open.Count == 0) return;
    var now = DateTime.Now;
    foreach (var slot in open.Keys.ToList()) {
      var menu       = open[slot];
      var controller = Utilities.GetPlayerFromSlot(slot);
      if (now > menu.Expiry
        || controller is not { IsValid: true, PawnIsAlive: true }
        || menu.Text is not { IsValid: true })
        closeMenu(slot);
    }
  }

  // How far in front of the eyes the panel floats, and how large the text is.
  // worldUnitsPerPx is deliberately small — the shared TextSpawner default (0.5)
  // is sized for a single head-letter and renders a whole menu block gigantic.
  private const float MenuDistance    = 50f;
  private const float MenuFontSize    = 50f;
  private const float MenuUnitsPerPx  = 0.1f; // DEBUG: deliberately large

  // Kill the current entity (if any) and spawn a fresh one with the current
  // selection. Re-render on each nav keeps the entity path simple.
  private void render(CCSPlayerController controller, Menu menu) {
    killText(menu);

    var msg = buildText(menu);
    if (msg.Length > 500) msg = msg[..500]; // hard cap: never exceed the buffer

    try {
      menu.Text = spawnMenuText(controller, msg);
      Server.PrintToConsole(
        $"[shop] render len={msg.Length} valid={menu.Text?.IsValid} pos={menu.Text?.AbsOrigin}");
    } catch (Exception e) {
      menu.Text = null;
      Server.PrintToConsole($"[shop] render THREW: {e.Message}");
    }
  }

  // Spawn a screen-facing world-text panel in front of the player's eyes,
  // parented so it follows them. Billboarded (AROUND_UP) so it always faces the
  // viewer, left/top justified, and scaled down to a readable HUD-like size.
  private static CPointWorldText? spawnMenuText(CCSPlayerController controller,
    string msg) {
    var pawn = controller.PlayerPawn.Value;
    if (pawn is not { IsValid: true }) return null;
    var origin = controller.AbsOrigin;
    var angles = pawn.AbsRotation;
    if (origin == null || angles == null) return null;

    var ent = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
    if (ent is not { IsValid: true }) return null;

    ent.MessageText       = msg;
    ent.Enabled           = true;
    ent.FontSize          = MenuFontSize;
    ent.Color             = Color.White;
    ent.Fullbright        = true;
    ent.WorldUnitsPerPx   = MenuUnitsPerPx;
    ent.FontName          = "Arial";
    ent.JustifyHorizontal =
      PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_LEFT;
    ent.JustifyVertical   =
      PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_TOP;
    // No billboard for now — mimic the proven-visible role-icon hats exactly.
    ent.ReorientMode      =
      PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_NONE;

    var forward = angles.Clone()!.ToForward();
    var eyeZ    = pawn.ViewOffset.Z > 1 ? pawn.ViewOffset.Z : 64f;
    var eyes    = new Vector(origin.X, origin.Y, origin.Z + eyeZ);
    var pos     = eyes + forward * MenuDistance;
    // Upright text facing back toward the player (roll +90 like the hats).
    var rot = new QAngle(angles.X, angles.Y + 180, angles.Z + 90);

    Server.PrintToConsole(
      $"[shop] spawn eyeZ={eyeZ:F1} eyes={eyes} fwd={forward} pos={pos} yaw={angles.Y:F0}");

    ent.Teleport(pos, rot);
    ent.DispatchSpawn();
    ent.AcceptInput("SetParent", pawn, null, "!activator");
    return ent;
  }

  private static void killText(Menu menu) {
    if (menu.Text is { IsValid: true }) menu.Text.AcceptInput("Kill");
    menu.Text = null;
  }

  // CPointWorldText.MessageText is capped at 512 chars, and a full item list
  // blows past that — so show a scrolling window of items around the selection.
  private const int VisibleItems = 8;

  private string buildText(Menu menu) {
    var sb    = new StringBuilder();
    var count = menu.Items.Count;
    sb.Append($"=(eGO)= SHOP    {menu.Balance} credits\n");

    var start = 0;
    if (count > VisibleItems) {
      start = Math.Clamp(menu.Selected - VisibleItems / 2, 0,
        count - VisibleItems);
    }

    var end = Math.Min(count, start + VisibleItems);
    if (start > 0) sb.Append("      ▲\n");
    for (var i = start; i < end; i++) {
      var item   = menu.Items[i];
      var canBuy = item.CanPurchase(menu.Player) == PurchaseResult.SUCCESS
        && item.Config.Price <= menu.Balance;
      var cursor = i == menu.Selected ? "► " : "   ";
      var mark   = canBuy ? "" : "  (x)";
      sb.Append($"{cursor}{item.Name} - {item.Config.Price}{mark}\n");
    }

    if (end < count) sb.Append("      ▼\n");

    sb.Append("← / →  move     E  buy     R  close");
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
