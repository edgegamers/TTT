using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Cvars.Validators;
using ShopAPI;
using ShopAPI.Configs.Traitor;
using TTT.API;
using TTT.API.Storage;
using TTT.CS2.Validators;

namespace TTT.CS2.Configs.ShopItems;

public class CS2C4Config : IStorage<C4Config>, IPluginModule {
  public static readonly FakeConVar<int> CV_PRICE = new("css_ttt_shop_c4_price",
    "Price of the C4 item", 130, ConVarFlags.FCVAR_NONE,
    new RangeValidator<int>(0, 10000));

  public static readonly FakeConVar<string> CV_WEAPON = new(
    "css_ttt_shop_c4_weapon", "Weapon entity name used for the C4", "weapon_c4",
    ConVarFlags.FCVAR_NONE, new ItemValidator(allowMultiple: false));

  public static readonly FakeConVar<int> CV_MAX = new(
    "css_ttt_shop_c4_max",
    "Maximum number of C4 that can be active at once", 1,
    ConVarFlags.FCVAR_NONE, new RangeValidator<int>(0, 64));

  public static readonly FakeConVar<float> CV_POWER = new(
    "css_ttt_shop_c4_power", "Explosion power (damage multiplier) of the C4",
    100f, ConVarFlags.FCVAR_NONE, new RangeValidator<float>(0f, 10000f));

  public static readonly FakeConVar<int> CV_FUSE_TIME = new(
    "css_ttt_shop_c4_fuse_time", "Fuse time of the C4 in seconds", 30,
    ConVarFlags.FCVAR_NONE, new RangeValidator<int>(1, 300));

  public static readonly FakeConVar<bool> CV_FRIENDLY_FIRE = new(
    "css_ttt_shop_c4_ff", "Whether the C4 damages teammates");
  
  public static readonly FakeConVar<int> CV_LIMIT_MODE =
    new("css_ttt_shop_c4_limit_mode",
      "0 = Unlimited, 1 = Per Player, 2 = Per Team", 2, ConVarFlags.FCVAR_NONE,
      new RangeValidator<int>(0, 2));

  public void Dispose() { }

  public void Start() { }

  public void Start(BasePlugin? plugin) { plugin?.RegisterFakeConVars(this); }

  public Task<C4Config?> Load() {
    var cfg = new C4Config {
      Price         = CV_PRICE.Value,
      Weapon        = CV_WEAPON.Value,
      Limit         = CV_MAX.Value,
      LimitMode     = (ItemLimitMode)CV_LIMIT_MODE.Value,
      Power         = CV_POWER.Value,
      FuseTime      = TimeSpan.FromSeconds(CV_FUSE_TIME.Value),
      FriendlyFire  = CV_FRIENDLY_FIRE.Value
    };

    return Task.FromResult<C4Config?>(cfg);
  }
}