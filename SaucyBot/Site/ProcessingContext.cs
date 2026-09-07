namespace SaucyBot.Site;

public sealed record ProcessingContext(
    bool NsfwAllowed,
    IMessageContext? Message = null,
    ICommandContext? Command = null,
    CancellationToken CancellationToken = default
);
