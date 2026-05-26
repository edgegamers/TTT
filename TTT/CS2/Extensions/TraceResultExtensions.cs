using CounterStrikeSharp.API.Core;
using RayTraceAPI;

namespace TTT.CS2.Extensions;

public static class TraceResultExtensions {
  public enum DesignerNameMatchType {
    Equals, StartsWith, EndsWith, Contains
  }

  public static bool TryGetHitEntityByDesignerName<T>(this TraceResult trace,
    string designerName, out T? entity,
    DesignerNameMatchType matchType = DesignerNameMatchType.Contains)
    where T : CEntityInstance {
    entity = null;

    if (!trace.DidHit || trace.HitEntity == 0) return false;

    if (Activator.CreateInstance(typeof(T), trace.HitEntity) is not T {
      IsValid: true
    } typedEntity)
      return false;

    entity = matchType switch {
      DesignerNameMatchType.Equals => typedEntity.DesignerName.Equals(
        designerName, StringComparison.OrdinalIgnoreCase) ?
        typedEntity :
        null,
      DesignerNameMatchType.StartsWith => typedEntity.DesignerName.StartsWith(
        designerName, StringComparison.OrdinalIgnoreCase) ?
        typedEntity :
        null,
      DesignerNameMatchType.EndsWith => typedEntity.DesignerName.EndsWith(
        designerName, StringComparison.OrdinalIgnoreCase) ?
        typedEntity :
        null,
      DesignerNameMatchType.Contains => typedEntity.DesignerName.Contains(
        designerName, StringComparison.OrdinalIgnoreCase) ?
        typedEntity :
        null,
      _ => null
    };

    return entity != null;
  }
}