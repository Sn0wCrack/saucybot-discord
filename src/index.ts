import discord from 'discord.js';
import dotenv from 'dotenv';
import Environment from './Environment';
import Logger from './Logger';
import MessageSender from './MessageSender';
import SiteRunner from './SiteRunner';

dotenv.config();

const client = new discord.Client();

const runner = new SiteRunner();

const sender = new MessageSender();

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
        Logger.error(ex.message);
    }
});

// Capture any unhandled client errors here
client.on('error', async (error) => {
    Logger.error(error);
});

client.on('ready', async () => {
    Logger.info('Ready');

    client.setInterval(async () => {
        await client.user.setActivity(
            `Your Links... | Servers: ${client.guilds.cache.size}`,
            {
                type: 'WATCHING',
            }
        );
    }, 5000);
});

client.login(Environment.get('DISCORD_API_KEY') as string).catch((err) => {
    Logger.error(err.message);
});
