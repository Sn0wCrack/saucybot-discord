namespace SaucyBot.Commands;

public interface IConditionallyRegisteredModule
{
    bool ShouldRegister(IServiceProvider services);
}
