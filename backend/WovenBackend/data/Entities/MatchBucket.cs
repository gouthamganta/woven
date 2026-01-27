namespace WovenBackend.Data.Entities;
 
public enum MatchBucket
{
    CORE_FIT = 1,          // High intent + foundational alignment
    LIFESTYLE_FIT = 2,     // Kids/diet/routine alignment
    CONVERSATION_FIT = 3,  // Communication/pulse alignment
    EXPLORER = 4,          // Slightly outside core but compatible
    WILDCARD = 5           // Rare, interesting mismatch
}