using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace TTT.Roles;

public class EntityGlowManager
{
    private List<CBaseModelEntity> _glowingEntities = new();
    private List<CCSPlayerController> _traitors = new();

    public EntityGlowManager(BasePlugin plugin)
    {
        plugin.RegisterListener<Listeners.CheckTransmit>(RemoveGlow);
    }

    public void SetTraitors(List<CCSPlayerController> traitors)
    {
        _traitors = traitors;
        foreach (var traitor in traitors)
        {
            SetGlowing(traitor.PlayerPawn.Value!);
        }
    }

    private void RemoveGlow(CCheckTransmitInfoList infoList)
    {
        foreach (var (info, player) in infoList)
        {
            if (player == null)
                continue;

            if (_traitors.Contains(player))
                continue;

            foreach (var model in _glowingEntities)
            {
                info.TransmitEntities.Remove(model);
            }
        }
    }
    
    public void Dispose()
    {
        List<CBaseModelEntity> entities = _glowingEntities;
        
        _glowingEntities.Clear();
        
        Server.RunOnTick(5, () =>
        {
            foreach (var entity in _glowingEntities)
            {
                entity.Remove();
            }
        });
        
        entities.Clear();
    }

    private void SetGlowing(CCSPlayerPawn pawn)
    {
        CBaseModelEntity? modelGlow = Utilities.CreateEntityByName<CBaseModelEntity>("prop_dynamic");
        CBaseModelEntity? modelRelay = Utilities.CreateEntityByName<CBaseModelEntity>("prop_dynamic");
        if (modelGlow == null || modelRelay == null)
        {
            return;
        }

        string modelName = pawn.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName;

        modelRelay.SetModel(modelName);
        modelRelay.Spawnflags = 256u;
        modelRelay.RenderMode = RenderMode_t.kRenderNone;
        modelRelay.DispatchSpawn();

        modelGlow.SetModel(modelName);
        modelGlow.Spawnflags = 256u;
        modelGlow.DispatchSpawn();

        modelGlow.Glow.GlowColorOverride = Color.Red;
        modelGlow.Glow.GlowRange = 5000;
        modelGlow.Glow.GlowTeam = -1;
        modelGlow.Glow.GlowType = 3;
        modelGlow.Glow.GlowRangeMin = 100;

        modelRelay.AcceptInput("FollowEntity", pawn, modelRelay, "!activator");
        modelGlow.AcceptInput("FollowEntity", modelRelay, modelGlow, "!activator");

        _glowingEntities.Add(modelGlow);
        _glowingEntities.Add(modelRelay);
    }
}