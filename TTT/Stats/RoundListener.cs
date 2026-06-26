using System.Net;
using System.Text;
using System.Text.Json;
using CounterStrikeSharp.API;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using ShopAPI.Events;
using Stats.lang;
using TTT.API.Events;
using TTT.API.Game;
using TTT.API.Messages;
using TTT.API.Role;
using TTT.Game.Events.Body;
using TTT.Game.Events.Game;
using TTT.Game.Events.Player;
using TTT.Game.Roles;
using TTT.Locale;

namespace Stats;

public class RoundListener(IServiceProvider provider)
  : IListener, IRoundTracker {
  private readonly Dictionary<string, int> bodiesFound = new();
  private readonly HashSet<string> deaths = new();

  private readonly Dictionary<string, (int, int, int)> kills = new();

  // Per-round credits earned (positive balance deltas), and per-player connect
  // times for computing connected playtime. Feeds the seasons/awards stats.
  private readonly Dictionary<string, int> creditsEarned = new();
  private readonly Dictionary<string, DateTime> joinedAt = new();
  private DateTime? roundStartedAt;

  private readonly IMsgLocalizer localizer =
    provider.GetRequiredService<IMsgLocalizer>();

  private readonly IMessenger messenger =
    provider.GetRequiredService<IMessenger>();

  private readonly IRoleAssigner roles =
    provider.GetRequiredService<IRoleAssigner>();

  public void Dispose() {
    provider.GetRequiredService<IEventBus>().UnregisterListener(this);
  }

  public int? CurrentRoundId { get; set; }

  [UsedImplicitly]
  [EventHandler(Priority = Priority.MONITOR, IgnoreCanceled = true)]
  public void OnGameState(GameStateUpdateEvent ev) {
    // Reset earned-credits at countdown so the round-start role grant (fired
    // during StartRound, before IN_PROGRESS) is counted toward this round.
    if (ev.NewState == State.COUNTDOWN) creditsEarned.Clear();

    if (ev.NewState == State.IN_PROGRESS) {
      kills.Clear();
      bodiesFound.Clear();
      deaths.Clear();
      roundStartedAt = DateTime.UtcNow;
      Task.Run(async () => await onRoundStart(ev.Game));
    }

    var game = ev.Game;
    if (ev.NewState == State.FINISHED)
      Task.Run(async () => await onRoundEnd(game));
  }

  // Accumulate credits *earned* (positive deltas) per player for the round.
  [UsedImplicitly]
  [EventHandler(Priority = Priority.MONITOR, IgnoreCanceled = true)]
  public void OnBalanceChange(PlayerBalanceEvent ev) {
    if (ev.NewBalance <= ev.OldBalance) return;
    var id = ev.Player.Id;
    creditsEarned[id] = creditsEarned.GetValueOrDefault(id, 0)
      + (ev.NewBalance - ev.OldBalance);
  }

  // Track when each player connected, to compute connected playtime per round.
  [UsedImplicitly]
  [EventHandler]
  public void OnJoin(PlayerJoinEvent ev) {
    joinedAt[ev.Player.Id] = DateTime.UtcNow;
  }

  [UsedImplicitly]
  [EventHandler]
  public void OnDeath(PlayerDeathEvent ev) {
    var killer = ev.Killer;
    var victim = ev.Victim;

    deaths.Add(victim.Id);

    if (killer == null) return;

    if (!kills.ContainsKey(killer.Id)) kills[killer.Id] = (0, 0, 0);

    var (innoKills, traitorKills, detectiveKills) = kills[killer.Id];
    var victimRoles = roles.GetRoles(victim);

    if (victimRoles.Any(r => r is InnocentRole))
      innoKills += 1;
    else if (victimRoles.Any(r => r is TraitorRole))
      traitorKills                                                    += 1;
    else if (victimRoles.Any(r => r is DetectiveRole)) detectiveKills += 1;

    kills[killer.Id] = (innoKills, traitorKills, detectiveKills);
  }

  [UsedImplicitly]
  [EventHandler(Priority = Priority.MONITOR, IgnoreCanceled = true)]
  public void OnIdentify(BodyIdentifyEvent ev) {
    if (ev.Identifier == null) return;
    var identifies = bodiesFound.GetValueOrDefault(ev.Identifier.Id, 0);
    bodiesFound[ev.Identifier.Id] = identifies + 1;
  }

  private async Task onRoundStart(IGame game) {
    Console.WriteLine("RoundListener: onRoundStart fired");
    await Server.NextWorldUpdateAsync(() => {
      var map_name  = Server.MapName;
      var startedAt = DateTime.UtcNow;
      Task.Run(async () => await createNewRound(game, map_name, startedAt));
    });
  }

  private async Task createNewRound(IGame game, string map_name,
    DateTime startedAt) {
    var data = new {
      map_name, startedAt, participants = getParticipants(game)
    };

    var content = new StringContent(JsonSerializer.Serialize(data),
      Encoding.UTF8, "application/json");

    var client = provider.GetRequiredService<HttpClient>();
    var response = await provider.GetRequiredService<HttpClient>()
     .PostAsync("round", content);

    var json    = await response.Content.ReadAsStringAsync();
    var jsonDoc = JsonDocument.Parse(json);
    CurrentRoundId = jsonDoc.RootElement.GetProperty("round_id").GetInt32();

    if (response.StatusCode == HttpStatusCode.Created) {
      await notifyNewRound(CurrentRoundId.Value);
      return;
    }

    if (response.StatusCode == HttpStatusCode.Conflict)
      await client.DeleteAsync("round/" + CurrentRoundId);

    // Retry
    response = await client.PostAsync("round", content);

    jsonDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    CurrentRoundId = jsonDoc.RootElement.GetProperty("round_id").GetInt32();
    if (response.StatusCode == HttpStatusCode.Created)
      await notifyNewRound(CurrentRoundId.Value);
  }

  private Task notifyNewRound(int id) {
    return messenger.MessageAll(localizer[StatsMsgs.API_ROUND_START(id)]);
  }

  private async Task onRoundEnd(IGame game) {
    if (CurrentRoundId == null) return;

    var ended_at = DateTime.UtcNow;
    var winning_role = game.WinningRole != null ?
      StatsApi.ApiNameForRole(game.WinningRole) :
      null;
    var participants = getParticipants(game);
    await Task.Run(async () => {
      var data = new { ended_at, winning_role, participants };

      var content = new StringContent(JsonSerializer.Serialize(data),
        Encoding.UTF8, "application/json");

      var client = provider.GetRequiredService<HttpClient>();

      await client.PatchAsync("round/" + CurrentRoundId, content);
    });
  }

  private List<Participant> getParticipants(IGame game) {
    var list = new List<Participant>();

    var now        = DateTime.UtcNow;
    var roundStart = roundStartedAt ?? now;

    foreach (var player in game.Players) {
      var playerRoles = roles.GetRoles(player);
      if (playerRoles.Count == 0) continue;

      var role = StatsApi.ApiNameForRole(playerRoles.First());
      kills.TryGetValue(player.Id, out var killCounts);
      bodiesFound.TryGetValue(player.Id, out var foundCount);
      creditsEarned.TryGetValue(player.Id, out var earned);

      // Connected seconds within this round: from the later of round start or
      // the player's connect time, up to now (round end when sent via PATCH).
      var sessionStart = joinedAt.TryGetValue(player.Id, out var j) && j > roundStart
        ? j
        : roundStart;
      var playtime = Math.Max(0, (int)(now - sessionStart).TotalSeconds);

      list.Add(new Participant {
        steam_id         = player.Id,
        role             = role,
        inno_kills       = killCounts.Item1,
        traitor_kills    = killCounts.Item2,
        detective_kills  = killCounts.Item3,
        bodies_found     = foundCount,
        died             = deaths.Contains(player.Id),
        credits_earned   = earned,
        playtime_seconds = playtime
      });
    }

    return list;
  }

  private record Participant {
    public required string steam_id { get; init; }
    public required string role { get; init; }
    public int? inno_kills { get; init; }
    public int? traitor_kills { get; init; }
    public int? detective_kills { get; init; }
    public int? bodies_found { get; init; }
    public bool? died { get; init; }
    public int? credits_earned { get; init; }
    public int? playtime_seconds { get; init; }
  }
}