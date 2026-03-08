using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Cvars.Validators;
using ShopAPI;
using ShopAPI.Configs.Traitor;
using TTT.API;
using TTT.API.Storage;

namespace TTT.CS2.Configs.ShopItems;

public class CS2OneHitKnifeConfig : IStorage<OneHitKnifeConfig>, IPluginModule {
  public static readonly FakeConVar<int> CV_PRICE = new(
    "css_ttt_shop_onehitknife_price",
    "Price of the One-Hit Knife item (Traitor)", 80, ConVarFlags.FCVAR_NONE,
    new RangeValidator<int>(0, 10000));

  public static readonly FakeConVar<bool> CV_FRIENDLY_FIRE = new(
    "css_ttt_shop_onehitknife_friendly_fire",
    "Whether the One-Hit Knife can damage teammates");

  public static readonly FakeConVar<int> CV_MAX_PURCHASES = new(
    "css_ttt_shop_onehitknife_max_purchases",
    "Maximum number of times a player can purchase the One-Hit Knife per round",
    0, ConVarFlags.FCVAR_NONE, new RangeValidator<int>(1, 100));

  public static readonly FakeConVar<int> CV_LIMIT_MODE =
    new("css_ttt_shop_onehitknife_limit_mode",
      "0 = Unlimited, 1 = Per Player, 2 = Per Team", 0, ConVarFlags.FCVAR_NONE,
      new RangeValidator<int>(0, 2));

  public void Dispose() { }

  public void Start() { }

  public void Start(BasePlugin? plugin) {
    ArgumentNullException.ThrowIfNull(plugin, nameof(plugin));
    plugin.RegisterFakeConVars(this);
  }

  public Task<OneHitKnifeConfig?> Load() {
    var cfg = new OneHitKnifeConfig {
      Price        = CV_PRICE.Value,
      FriendlyFire = CV_FRIENDLY_FIRE.Value,
      Limit        = CV_MAX_PURCHASES.Value,
      LimitMode    = (ItemLimitMode)CV_LIMIT_MODE.Value
    };

    return Task.FromResult<OneHitKnifeConfig?>(cfg);
  }
}