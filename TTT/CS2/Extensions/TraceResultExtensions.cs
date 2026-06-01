using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities;
using FastGenericNew;
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
    
    var entityPointer = NativeAPI.GetEntityPointerFromHandle(trace.HitEntity);

    var instance = new CEntityInstance(entityPointer);
    
    var entityPtr = EntitySystem.GetEntityByIndex(instance.Index);

    if (entityPtr == null) return entity != null;
    var typedEntity = FastNew.CreateInstance<T, nint>(entityPtr.Value);

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