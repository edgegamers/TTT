using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

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

    var entityInstance = trace.HitEntity();
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
