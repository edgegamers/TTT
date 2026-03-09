using Microsoft.Extensions.DependencyInjection;
using ShopAPI;
using ShopAPI.Configs;
using TTT.API;
using TTT.API.Extensions;
using TTT.API.Player;
using TTT.API.Storage;
using TTT.Game.Roles;

namespace TTT.Shop.Items.Detective.OneShotDeagle;

public static class DeagleServiceCollection {
  public static void AddDeagleServices(this IServiceCollection collection) {
    collection.AddModBehavior<OneShotDeagleItem>();
    collection.AddModBehavior<OneShotDeagleDamageListener>();
  }
}

public class OneShotDeagleItem(IServiceProvider provider)
  : RoleRestrictedItem<DetectiveRole>(provider), IWeapon {
  private OneShotDeagleConfig deagleConfigStorage
    => Provider.GetService<IStorage<OneShotDeagleConfig>>()
    ?.Load()
     .GetAwaiter()
     .GetResult() ?? new OneShotDeagleConfig();

  public override string Name => Locale[OneShotDeagleMsgs.SHOP_ITEM_DEAGLE];

  public override string Description
    => Locale[OneShotDeagleMsgs.SHOP_ITEM_DEAGLE_DESC];

  public override ShopItemConfig Config => deagleConfigStorage;

  public string WeaponId => deagleConfigStorage.Weapon;

  public int? ReserveAmmo { get; init; } = 0;
  public int? CurrentAmmo { get; init; } = 1;

  public override void OnPurchase(IOnlinePlayer player) {
    Task.Run(async () => {
      await Inventory.RemoveWeaponInSlot(player,
        deagleConfigStorage.WeaponSlot);
      await Inventory.GiveWeapon(player, this);
    });
  }
}