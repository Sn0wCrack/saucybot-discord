Privacy Guide and Policy
========================

> Guidance for server owners, administrators, and moderators and end-users in servers using SaucyBot on how SaucyBot reads messages, handles them, and stores data long term.

---

## Storage

SaucyBot, including the public instance, does not store any data long term about the messages it processes.

The message content is briefly stored in a queue system before SaucyBot processes the message.

Once the message is processed, that is either successfully responded to or failed after a certain number of tries, the message is removed from this queue.

SaucyBot keeps the message in a in-memory cache for a short period of time after it has processed it, but the message is removed from cache.

SaucyBot may log the name of the server it is in when providing error infromation.

Logging is only accessible to the operator of the SaucyBot instance.

On the public instance, logs are stored temporarily and are rotated after 7 days, when storage limits are reached, or the bot is restarted, whatever comes first.

Only the internal Discord ID of your server is stored.

If you have configured whitelisted roles for your server then the ID of the roles is stored against the ID of your server as well.

These IDs can only be used to retrieve the names or data associated with them if the server is public or the individual has a Discord account in your server.

## Usage

SaucyBot only uses user data for what is required for it to function.

SaucyBot only holds onto data for a short period of time in an ephemeral manner.

## Privacy

The public instance of SaucyBot does not collect or sell any user data.

There is no intention of using the public instance of SaucyBot to collect or sell any user data.

