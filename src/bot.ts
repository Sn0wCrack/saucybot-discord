import discord, { Intents } from 'discord.js';
import dotenv from 'dotenv';
import Environment from './Environment';
import Logger from './Logger';
import MessageSender from './MessageSender';
import SiteRunner from './SiteRunner';

dotenv.config();

const intents = new Intents([
    Intents.FLAGS.GUILDS,
    Intents.FLAGS.GUILD_MESSAGES,
]);

const client = new discord.Client({
    intents: intents,
    allowedMentions: { repliedUser: false },
});

const runner = new SiteRunner();

const sender = new MessageSender();

const identifier = `Shard ${client.shard?.ids?.[0] ?? 0}`;

client.on('messageCreate', async (message) => {
    // If message is from Bot, then ignore it.
    if (message.author == client.user) {
        return;
    }

    // If the message is sorrounded by < > it'll be ignored.
    if (message.content.match(/(<|\|\|)(?!@|#|:|a:).*(>|\|\|)/)) {
        return;
    }

    try {
        const responses = await runner.process(message.content);

        // If the response is false, then we didn't find anything.
        if (responses === false) {
            return;
        }

        // TODO: Find a way to turn this into a "Promise.all" so we can match and process the links at the same time
        for (const response of responses) {
            for (const match of response.matches) {
                Logger.info(
                    `Matched link "${match[0]}" to site ${response.site.identifier}`,
                    identifier
                );

                const waitMessage = await message.reply(
                    `Matched link to ${response.site.identifier}, please wait...`
                );

                // Always ensure, even if there's an exception from processing
                // that we delete our waiting message
                try {
                    const processed = await response.site.process(
                        match,
                        message
                    );

                    // If we failed to process the image, remove the wait message and return
                    if (processed === false) {
                        waitMessage.delete();
                        return;
                    }

                    await sender.send(message, processed);

                    await waitMessage.delete();
                } catch (ex) {
                    await waitMessage.delete();
                    throw ex;
                }
            }
        }
    } catch (ex) {
        Logger.error(ex?.message, identifier);
    }
});

client.on('interactionCreate', async (interaction) => {
    if (!interaction.isCommand()) {
        return;
    }

    const { commandName } = interaction;

    if (commandName !== 'sauce') {
        return;
    }

    interaction.deferReply();

    try {
        const responses = await runner.process(
            interaction.options.getString('url')
        );

        // If the response is false, then we didn't find anything.
        if (responses === false) {
            interaction.editReply('Provided URL cannot be sauced');
            return;
        }

        for (const response of responses) {
            for (const match of response.matches) {
                Logger.info(
                    `Matched message "${match[0]}" to site ${response.site.identifier}`,
                    identifier
                );

                const processed = await response.site.process(match, null);

                if (!processed) {
                    interaction.editReply('Provided URL cannot be sauced');
                    return;
                }

                await sender.send(interaction, processed);
            }
        }
    } catch (ex) {
        interaction.editReply('Provided URL cannot be sauced');
        Logger.error(ex?.message, identifier);
    }
});

// Capture any unhandled client errors here
client.on('error', async (error) => {
    Logger.error(error, identifier);
});

client.once('ready', async () => {
    Logger.info('Ready', identifier);

    setInterval(async () => {
        let guilds = 0;

        if (client.shard !== null) {
            const results = await client.shard.fetchClientValues(
                'guilds.cache.size'
            );

            guilds = results.reduce(
                (acc: number, guildCount: number) => acc + guildCount,
                0
            ) as number;
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
