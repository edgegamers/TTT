using TTT.Locale;

namespace TTT.RDM.lang;

public static class RdmMsgs {
  public static IMsg RDM_PROMPT(string attacker) {
    return MsgFactory.Create(nameof(RDM_PROMPT), attacker);
  }

  public static IMsg RDM_STAFF_NEW_REPORT(string victim, string attacker,
    int caseId) {
    return MsgFactory.Create(nameof(RDM_STAFF_NEW_REPORT), victim, attacker,
      caseId);
  }
}
