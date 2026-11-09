using System.Text.RegularExpressions;

namespace KnowledgeAgent.Features.Sources.Ingestion;

// Flags text that talks to the assistant instead of the reader: the agent's own tool names, "ignore previous
// instructions" and the like. Cheap and deterministic; it only labels the source for a person to read, it blocks nothing.
internal static partial class InjectionScan
{
    public static bool IsSuspicious(string text) => !string.IsNullOrEmpty(text) && Pattern().IsMatch(text);

    public static int CountSuspicious(IEnumerable<string> chunks) => chunks.Count(IsSuspicious);

    [GeneratedRegex(
        """
        \b(?:record_feedback|request_human_handover|delegate_research|transfer_to_agent|transfer_back)\b
        |\bmcp_[a-z0-9]+__\w+
        |\b(?:ignore|disregard|forget)\s+(?:all\s+|any\s+)?(?:the\s+|your\s+)?(?:previous|prior|above|earlier|preceding)\s+(?:instructions|messages|prompts|rules)
        |\bsystem\s+prompt\b
        |\b(?:note|message|instructions?)\s+(?:to|for)\s+(?:the\s+)?(?:assistant|ai|chatbot|agent|model|llm)\b
        |\b(?:assistant|chatbot|ai)\s*[,:]\s*(?:please\s+)?(?:call|invoke|use|run|execute)\b
        |\b(?:ignoruj|ignorujte)\s+(?:všetky\s+|všechny\s+)?(?:predchádzajúce|předchozí|predošlé)\s+(?:pokyny|inštrukcie|instrukce)
        |\b(?:poznámka|pokyn|pokyny)\s+pre\s+(?:asistenta|ai|chatbota|agenta)\b
        |\b(?:asistent|asistente)\s*[,:]\s*(?:prosím\s+)?(?:zavolaj|spusti|použi|zavolej|spusť|použij)\b
        """,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex Pattern();
}
