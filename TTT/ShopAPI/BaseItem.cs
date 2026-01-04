using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using ShopAPI.Configs;
using TTT.API.Game;
using TTT.API.Messages;
using TTT.API.Player;
using TTT.API.Role;
using TTT.Game.Events.Game;
using TTT.API.Events;
using TTT.Game.Roles;
using TTT.Locale;

namespace ShopAPI;

public abstract class BaseItem(IServiceProvider provider) : IShopItem {
  protected readonly IPlayerFinder Finder =
    provider.GetRequiredService<IPlayerFinder>();

  protected readonly IGameManager Games =
    provider.GetRequiredService<IGameManager>();

  protected readonly IInventoryManager Inventory =
    provider.GetRequiredService<IInventoryManager>();

  protected readonly IMsgLocalizer Locale =
    provider.GetRequiredService<IMsgLocalizer>();

  protected readonly IMessenger Messenger =
    provider.GetRequiredService<IMessenger>();

  protected readonly IServiceProvider Provider = provider;

  protected readonly IRoleAssigner Roles =
    provider.GetRequiredService<IRoleAssigner>();

  protected readonly IShop Shop = provider.GetRequiredService<IShop>();

  protected readonly Dictionary<IPlayer, int> PlayerPurchaseCount = new();
  protected readonly Dictionary<IRole, int> TeamPurchaseCount = new();


  public virtual void Dispose() { }
  public abstract string Name { get; }
  public abstract string Description { get; }
  public abstract ShopItemConfig Config { get; }

  public virtual void OnPurchase(IOnlinePlayer player) {
    trackPurchase(player);
  }

  public virtual PurchaseResult CanPurchase(IOnlinePlayer player) {
    switch (Config.LimitMode) {
      case ItemLimitMode.PER_PLAYER: {
        var currentCount = PlayerPurchaseCount.GetValueOrDefault(player, 0);
        if (currentCount >= Config.Limit)
          return PurchaseResult.LIMIT_REACHED_PLAYER;
        break;
      }
      
      case ItemLimitMode.PER_TEAM: {
        var roles = Roles.GetRoles(player).Where(r => r is BaseRole);
        if (roles.Select(role => TeamPurchaseCount.GetValueOrDefault(role, 0))
         .Any(currentCount => currentCount >= Config.Limit)) {
          return PurchaseResult.LIMIT_REACHED_TEAM;
        }
        break;
      }
      
      case ItemLimitMode.OFF:
      default:
        break;
    }

    return PurchaseResult.SUCCESS;
  }

  private void trackPurchase(IOnlinePlayer player) {
    switch (Config.LimitMode) {
      case ItemLimitMode.PER_PLAYER:
        PlayerPurchaseCount[player] =
          PlayerPurchaseCount.GetValueOrDefault(player, 0) + 1;
        break;
      case ItemLimitMode.PER_TEAM: {
        var roles = Roles.GetRoles(player).Where(r => r is BaseRole);
        foreach (var role in roles) {
          TeamPurchaseCount[role] =
            TeamPurchaseCount.GetValueOrDefault(role, 0) + 1;
        }

        break;
      }
      case ItemLimitMode.OFF:
      default:
        break;
    }
  }

  [UsedImplicitly]
  [EventHandler]
  public void OnGameState(GameStateUpdateEvent ev) {
    if (ev.NewState != State.FINISHED) return;
    PlayerPurchaseCount.Clear();
    TeamPurchaseCount.Clear();
  }

  public virtual void Start() { Shop.RegisterItem(this); }
}
