namespace SaucyBot.Extensions;

public static class TypeExtensions
{
    extension(Type type)
    {
        /// <summary>
        /// Determines whether the type derives from the specified open generic type definition
        /// (for example, <c>typeof(InteractionModuleBase&lt;&gt;)</c>).
        /// </summary>
        /// <param name="generic">The open generic type definition to search for in the base class hierarchy.</param>
        /// <returns><c>true</c> if any base type is constructed from <paramref name="generic"/>; otherwise <c>false</c>.</returns>
        /// <exception cref="ArgumentException"><paramref name="generic"/> is not a generic type definition.</exception>
        public bool IsSubclassOfOpenGeneric(Type generic)
        {
            ArgumentNullException.ThrowIfNull(generic);

            if (!generic.IsGenericTypeDefinition)
            {
                throw new ArgumentException("The type must be an open generic type definition.", nameof(generic));
            }

            var current = type;

            while (current is not null && current != typeof(object))
            {
                if (current.IsGenericType && current.GetGenericTypeDefinition() == generic)
                {
                    return true;
                }

                current = current.BaseType;
            }

            return false;
        }
    }
}
