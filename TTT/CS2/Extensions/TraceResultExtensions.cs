using CounterStrikeSharp.API.Core;
using RayTraceAPI;

namespace TTT.CS2.Extensions;

public static class TraceResultExtensions {
  public static bool TryGetHitEntityByDesignerName<T>(this TraceResult trace,
    string designerNameContains, out T? entity) where T : CEntityInstance {
    entity = null;

    if (!trace.DidHit || trace.HitEntity == IntPtr.Zero) return false;

    var baseEntity = new CEntityInstance(trace.HitEntity);

    if (!baseEntity.IsValid) return false;

    if (string.IsNullOrWhiteSpace(baseEntity.DesignerName)) return false;

    if (!baseEntity.DesignerName.Contains(designerNameContains,
      StringComparison.OrdinalIgnoreCase))
      return false;

    try {
      var typedEntity =
        (T)Activator.CreateInstance(typeof(T), baseEntity.Handle)!;

      if (!typedEntity.IsValid) return false;

      entity = typedEntity;
      return true;
    } catch { return false; }
  }
}