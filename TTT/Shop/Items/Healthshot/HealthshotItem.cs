using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using ShopAPI;
using ShopAPI.Configs;
using TTT.API.Events;
using TTT.API.Extensions;
using TTT.API.Game;
using TTT.API.Player;
using TTT.API.Storage;
using TTT.Game.Events.Game;
using TTT.Game.Roles;

namespace TTT.Shop.Items.Healthshot;

public static class HealthshotServiceCollection {
  public static void AddHealthshotServices(this IServiceCollection services) {
    services.AddModBehavior<HealthshotItem>();
  }
}

public class HealthshotItem(IServiceProvider provider)
  : BaseItem(provider), IListener {
  private HealthshotConfig config
    => Provider.GetService<IStorage<HealthshotConfig>>()
    ?.Load()
     .GetAwaiter()
     .GetResult() ?? new HealthshotConfig();

  public override string Name => Locale[HealthshotMsgs.SHOP_ITEM_HEALTHSHOT];

  public override string Description
    => Locale[HealthshotMsgs.SHOP_ITEM_HEALTHSHOT_DESC];

  public override ShopItemConfig Config => config;

  public override void OnPurchase(IOnlinePlayer player) {
    Inventory.GiveWeapon(player, new BaseWeapon(config.Weapon));

    base.OnPurchase(player);
  }
}