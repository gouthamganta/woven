namespace WovenBackend.Data.Entities.Games;

public enum GameSessionType
{
    KNOW_ME,
    RED_GREEN_FLAG,
    FIRST_DATE_DRAFT
}

public enum GameSessionStatus
{
    PENDING,
    ACTIVE,
    COMPLETED,
    EXPIRED,
    REJECTED
}
