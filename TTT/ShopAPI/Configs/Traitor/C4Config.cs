namespace ShopAPI.Configs.Traitor;

// TODO: Support this config
public record C4Config : ShopItemConfig {
  public override int Price { get; init; } = 90;
  public string Weapon { get; init; } = "c4";
  public override ItemLimitMode LimitMode { get; init; } = ItemLimitMode.PER_TEAM;
  public override int Limit { get; init; } = 1;
  public float Power { get; init; } = 100f;
  public TimeSpan FuseTime { get; init; } = TimeSpan.FromSeconds(30);
  public bool FriendlyFire { get; init; } = false;
}