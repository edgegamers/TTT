using CounterStrikeSharp.API.Core;
using RayTraceAPI;

namespace TTT.CS2.Extensions;

public static class TraceResultExtensions {
  public static bool TryGetHitEntityByDesignerName<T>(this TraceResult trace,
    string designerNameContains, out T? entity) where T : CEntityInstance
  {
    entity = null;

    if (!trace.DidHit || trace.HitEntity == IntPtr.Zero) return false;
    
    var baseEntity = new CEntityInstance(trace.HitEntity);
    if (!baseEntity.IsValid) return false;

    var designerName = baseEntity.DesignerName;
    if (string.IsNullOrWhiteSpace(designerName)) return false;

    if (!designerName.Contains(designerNameContains, StringComparison.OrdinalIgnoreCase))
      return false;
    
    try
    {
      var typedEntity = (T?)Activator.CreateInstance(typeof(T), trace.HitEntity);
      if (typedEntity == null || !typedEntity.IsValid) return false;

      entity = typedEntity;
      return true;
    }
    catch { return false; }
  }
}