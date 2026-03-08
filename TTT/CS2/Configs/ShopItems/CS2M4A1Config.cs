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

public class CS2M4A1Config : IStorage<M4A1Config>, IPluginModule {
  public static readonly FakeConVar<int> CV_PRICE = new(
    "css_ttt_shop_m4a1_price", "Price of the M4A1 item", 50,
    ConVarFlags.FCVAR_NONE, new RangeValidator<int>(0, 10000));

  public static readonly FakeConVar<string> CV_CLEAR_SLOTS = new(
    "css_ttt_shop_m4a1_clear_slots",
    "Slots to clear when granting M4A1 (comma-separated ints)", "0,1");

  public static readonly FakeConVar<string> CV_WEAPONS = new(
    "css_ttt_shop_m4a1_weapons",
    "Weapons granted with this item (comma-separated names)",
    "weapon_m4a1_silencer,weapon_usp_silencer", ConVarFlags.FCVAR_NONE,
    new ItemValidator(allowMultiple: true));

  public static readonly FakeConVar<int> CV_MAX_PURCHASES = new(
    "css_ttt_shop_m4a1_max_purchases",
    "Maximum number of times a player can purchase the M4A1 per round", 0,
    ConVarFlags.FCVAR_NONE, new RangeValidator<int>(1, 100));

  public static readonly FakeConVar<int> CV_LIMIT_MODE =
    new("css_ttt_shop_m4a1_limit_mode",
      "0 = Unlimited, 1 = Per Player, 2 = Per Team", 0, ConVarFlags.FCVAR_NONE,
      new RangeValidator<int>(0, 2));

  public void Dispose() { }

  public void Start() { }

  public void Start(BasePlugin? plugin) {
    ArgumentNullException.ThrowIfNull(plugin, nameof(plugin));
    plugin.RegisterFakeConVars(this);
  }

  public Task<M4A1Config?> Load() {
    var slots = CV_CLEAR_SLOTS.Value.Split(',')
     .Select(s => s.Trim())
     .Where(s => int.TryParse(s, out _))
     .Select(int.Parse)
     .ToArray();

    var weapons = CV_WEAPONS.Value.Split(',')
     .Select(s => s.Trim())
     .Where(s => !string.IsNullOrEmpty(s))
     .ToArray();

    var cfg = new M4A1Config {
      Price      = CV_PRICE.Value,
      ClearSlots = slots,
      Weapons    = weapons,
      Limit      = CV_MAX_PURCHASES.Value,
      LimitMode  = (ItemLimitMode)CV_LIMIT_MODE.Value
    };

    return Task.FromResult<M4A1Config?>(cfg);
  }
}