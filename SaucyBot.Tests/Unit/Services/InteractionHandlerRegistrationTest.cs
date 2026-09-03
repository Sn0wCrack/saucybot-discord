using System;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using SaucyBot.Commands;
using SaucyBot.Services;
using Xunit;

namespace SaucyBot.Tests.Unit.Services;

public class InteractionHandlerRegistrationTest
{
    [Fact]
    public void ModuleWithoutConditionIsAlwaysRegistered()
    {
        var services = new ServiceCollection().BuildServiceProvider();

        Assert.True(InteractionHandler.ShouldRegister(typeof(UnconditionalTestModule), services));
    }

    [Fact]
    public void ModuleWithConditionDelegatesToShouldRegister()
    {
        var services = new ServiceCollection().BuildServiceProvider();

        Assert.False(InteractionHandler.ShouldRegister(typeof(DisabledTestModule), services));
    }

    private class UnconditionalTestModule : InteractionModuleBase<SocketInteractionContext<SocketInteraction>>
    {
    }

    private class DisabledTestModule : InteractionModuleBase<SocketInteractionContext<SocketInteraction>>, IConditionallyRegisteredModule
    {
        public bool ShouldRegister(IServiceProvider services) => false;
    }
}
