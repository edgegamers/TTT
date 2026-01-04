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

public class CS2PoisonSmokeConfig : IStorage<PoisonSmokeConfig>, IPluginModule {
  public static readonly FakeConVar<int> CV_PRICE = new(
    "css_ttt_shop_poisonsmoke_price", "Price of the Poison Smoke item", 45,
    ConVarFlags.FCVAR_NONE, new RangeValidator<int>(0, 10000));

  public static readonly FakeConVar<string> CV_WEAPON = new(
    "css_ttt_shop_poisonsmoke_weapon",
    "Weapon entity name used for the Poison Smoke item", "weapon_smokegrenade",
    ConVarFlags.FCVAR_NONE, new ItemValidator(allowMultiple: false));

  // Poison effect sub-config
  public static readonly FakeConVar<int> CV_POISON_TICK_DAMAGE = new(
    "css_ttt_shop_poisonsmoke_poison_damage_per_tick",
    "Damage dealt per poison tick", 15, ConVarFlags.FCVAR_NONE,
    new RangeValidator<int>(1, 100));

  public static readonly FakeConVar<int> CV_POISON_TOTAL_DAMAGE = new(
    "css_ttt_shop_poisonsmoke_poison_total_damage",
    "Total damage dealt over the poison duration", 500, ConVarFlags.FCVAR_NONE,
    new RangeValidator<int>(1, 1000));

  public static readonly FakeConVar<int> CV_POISON_TICK_INTERVAL = new(
    "css_ttt_shop_poisonsmoke_poison_tick_interval",
    "Milliseconds between each poison damage tick", 500, ConVarFlags.FCVAR_NONE,
    new RangeValidator<int>(100, 10000));

  public static readonly FakeConVar<string> CV_POISON_SOUND = new(
    "css_ttt_shop_poisonsmoke_poison_sound",
    "Sound played when poison deals damage",
    "sounds/player/player_damagebody_03");

  public static readonly FakeConVar<int> CV_MAX_PURCHASES = new(
    "css_ttt_shop_poisonsmoke_max_purchases",
    "Maximum number of times a player can purchase the Damage Station per round",
    0, ConVarFlags.FCVAR_NONE, new RangeValidator<int>(1, 100));

  public static readonly FakeConVar<int> CV_LIMIT_MODE =
    new("css_ttt_shop_poisonsmoke_limit_mode",
      "0 = Unlimited, 1 = Per Player, 2 = Per Team", 0, ConVarFlags.FCVAR_NONE,
      new RangeValidator<int>(0, 2));

  public void Dispose() { }

  public void Start() { }

  public void Start(BasePlugin? plugin) {
    ArgumentNullException.ThrowIfNull(plugin, nameof(plugin));
    plugin.RegisterFakeConVars(this);
  }

  public Task<PoisonSmokeConfig?> Load() {
    var poison = new PoisonConfig {
      TimeBetweenDamage =
        TimeSpan.FromMilliseconds(CV_POISON_TICK_INTERVAL.Value),
      DamagePerTick = CV_POISON_TICK_DAMAGE.Value,
      TotalDamage   = CV_POISON_TOTAL_DAMAGE.Value,
      PoisonSound   = CV_POISON_SOUND.Value
    };

    var cfg = new PoisonSmokeConfig {
      Price        = CV_PRICE.Value,
      Weapon       = CV_WEAPON.Value,
      PoisonConfig = poison,
      Limit        = CV_MAX_PURCHASES.Value,
      LimitMode    = (ItemLimitMode)CV_LIMIT_MODE.Value
    };

    return Task.FromResult<PoisonSmokeConfig?>(cfg);
  }
}