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

        sender.send(message, response);
    } catch (ex) {
        Logger.error(ex.message);
    }
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
