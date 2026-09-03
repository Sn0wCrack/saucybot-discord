using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using SaucyBot.Commands;
using SaucyBot.Extensions;

namespace SaucyBot.Services;

public sealed class InteractionHandler
{
    private readonly ILogger<InteractionHandler> _logger;
    private readonly IServiceProvider _services;

    private BaseSocketClient? _client;
    private InteractionService? _interactionService;
    private IReadOnlyList<Type>? _modulesToRegister;

    public event Func<LogMessage, Task>? Log;

    public InteractionHandler(ILogger<InteractionHandler> logger, IServiceProvider services)
    {
        _logger = logger;
        _services = services;
    }

    public void Initialize(BaseSocketClient client)
    {
        _client = client;

        var interactionService = new InteractionService(client, new InteractionServiceConfig
        {
            AutoServiceScopes = true
        });

        if (Log is not null)
        {
            interactionService.Log += Log;
        }

        _interactionService = interactionService;
    }

    public async Task RegisterAsync()
    {
        if (_interactionService is null)
        {
            throw new InvalidOperationException("InteractionHandler has not been initialized.");
        }

        try
        {
            using var scope = _services.CreateScope();
            var services = scope.ServiceProvider;

            _modulesToRegister ??= GetInteractionModuleTypes()
                .Where(type => ShouldRegister(type, services))
                .ToList();

            foreach (var moduleType in _modulesToRegister)
            {
                _logger.LogDebug("Attempting to load interaction module: {Module}", moduleType.Name);

                if (_interactionService.Modules.Any(module => module.Name == moduleType.Name))
                {
                    _logger.LogDebug("Interaction module '{Module}' is already loaded, skipping...", moduleType.Name);
                    continue;
                }

                _logger.LogInformation("Registering interaction module: {Module}", moduleType.Name);

                await _interactionService.AddModuleAsync(moduleType, _services);
            }

            await _interactionService.RegisterCommandsGloballyAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create global application commands with message: {Message}", ex.Message);
        }
    }

    private static IEnumerable<Type> GetInteractionModuleTypes() =>
        Assembly
            .GetExecutingAssembly()
            .GetTypes()
            .Where(t =>
                t is { Namespace: "SaucyBot.Commands", IsAbstract: false } &&
                t.IsSubclassOfOpenGeneric(typeof(InteractionModuleBase<>))
            );

    internal static bool ShouldRegister(Type moduleType, IServiceProvider services)
    {
        if (!typeof(IConditionallyRegisteredModule).IsAssignableFrom(moduleType))
        {
            return true;
        }

        var command = (IConditionallyRegisteredModule)ActivatorUtilities.CreateInstance(services, moduleType);

        return command.ShouldRegister(services);
    }

    public async Task ExecuteAsync(SocketInteraction interaction, IServiceProvider services)
    {
        if (_interactionService is null || _client is null)
        {
            throw new InvalidOperationException("InteractionHandler has not been initialized.");
        }

        IInteractionContext context = _client switch
        {
            DiscordSocketClient socketClient => new SocketInteractionContext(socketClient, interaction),
            DiscordShardedClient shardedClient => new ShardedInteractionContext(shardedClient, interaction),
            _ => throw new InvalidOperationException($"Unsupported client type: {_client.GetType().Name}")
        };

        await _interactionService.ExecuteCommandAsync(context, services);
    }
}
