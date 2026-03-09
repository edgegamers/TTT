using TTT.API.Events;
using TTT.API.Player;

namespace TTT.Karma;

public record KarmaUpdate(IPlayer Player, int Delta, Event? SourceEvent = null, string? Reason = "Unknown");
