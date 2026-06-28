using TTT.Locale;

namespace TTT.RDM.lang;

public static class RdmMsgs {
  public static IMsg RDM_PROMPT(string attacker) {
    return MsgFactory.Create(nameof(RDM_PROMPT), attacker);
  }
}
