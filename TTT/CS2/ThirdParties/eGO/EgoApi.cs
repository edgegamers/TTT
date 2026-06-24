using CounterStrikeSharp.API.Core.Capabilities;
using MAULActainShared.plugin;
using RayTraceAPI;

namespace TTT.CS2.ThirdParties.eGO;

public class EgoApi {
  public static PluginCapability<IActain> MAUL { get; } =
    new("maulactain:core");

  public static PluginCapability<CRayTraceInterface>
    RAY_TRACE { get; } = new("raytrace:craytraceinterface");
}