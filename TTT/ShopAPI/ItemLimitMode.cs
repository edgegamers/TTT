namespace ShopAPI;

public enum ItemLimitMode {
  /// <summary>
  ///   No Limit. Players May purchase as many items as they want.
  /// </summary>
  OFF,
  
  /// <summary>
  ///   Limits the item per player. Up to the Set Limit.
  /// </summary>
  PER_PLAYER,
  
  /// <summary>
  ///   Limits the item for the entire team. Up to the Set Limit.
  /// </summary>
  PER_TEAM
}