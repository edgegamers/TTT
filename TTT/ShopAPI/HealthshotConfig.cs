using ShopAPI.Configs;

namespace ShopAPI;

public record HealthshotConfig : ShopItemConfig {
  public override int Price { get; init; } = 30;
  public override ItemLimitMode LimitMode { get; init; } =
    ItemLimitMode.PER_PLAYER;
  public override int Limit { get; init; } = 2;
  public string Weapon { get; init; } = "weapon_healthshot";
}