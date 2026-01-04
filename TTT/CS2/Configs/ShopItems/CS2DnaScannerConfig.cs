using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Cvars.Validators;
using ShopAPI;
using ShopAPI.Configs.Detective;
using TTT.API;
using TTT.API.Storage;

namespace TTT.CS2.Configs.ShopItems;

public class CS2DnaScannerConfig : IStorage<DnaScannerConfig>, IPluginModule {
  public static readonly FakeConVar<int> CV_PRICE = new(
    "css_ttt_shop_dna_price", "Price of the DNA Scanner item (Detective)", 110,
    ConVarFlags.FCVAR_NONE, new RangeValidator<int>(0, 10000));

  public static readonly FakeConVar<int> CV_MAX_SAMPLES = new(
    "css_ttt_shop_dna_max_samples",
    "Maximum number of DNA samples that can be stored at once", 0,
    ConVarFlags.FCVAR_NONE, new RangeValidator<int>(0, 100));

  public static readonly FakeConVar<int> CV_DECAY_TIME_SECONDS = new(
    "css_ttt_shop_dna_decay_time",
    "Time (in seconds) before a DNA sample decays", 120, ConVarFlags.FCVAR_NONE,
    new RangeValidator<int>(10, 3600));

  public static readonly FakeConVar<int> CV_MAX_PURCHASES = new(
    "css_ttt_shop_dna_max_purchases",
    "Maximum number of times a player can purchase the DNA Scanner per round",
    0, ConVarFlags.FCVAR_NONE, new RangeValidator<int>(1, 100));

  public static readonly FakeConVar<int> CV_LIMIT_MODE =
    new("css_ttt_shop_dna_limit_mode",
      "0 = Unlimited, 1 = Per Player, 2 = Per Team", 0, ConVarFlags.FCVAR_NONE,
      new RangeValidator<int>(0, 2));

  public void Dispose() { }

  public void Start() { }

  public void Start(BasePlugin? plugin) {
    ArgumentNullException.ThrowIfNull(plugin, nameof(plugin));
    plugin.RegisterFakeConVars(this);
  }

  public Task<DnaScannerConfig?> Load() {
    var cfg = new DnaScannerConfig {
      Price      = CV_PRICE.Value,
      MaxSamples = CV_MAX_SAMPLES.Value,
      DecayTime  = TimeSpan.FromSeconds(CV_DECAY_TIME_SECONDS.Value),
      Limit      = CV_MAX_PURCHASES.Value,
      LimitMode  = (ItemLimitMode)CV_LIMIT_MODE.Value
    };

    return Task.FromResult<DnaScannerConfig?>(cfg);
  }
}