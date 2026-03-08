using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Cvars.Validators;
using ShopAPI;
using ShopAPI.Configs.Detective;
using TTT.API;
using TTT.API.Storage;

namespace TTT.CS2.Configs.ShopItems;

public class CS2StickersConfig : IStorage<StickersConfig>, IPluginModule {
  public static readonly FakeConVar<int> CV_PRICE = new(
    "css_ttt_shop_stickers_price", "Price of the Stickers item (Detective)", 45,
    ConVarFlags.FCVAR_NONE, new RangeValidator<int>(0, 10000));

  public static readonly FakeConVar<int> CV_MAX_PURCHASES = new(
    "css_ttt_shop_stickers_max_purchases",
    "Maximum number of times a player can purchase the Stickers Item per round",
    0, ConVarFlags.FCVAR_NONE, new RangeValidator<int>(1, 100));

  public static readonly FakeConVar<int> CV_LIMIT_MODE =
    new("css_ttt_shop_stickers_limit_mode",
      "0 = Unlimited, 1 = Per Player, 2 = Per Team", 0, ConVarFlags.FCVAR_NONE,
      new RangeValidator<int>(0, 2));

  public void Dispose() { }

  public void Start() { }

  public void Start(BasePlugin? plugin) {
    ArgumentNullException.ThrowIfNull(plugin, nameof(plugin));
    plugin.RegisterFakeConVars(this);
  }

  public Task<StickersConfig?> Load() {
    var cfg = new StickersConfig {
      Price     = CV_PRICE.Value,
      Limit     = CV_MAX_PURCHASES.Value,
      LimitMode = (ItemLimitMode)CV_LIMIT_MODE.Value
    };

    return Task.FromResult<StickersConfig?>(cfg);
  }
}