namespace SaucyBot.Services;

public record ValidationResult(bool Passed, string? Reason = null)
{
    public static ValidationResult Pass() => new(true);
    public static ValidationResult Fail(string reason) => new(false, reason);
}
