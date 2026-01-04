using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Cvars.Validators;
using ShopAPI;
using TTT.API;
using TTT.API.Storage;

namespace TTT.CS2.Configs.ShopItems;

public class CS2HealthshotConfig : IStorage<HealthshotConfig>, IPluginModule {
  public static readonly FakeConVar<int> CV_PRICE = new(
    "css_ttt_shop_healthshot_price", "Price of the Healthshot item", 40,
    ConVarFlags.FCVAR_NONE, new RangeValidator<int>(0, 10000));

  public static readonly FakeConVar<int> CV_MAX_PURCHASES = new(
    "css_ttt_shop_healthshot_max_purchases",
    "Maximum number of times a player can purchase the Healthshot per round", 2,
    ConVarFlags.FCVAR_NONE, new RangeValidator<int>(1, 100));

  public static readonly FakeConVar<string> CV_WEAPON = new(
    "css_ttt_shop_healthshot_weapon", "Weapon entity name for the Healthshot",
    "weapon_healthshot");
  
  public static readonly FakeConVar<int> CV_LIMIT_MODE =
    new("css_ttt_shop_healthshot_limit_mode",
      "0 = Unlimited, 1 = Per Player, 2 = Per Team", 1, ConVarFlags.FCVAR_NONE,
      new RangeValidator<int>(0, 2));

  public void Dispose() { }

  public void Start() { }

  public void Start(BasePlugin? plugin) {
    ArgumentNullException.ThrowIfNull(plugin, nameof(plugin));
    plugin.RegisterFakeConVars(this);
  }

  public Task<HealthshotConfig?> Load() {
    var cfg = new HealthshotConfig {
      Price        = CV_PRICE.Value,
      Limit        = CV_MAX_PURCHASES.Value,
      Weapon       = CV_WEAPON.Value,
      LimitMode    = (ItemLimitMode)CV_LIMIT_MODE.Value
    };

    return Task.FromResult<HealthshotConfig?>(cfg);
  }
}