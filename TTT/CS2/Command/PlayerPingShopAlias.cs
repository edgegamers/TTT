using System.Drawing;
using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.DependencyInjection;
using ShopAPI;
using TTT.API;
using TTT.API.Command;
using TTT.API.Player;
using TTT.CS2.Hats;

namespace TTT.CS2.Command;

public class PlayerPingShopAlias(IServiceProvider provider) : IPluginModule {
  private readonly IPlayerConverter<CCSPlayerController> converter =
    provider.GetRequiredService<IPlayerConverter<CCSPlayerController>>();

  private readonly IItemSorter itemSorter =
    provider.GetRequiredService<IItemSorter>();

  private readonly ITextSpawner? textSpawner =
    provider.GetService<ITextSpawner>();

  private readonly IShop shop = provider.GetRequiredService<IShop>();

  // On-screen shop menu entity per player slot (null = no menu open).
  private readonly CPointWorldText?[] menus = new CPointWorldText?[64];

  public void Dispose() { }
  public void Start() { }

  public void Start(BasePlugin? plugin) {
    plugin?.AddCommandListener("player_ping", onPlayerPing, HookMode.Post);
    plugin
    ?.RegisterListener<CounterStrikeSharp.API.Core.Listeners.CheckTransmit>(
        onTransmit);

    for (var i = 0; i < 10; i++) {
      var index = i; // Capture variable
      plugin?.AddCommand($"css_{index}", "",
        (player, _) => { onButton(player, index); });
    }
  }

  // Ping now opens an on-screen list (was a chat dump). Buying is unchanged:
  // the css_0..css_9 quick-buy commands below stay armed for 20s.
  private HookResult onPlayerPing(CCSPlayerController? player,
    CommandInfo commandInfo) {
    if (player == null || !player.IsValid) return HookResult.Continue;
    if (converter.GetPlayer(player) is not IOnlinePlayer apiPlayer)
      return HookResult.Continue;

    // Snapshot + arm the css_N order, then render once we have the balance.
    var items = itemSorter.GetSortedItems(apiPlayer, true);
    Task.Run(async () => {
      var balance = await shop.Load(apiPlayer);
      Server.NextWorldUpdate(() => renderMenu(player, apiPlayer, items, balance));
    });

    return HookResult.Continue;
  }

  private void renderMenu(CCSPlayerController player, IOnlinePlayer apiPlayer,
    List<IShopItem> items, int balance) {
    if (textSpawner == null || !player.IsValid) return;

    var text = buildMenuText(apiPlayer, items, balance);
    removeMenu(player.Slot);

    try {
      var ent = textSpawner.CreateTextScreen(new TextSetting {
        msg = text, color = Color.White, fontSize = 26, worldUnitsPerPx = 0.013f
      }, player).FirstOrDefault();
      if (ent == null) return;
      menus[player.Slot] = ent;

      // Auto-close after ~12s so it doesn't linger in front of the player.
      var captured = ent;
      Server.RunOnTick(Server.TickCount + 64 * 12, () => {
        if (menus[player.Slot] == captured) removeMenu(player.Slot);
      });
    } catch {
      // entity creation can throw if the pawn isn't ready; ignore.
    }
  }

  private string buildMenuText(IOnlinePlayer apiPlayer, List<IShopItem> items,
    int balance) {
    var lines = new List<string> { $"=( eGO )= SHOP    {balance} credits", "" };

    for (var i = 0; i < items.Count; i++) {
      var item   = items[i];
      var canBuy = item.CanPurchase(apiPlayer) == PurchaseResult.SUCCESS
        && item.Config.Price <= balance;
      var num  = (i + 1).ToString().PadLeft(2);
      var name = item.Name.Length > 22
        ? item.Name[..22]
        : item.Name.PadRight(22);
      var price = item.Config.Price.ToString().PadLeft(4);
      lines.Add(canBuy ? $"{num}  {name} {price}" : $"{num}  {name} {price}  x");
    }

    lines.Add("");
    lines.Add("press 1-9 (bound to css_N) to buy");
    return string.Join("\n", lines);
  }

  private void onTransmit(CCheckTransmitInfoList infoList) {
    foreach (var (info, player) in infoList) {
      if (player == null || !player.IsValid) continue;
      // A player should only see their own on-screen menu.
      for (var i = 0; i < menus.Length; i++) {
        if (i == player.Slot) continue;
        var ent = menus[i];
        if (ent != null && ent.IsValid) info.TransmitEntities.Remove(ent);
      }
    }
  }

  private void removeMenu(int slot) {
    var ent = menus[slot];
    if (ent != null && ent.IsValid) ent.AcceptInput("Kill");
    menus[slot] = null;
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
