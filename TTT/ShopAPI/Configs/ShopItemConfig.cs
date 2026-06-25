namespace ShopAPI.Configs;

public abstract record ShopItemConfig {
  public abstract int Price { get; init; }

  /// <summary>
  ///   Max times a single player may purchase this item per round.
  ///   0 (default) = unlimited.
  /// </summary>
  public virtual int PurchaseLimit { get; init; }
}