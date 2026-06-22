using System.Linq.Expressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Memory;
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

    var entityInstance = new CEntityInstance(trace.HitEntity);

    if (string.IsNullOrWhiteSpace(entityInstance.DesignerName)) { return false; }

    var baseEntity = entityInstance.As<CBaseEntity>();
    
    entity = baseEntity.As<T>();
    return true;
  }
}
