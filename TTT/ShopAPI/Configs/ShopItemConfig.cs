namespace ShopAPI.Configs;

public abstract record ShopItemConfig {
  public abstract int Price { get; init; }
  
  public virtual ItemLimitMode LimitMode { get; init; } = ItemLimitMode.OFF;
  public virtual int Limit { get; init; } = 0;
}