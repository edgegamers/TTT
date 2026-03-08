using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Cvars.Validators;
using ShopAPI;
using ShopAPI.Configs;
using TTT.API;
using TTT.API.Storage;
using TTT.CS2.Validators;

namespace TTT.CS2.Configs.ShopItems;

public class CS2TaserConfig : IStorage<TaserConfig>, IPluginModule {
  public static readonly FakeConVar<int> CV_PRICE = new(
    "css_ttt_shop_taser_price", "Price of the Taser item", 110,
    ConVarFlags.FCVAR_NONE, new RangeValidator<int>(0, 10000));

  public static readonly FakeConVar<string> CV_WEAPON = new(
    "css_ttt_shop_taser_weapon", "Weapon entity name used for the Taser",
    "weapon_taser", ConVarFlags.FCVAR_NONE,
    new ItemValidator(allowMultiple: false));

  public static readonly FakeConVar<int> CV_MAX_PURCHASES = new(
    "css_ttt_shop_taser_max_purchases",
    "Maximum number of times a player can purchase the Taser per round", 0,
    ConVarFlags.FCVAR_NONE, new RangeValidator<int>(1, 100));

  public static readonly FakeConVar<int> CV_LIMIT_MODE =
    new("css_ttt_shop_taser_limit_mode",
      "0 = Unlimited, 1 = Per Player, 2 = Per Team", 0, ConVarFlags.FCVAR_NONE,
      new RangeValidator<int>(0, 2));

  public void Dispose() { }

  public void Start() { }

  public void Start(BasePlugin? plugin) {
    ArgumentNullException.ThrowIfNull(plugin, nameof(plugin));
    plugin.RegisterFakeConVars(this);
  }

  public Task<TaserConfig?> Load() {
    var cfg = new TaserConfig {
      Price     = CV_PRICE.Value,
      Weapon    = CV_WEAPON.Value,
      Limit     = CV_MAX_PURCHASES.Value,
      LimitMode = (ItemLimitMode)CV_LIMIT_MODE.Value
    };

    return Task.FromResult<TaserConfig?>(cfg);
  }
}