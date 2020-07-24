import discord from 'discord.js';
import dotenv from 'dotenv';
import Environment from './Environment';
import SiteRunner from './SiteRunner';

dotenv.config();

const client = new discord.Client();

client.on('message', async (message) => {
    // If message is from Bot, then ignore it.
    if (message.author == client.user) {
        return;
    }

    // If the message is sorrounded by < > it'll be ignored.
    if (message.content.match(/(<|\|\|)(?!@|#|:|a:).*(>|\|\|)/)) {
        return;
    }

    const response = await SiteRunner.process(message.content);

    // If the response is false, then we didn't find anything.
    if (response === false) {
        return;
    }

    if (response.embeds.length <= 1) {
        message.channel.send({
            embed: response.embeds.find(x => x !== undefined), // This safely gets the first element of our array
            files: response.files,
            content: response.text
        });
    } else {
        if (response.text) {
            message.channel.send(response.text);
        }

        for (const embed of response.embeds) {
            message.channel.send(embed);
        }
    }
});

client
    .login(Environment.get('DISCORD_API_KEY'))
    .catch((err) => {
        console.error(err.message);
    });