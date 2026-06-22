using System.Linq.Expressions;
using CounterStrikeSharp.API.Core;
using RayTraceAPI;

namespace TTT.CS2.Extensions;

public static class TraceResultExtensions {
  public enum DesignerNameMatchType {
    Equals, StartsWith, EndsWith, Contains
  }

  public static bool TryGetHitEntityByDesignerName<T>(
    this TraceResult trace,
    string designerName,
    out T? entity,
    DesignerNameMatchType matchType = DesignerNameMatchType.Contains)
    where T : CBaseEntity {
    entity = null;

    var pointer = (nint)trace.HitEntity;

    if (pointer == nint.Zero) {
      return false;
    }

    // Use the base wrapper only to inspect shared entity data.
    var hitEntity = new CBaseEntity(pointer);

    if (!MatchesDesignerName(
      hitEntity.DesignerName,
      designerName,
      matchType)) {
      return false;
    }

    entity = EntityFactory<T>.Create(pointer);
    return true;
  }

  private static bool MatchesDesignerName(
    string? actual,
    string expected,
    DesignerNameMatchType matchType) {
    if (string.IsNullOrWhiteSpace(actual)) {
      return false;
    }

    return matchType switch {
      DesignerNameMatchType.Equals =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),

      DesignerNameMatchType.StartsWith =>
        actual.StartsWith(expected, StringComparison.OrdinalIgnoreCase),

      DesignerNameMatchType.EndsWith =>
        actual.EndsWith(expected, StringComparison.OrdinalIgnoreCase),

      _ => actual.Contains(expected, StringComparison.OrdinalIgnoreCase)
    };
  }

  private static class EntityFactory<T>
    where T : CBaseEntity {
    public static readonly Func<nint, T> Create = Build();

    private static Func<nint, T> Build() {
      var constructor = typeof(T).GetConstructor([typeof(nint)])
        ?? throw new InvalidOperationException(
          $"{typeof(T).Name} must expose a public constructor accepting nint.");

      var pointer = Expression.Parameter(typeof(nint), "pointer");

      return Expression.Lambda<Func<nint, T>>(
        Expression.New(constructor, pointer),
        pointer).Compile();
    }
  }
}
