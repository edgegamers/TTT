using System.Numerics;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using TTT.Player;
using TTT.Public.Extensions;
using TTT.Public.Formatting;
using TTT.Public.Mod.Role;
using TTT.Public.Mod.Round;
using TTT.Public.Player;
using TTT.Round;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace TTT.Roles;

public class InfoManager
{
    private readonly Dictionary<CCSPlayerController, Tuple<CCSPlayerController, Role>> _playerLookAtRole = new();
    private readonly RoleManager _roleService;
    private readonly IRoundService _manager;
    private readonly Dictionary<CCSPlayerController, Tuple<string, Role>> _spectatorLookAtRole = new();

    public InfoManager(RoleManager roleService, IRoundService manager, BasePlugin plugin)
    {
        _roleService = roleService;
        _manager = manager;
        plugin.AddTimer(0.1f, OnTickScoreboard, TimerFlags.REPEAT);

        plugin.RegisterEventHandler<EventSpecTargetUpdated>(OnPlayerSpectateChange);
    }

    public void Reset()
    {
        _playerLookAtRole.Clear();
        _spectatorLookAtRole.Clear();
    }
    
    public void RegisterLookAtRole(CCSPlayerController player, Tuple<CCSPlayerController, Role> role)
    {
        _playerLookAtRole.TryAdd(player, role);
    }

    public void RemoveLookAtRole(CCSPlayerController player)
    {
        _playerLookAtRole.Remove(player);
    }

    public void OnTickScoreboard()
    {
        return;
        foreach (var player in _roleService.GetPlayers().Keys)
        {
            player.ModifyScoreBoard();
        }
    }
    

    [GameEventHandler]
    private HookResult OnPlayerSpectateChange(EventSpecTargetUpdated @event, GameEventInfo info)
    {
        var player = @event.Userid;
        var target = new CCSPlayerController(@event.Target);

        if (!player.IsReal() || !target.IsReal()) return HookResult.Continue;
        
        _spectatorLookAtRole.TryAdd(player, new Tuple<string, Role>(target.PlayerName, _roleService.GetRole(target)));
        
        return HookResult.Continue;
    }
}