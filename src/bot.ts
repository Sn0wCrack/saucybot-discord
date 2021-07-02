import discord, { Intents } from 'discord.js';
import dotenv from 'dotenv';
import Environment from './Environment';
import Logger from './Logger';
import MessageSender from './MessageSender';
import SiteRunner from './SiteRunner';

dotenv.config();

const client = new discord.Client({
    intents: Intents.NON_PRIVILEGED,
    allowedMentions: { repliedUser: false },
});

const runner = new SiteRunner();

const sender = new MessageSender();

const identifier = `Shard ${client.shard?.ids?.[0] ?? 0}`;

client.on('message', async (message) => {
    // If message is from Bot, then ignore it.
    if (message.author == client.user) {
        return;
    }

    // If the message is sorrounded by < > it'll be ignored.
    if (message.content.match(/(<|\|\|)(?!@|#|:|a:).*(>|\|\|)/)) {
        return;
    }

    try {
        const response = await runner.process(message);

        // If the response is false, then we didn't find anything.
        if (response === false) {
            return;
        }

        Logger.info(
            `Matched message "${response.match[0]}" to site ${response.site.identifier}`,
            identifier
        );

        // TODO: When discord.js releases version 13, change this to be an inline-reply that doesn't ping
        const waitMessage = await message.reply(
            `Matched link to ${response.site.identifier}, please wait...`
        );

        const processed = await response.site.process(response.match);

        // If we failed to process the image, remove the wait message and return
        if (processed === false) {
            waitMessage.delete();
            return;
        }

        await sender.send(message, processed);

        await waitMessage.delete();
    } catch (ex) {
        Logger.error(ex.message, identifier);
    }
});

// Capture any unhandled client errors here
client.on('error', async (error) => {
    Logger.error(error, identifier);
});

client.on('ready', async () => {
    Logger.info('Ready', identifier);

    client.setInterval(async () => {
        let guilds = 0;

        if (client.shard !== null) {
            const results = await client.shard.fetchClientValues(
                'guilds.cache.size'
            );

            guilds = results.reduce((acc, guildCount) => acc + guildCount, 0);
        } else {
            guilds = client.guilds.cache.size;
        }

        client.user.setActivity(`Your Links... | Servers: ${guilds}`, {
            type: 'WATCHING',
        });
    }, 5000);
});

client.login(Environment.get('DISCORD_API_KEY') as string).catch((err) => {
    Logger.error(err.message, identifier);
});
