import discord from 'discord.js';
import dotenv from 'dotenv';
import Environment from './Environment';
import SiteRunner from './SiteRunner';

dotenv.config();

const client = new discord.Client();

const runner = new SiteRunner();

client.on('message', async (message) => {
    // If message is from Bot, then ignore it.
    if (message.author == client.user) {
        return;
    }

    // If the message is sorrounded by < > it'll be ignored.
    if (message.content.match(/(<|\|\|)(?!@|#|:|a:).*(>|\|\|)/)) {
        return;
    }

    const response = await runner.process(message);

    // If the response is false, then we didn't find anything.
    if (response === false) {
        return;
    }

    if (response.embeds.length <= 1) {
        message.channel.send({
            embed: response.embeds.find((x) => x !== undefined), // This safely gets the first element of our array
            files: response.files,
            content: response.text,
        });
    } else {
        if (response.text) {
            message.channel.send(response.text);
        }

        message.channel.send(response.embeds);
    }
});

client.on('ready', async () => {
    console.log('Ready');

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
    console.error(err.message);
});
