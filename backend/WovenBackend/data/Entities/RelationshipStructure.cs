namespace WovenBackend.Data.Entities;

public enum RelationshipStructure
{
    MONO_ONLY = 1,      // Only interested in monogamous relationships
    NONMONO_ONLY = 2,   // Only interested in non-monogamous relationships
    OPEN = 3            // Open to both
}