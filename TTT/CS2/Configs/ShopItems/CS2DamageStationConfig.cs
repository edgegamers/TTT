using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Cvars.Validators;
using ShopAPI;
using ShopAPI.Configs.Traitor;
using TTT.API;
using TTT.API.Storage;

namespace TTT.CS2.Configs.ShopItems;

public class CS2DamageStationConfig : IStorage<DamageStationConfig>,
  IPluginModule {
  public static readonly FakeConVar<int> CV_PRICE = new(
    "css_ttt_shop_damagestation_price",
    "Price of the Damage Station item (Detective)", 65, ConVarFlags.FCVAR_NONE,
    new RangeValidator<int>(0, 10000));

  public static readonly FakeConVar<string> CV_USE_SOUND = new(
    "css_ttt_shop_damagestation_use_sound",
    "Sound played when using the Damage Station", "sounds/buttons/blip2");

  public static readonly FakeConVar<int> CV_DAMAGE_INCREMENTS = new(
    "css_ttt_shop_damagestation_increments",
    "Number of damage increments applied per use", -25, ConVarFlags.FCVAR_NONE,
    new RangeValidator<int>(-100, -1));

  public static readonly FakeConVar<int> CV_DAMAGE_INTERVAL = new(
    "css_ttt_shop_damagestation_interval",
    "Interval (in seconds) between damage increments", 1,
    ConVarFlags.FCVAR_NONE, new RangeValidator<int>(1, 60));

  public static readonly FakeConVar<int> CV_STATION_DAMAGE = new(
    "css_ttt_shop_damagestation_station_damage",
    "Maximum damage of the station object itself", 200, ConVarFlags.FCVAR_NONE,
    new RangeValidator<int>(50, 1000));

  public static readonly FakeConVar<int> CV_TOTAL_DAMAGE_GIVEN = new(
    "css_ttt_shop_damagestation_total_damage_given",
    "Total damage the station can provide before depleting (0 = infinite)", -3000,
    ConVarFlags.FCVAR_NONE, new RangeValidator<int>(-10000, 0));

  public static readonly FakeConVar<float> CV_MAX_RANGE = new(
    "css_ttt_shop_damagestation_max_range",
    "Maximum range (in units) from which players can use the station", 256f,
    ConVarFlags.FCVAR_NONE, new RangeValidator<float>(50f, 2048f));

  public static readonly FakeConVar<int> CV_MAX_PURCHASES = new(
    "css_ttt_shop_damagestation_max_purchases",
    "Maximum number of times a player can purchase the Damage Station per round",
    1, ConVarFlags.FCVAR_NONE, new RangeValidator<int>(1, 100));

  public static readonly FakeConVar<int> CV_LIMIT_MODE =
    new("css_ttt_shop_damagestation_limit_mode",
      "0 = Unlimited, 1 = Per Player, 2 = Per Team", 1, ConVarFlags.FCVAR_NONE,
      new RangeValidator<int>(0, 2));
  
  public void Dispose() {
    throw new NotImplementedException();
  }
  
  public void Start() {
    throw new NotImplementedException();
  }
  
  public void Start(BasePlugin? plugin) {
    ArgumentNullException.ThrowIfNull(plugin, nameof(plugin));
    plugin.RegisterFakeConVars(this);
  }
  
  public Task<DamageStationConfig?> Load() {
    var cfg = new DamageStationConfig {
      Price            = CV_PRICE.Value,
      UseSound         = CV_USE_SOUND.Value,
      HealthIncrements = CV_DAMAGE_INCREMENTS.Value,
      HealthInterval   = TimeSpan.FromSeconds(CV_DAMAGE_INTERVAL.Value),
      StationHealth    = CV_STATION_DAMAGE.Value,
      TotalHealthGiven = CV_TOTAL_DAMAGE_GIVEN.Value,
      MaxRange         = CV_MAX_RANGE.Value,
      Limit            = CV_MAX_PURCHASES.Value,
      LimitMode        = (ItemLimitMode)CV_LIMIT_MODE.Value
    };

    return Task.FromResult<DamageStationConfig?>(cfg);
  }
}