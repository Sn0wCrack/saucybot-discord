using Discord;

namespace SaucyBot.Extensions.Discord;

public static class ComponentBuilderExtension
{
    extension<BuilderT>(BuilderT builder) where BuilderT : IMessageComponentBuilder
    {
        public BuilderT When(bool condition, Action<BuilderT> action)
        {
            if (condition)
            {
                action(builder);
            }

            return builder;
        }

        public BuilderT When(Func<bool> condition, Action<BuilderT> action)
        {
            if (condition())
            {
                action(builder);
            }

            return builder;
        }
    }
}
