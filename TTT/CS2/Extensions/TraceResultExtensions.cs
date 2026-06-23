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

    // The ray hit nothing, world geometry, or a static prop: HitEntity is a
    // null/invalid native pointer. Wrapping it and reading a schema field
    // (DesignerName) dereferences bad memory and hard-crashes the server, so
    // bail before touching it.
    if (trace.HitEntity == nint.Zero) return false;

    var entityInstance = new CEntityInstance(trace.HitEntity);
    if (!entityInstance.IsValid) return false;

    var hitName = entityInstance.DesignerName;
    if (string.IsNullOrWhiteSpace(hitName)) return false;

    // Honor the requested name filter (previously designerName/matchType were
    // ignored, so this returned the first named entity it hit regardless).
    var matches = matchType switch {
      DesignerNameMatchType.Equals     => hitName == designerName,
      DesignerNameMatchType.StartsWith => hitName.StartsWith(designerName),
      DesignerNameMatchType.EndsWith   => hitName.EndsWith(designerName),
      DesignerNameMatchType.Contains   => hitName.Contains(designerName),
      _                                => false
    };
    if (!matches) return false;

    entity = entityInstance.As<T>();
    return true;
  }
}
