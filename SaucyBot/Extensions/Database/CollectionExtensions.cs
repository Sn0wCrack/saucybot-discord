using Microsoft.EntityFrameworkCore;

namespace SaucyBot.Extensions.Database;

public static class CollectionExtensions
{
    public static void Sync<TEntity, TDto, TKey>(
        this ICollection<TEntity> currentCollection,
        IEnumerable<TDto> incomingCollection,
        Func<TEntity, TKey> currentKeySelector,
        Func<TDto, TKey> incomingKeySelector,
        Action<TEntity, TDto> updateAction,
        Func<TDto, TEntity> createAction,
        DbContext context)
        where TEntity : class
        where TKey : notnull
    {
        // Identify items to delete (exist in DB but not in incoming data)
        var incomingDtos = incomingCollection.ToList();

        var incomingKeys = incomingDtos.Select(incomingKeySelector).ToHashSet();

        var toRemove = currentCollection
            .Where(item => !incomingKeys.Contains(currentKeySelector(item)))
            .ToList();

        foreach (var item in toRemove)
        {
            currentCollection.Remove(item);
        }

        // Identify items to add and update
        foreach (var incomingDto in incomingDtos)
        {
            var incomingKey = incomingKeySelector(incomingDto);

            // If the key is the default value (e.g., 0 for int), treat it as a new item
            var existingEntity = EqualityComparer<TKey>.Default.Equals(incomingKey, default)
                ? null
                : currentCollection.FirstOrDefault(item => currentKeySelector(item)!.Equals(incomingKey));

            if (existingEntity != null)
            {
                // Update properties on the matched existing record
                updateAction(existingEntity, incomingDto);
            }
            else
            {
                // Instantiate and append the new record
                var newEntity = createAction(incomingDto);
                context.Entry(newEntity).State = EntityState.Added;
                currentCollection.Add(newEntity);
            }
        }
    }
}
