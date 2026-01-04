using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Cvars.Validators;
using ShopAPI;
using ShopAPI.Configs;
using TTT.API;
using TTT.API.Storage;

namespace TTT.CS2.Configs.ShopItems;

public class CS2CamoConfig : IStorage<CamoConfig>, IPluginModule {
  public static readonly FakeConVar<int> CV_PRICE = new(
    "css_ttt_shop_camo_price", "Price of the Camo item", 65,
    ConVarFlags.FCVAR_NONE, new RangeValidator<int>(0, 10000));

  public static readonly FakeConVar<float> CV_CAMO_VISIBILITY = new(
    "css_ttt_shop_camo_visibility",
    "Player visibility multiplier while camouflaged (0 = invisible, 1 = fully visible)",
    0.5f, ConVarFlags.FCVAR_NONE, new RangeValidator<float>(0f, 1f));

  public static readonly FakeConVar<int> CV_MAX_PURCHASES = new(
    "css_ttt_shop_camo_max_purchases",
    "Maximum number of times a player can purchase the Camo item per round", 0,
    ConVarFlags.FCVAR_NONE, new RangeValidator<int>(1, 100));

  public static readonly FakeConVar<int> CV_LIMIT_MODE =
    new("css_ttt_shop_camo_limit_mode",
      "0 = Unlimited, 1 = Per Player, 2 = Per Team", 0, ConVarFlags.FCVAR_NONE,
      new RangeValidator<int>(0, 2));

  public void Dispose() { }

  public void Start() { }

  public void Start(BasePlugin? plugin) {
    ArgumentNullException.ThrowIfNull(plugin, nameof(plugin));
    plugin.RegisterFakeConVars(this);
  }

  public Task<CamoConfig?> Load() {
    var cfg = new CamoConfig {
      Price          = CV_PRICE.Value,
      CamoVisibility = CV_CAMO_VISIBILITY.Value,
      Limit          = CV_MAX_PURCHASES.Value,
      LimitMode      = (ItemLimitMode)CV_LIMIT_MODE.Value
    };

    return Task.FromResult<CamoConfig?>(cfg);
  }
}