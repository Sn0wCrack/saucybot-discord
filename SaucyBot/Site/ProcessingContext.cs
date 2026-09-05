namespace SaucyBot.Site;

public sealed record ProcessingContext(
    CancellationToken CancellationToken,
    bool NsfwAllowed,
    IMessageContext? Message = null,
    ICommandContext? Command = null
);
