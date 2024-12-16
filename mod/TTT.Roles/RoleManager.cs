using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using TTT.Player;
using TTT.Public.Action;
using TTT.Public.Behaviors;
using TTT.Public.Extensions;
using TTT.Public.Formatting;
using TTT.Public.Mod.Role;
using TTT.Public.Mod.Round;
using TTT.Roles.Shop;
using TTT.Round;

namespace TTT.Roles;

public class RoleManager : PlayerHandler, IRoleService, IPluginBehavior
{
    private const int MaxDetectives = 3;

    private int _innocentsLeft;
    private IRoundService _roundService;
    private int _traitorsLeft;
    private InfoManager _infoManager;
    private MuteManager _muteManager;
    private EntityGlowManager _entityGlowManager;
    
    public void Start(BasePlugin parent)
    {
        _roundService = new RoundManager(this, parent);
        _infoManager = new InfoManager(this, _roundService, parent);
        _muteManager = new MuteManager(parent);
        _entityGlowManager = new EntityGlowManager(parent);
        ModelHandler.RegisterListener(parent);
        //ShopManager.Register(parent, this); //disabled until items are implemented.
        //CreditManager.Register(parent, this);
        
        parent.RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnect);
        parent.RegisterEventHandler<EventRoundFreezeEnd>(OnRoundStart);
        parent.RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
        parent.RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        parent.RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath, HookMode.Pre);
    }

    [GameEventHandler]
    private HookResult OnRoundStart(EventRoundFreezeEnd @event, GameEventInfo info)
    {
        _roundService.SetRoundStatus(RoundStatus.Waiting);
        foreach (var player in Utilities.GetPlayers().Where(player => player.IsReal() && player.Team != CsTeam.None || player.Team != CsTeam.Spectator))
        {
            player.RemoveWeapons();
            player.GiveNamedItem("weapon_knife");
            player.GiveNamedItem("weapon_glock");
        }
        return HookResult.Continue;
    }

    [GameEventHandler]
    private HookResult OnPlayerConnect(EventPlayerConnectFull @event, GameEventInfo info)
    {
        if (Utilities.GetPlayers().Count(player => player.IsReal() && player.Team != CsTeam.None || player.Team == CsTeam.Spectator) < 3)
        {
            _roundService.ForceEnd();
        }
        
        CreatePlayer(@event.Userid);
        
        return HookResult.Continue;
    }
    
    [GameEventHandler]
    private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        info.DontBroadcast = true;

        var playerWhoWasDamaged = @event.Userid;
        var attacker = @event.Attacker;

        if (playerWhoWasDamaged == null) return HookResult.Continue;

        playerWhoWasDamaged.ModifyScoreBoard();
        
        GetPlayer(playerWhoWasDamaged).SetKiller(attacker);
        
        _muteManager.Mute(playerWhoWasDamaged);
        
        if (IsTraitor(playerWhoWasDamaged)) _traitorsLeft--;
        
        if (IsDetective(playerWhoWasDamaged) || IsInnocent(playerWhoWasDamaged)) _innocentsLeft--;
        
        if (_traitorsLeft == 0 || _innocentsLeft == 0) Server.NextFrame(() => _roundService.ForceEnd());

        Server.NextFrame(() => playerWhoWasDamaged.CommitSuicide(false, true));
            
        Server.NextFrame(() => SendDeathMessage(playerWhoWasDamaged, attacker));
        
        return HookResult.Continue;
    }

    [GameEventHandler]
    private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        var players = Utilities.GetPlayers()
            .Where(player => player.IsValid).Where(player => player.IsReal()).ToList();

        foreach (var player in players) player.PrintToCenter(GetWinner().FormatStringFullAfter("s has won!"));

        Server.NextFrame(Clear);
        _muteManager.UnMuteAll();
        _entityGlowManager.Dispose();
        return HookResult.Continue;
    }

    [GameEventHandler]
    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        Server.NextFrame(() =>
        {
            RemovePlayer(player);
            if (GetPlayers().Count == 0) _roundService.SetRoundStatus(RoundStatus.Paused);
        });
        
        return HookResult.Continue;
    }
    
    public void AddRoles()
    {
        var eligible = Utilities.GetPlayers()
            .Where(player => player.IsReal())
            .Where(player => player.Team is not (CsTeam.Spectator or CsTeam.None))
            .ToList();

        var traitorCount = (int)Math.Floor(Convert.ToDouble(eligible.Count / 3));
        var detectiveCount = (int)Math.Floor(Convert.ToDouble(eligible.Count / 8));

        _traitorsLeft = traitorCount;
        _innocentsLeft = eligible.Count - traitorCount;

        if (detectiveCount > MaxDetectives) detectiveCount = MaxDetectives;

        for (var i = 0; i < traitorCount; i++)
        {
            var chosen = eligible[Random.Shared.Next(eligible.Count)];
            eligible.Remove(chosen);
            AddTraitor(chosen);
        }

        for (var i = 0; i < detectiveCount; i++)
        {
            var chosen = eligible[Random.Shared.Next(eligible.Count)];
            eligible.Remove(chosen);
            AddDetective(chosen);
        }

        AddInnocents(eligible);
        _entityGlowManager.SetTraitors(GetTraitors().ToList());
    }

    public ISet<CCSPlayerController> GetTraitors()
    {
        return Players().Where(player => player.PlayerRole() == Role.Traitor).Select(player => player.Player()).ToHashSet();
    }

    public ISet<CCSPlayerController> GetDetectives()
    {
        return Players().Where(player => player.PlayerRole() == Role.Detective).Select(player => player.Player()).ToHashSet();
    }

    public ISet<CCSPlayerController> GetInnocents()
    {
        return Players().Where(player => player.PlayerRole() == Role.Innocent).Select(player => player.Player()).ToHashSet();
    }
    

    public Role GetRole(CCSPlayerController player)
    {
        return GetPlayer(player).PlayerRole();
    }

    public void AddTraitor(CCSPlayerController player)
    {
        GetPlayer(player).SetPlayerRole(Role.Traitor);
        player.SwitchTeam(CsTeam.Terrorist);
        player.PrintToCenter(Role.Traitor.FormatStringFullBefore("You are now a(n)"));
        player.PrintToChat(Role.Traitor.FormatStringFullBefore("You are now a(n)"));
        ModelHandler.SetModelNextServerFrame(player, ModelHandler.ModelPathTmPhoenix);
    }

    public void AddDetective(CCSPlayerController player)
    {
        GetPlayer(player).SetPlayerRole(Role.Detective);
        player.SwitchTeam(CsTeam.CounterTerrorist);
        player.PrintToCenter(Role.Detective.FormatStringFullBefore("You are now a(n)"));
        player.GiveNamedItem(CsItem.Taser);
        ModelHandler.SetModelNextServerFrame(player, ModelHandler.ModelPathCtmSas);
    }

    public void AddInnocents(IEnumerable<CCSPlayerController> players)
    {
        foreach (var player in players)
        {
            GetPlayer(player).SetPlayerRole(Role.Innocent);
            player.PrintToCenter(Role.Innocent.FormatStringFullBefore("You are now an"));
            player.SwitchTeam(CsTeam.Terrorist);     
            ModelHandler.SetModelNextServerFrame(player, ModelHandler.ModelPathTmPhoenix);
        }
    }

    public bool IsDetective(CCSPlayerController player)
    {
        return GetPlayer(player).PlayerRole() == Role.Detective;
    }

    public bool IsTraitor(CCSPlayerController player)
    {
        return GetPlayer(player).PlayerRole() == Role.Traitor;
    }

    public void Clear()
    {
        Clr();
        _infoManager.Reset();
        foreach (var key in GetPlayers()) key.Value.SetPlayerRole(Role.Unassigned);
    }
    
    public bool IsInnocent(CCSPlayerController player)
    {
        return GetPlayer(player).PlayerRole() == Role.Innocent;
    }

    private Role GetWinner()
    {
        return _traitorsLeft == 0 ? Role.Traitor : Role.Innocent;
    }

    private void SendDeathMessage(CCSPlayerController playerWhoWasDamaged, CCSPlayerController attacker)
    {
        Server.PrintToChatAll(StringUtils.FormatTTT($"{GetRole(playerWhoWasDamaged).FormatStringFullAfter(" has been found.")}"));
            
        if (attacker == playerWhoWasDamaged || attacker == null) return;
            
        attacker.ModifyScoreBoard();
            
        playerWhoWasDamaged.PrintToChat(StringUtils.FormatTTT(
            $"You were killed by {GetRole(attacker).FormatStringFullAfter(" " + attacker.PlayerName)}."));
        attacker.PrintToChat(StringUtils.FormatTTT($"You killed {GetRole(playerWhoWasDamaged).FormatStringFullAfter(" " + playerWhoWasDamaged.PlayerName)}."));

    }
}