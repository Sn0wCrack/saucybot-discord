import discord, { Intents } from 'discord.js';
import dotenv from 'dotenv';
import Environment from './Environment';
import Logger from './Logger';
import MessageSender from './MessageSender';
import SiteRunner from './SiteRunner';
import * as Sentry from '@sentry/node';

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

Sentry.init({
    integrations: [new Sentry.Integrations.Http()],
    initialScope: {
        contexts: {
            shard: {
                identifier: identifier,
                master: false,
            },
        },
    },
});

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

        // In order to process our messages simulataneously we build up an array of Promises.
        // We then run Promse.all over this array to roughly execute
        const playbook: Array<Promise<void>> = [];

        for (const response of responses) {
            for (const match of response.matches) {
                const promise = new Promise<void>(async (resolve) => {
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
                            return resolve();
                        }

                        await sender.send(message, processed);

                        await waitMessage.delete();
                    } catch (ex) {
                        await waitMessage.delete();
                        Sentry.captureException(ex);
                        Logger.error(ex?.message, identifier);
                    }

                    return resolve();
                });

                playbook.push(promise);
            }
        }

        Promise.all(playbook);
    } catch (ex) {
        Sentry.captureException(ex);
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

        const playbook: Array<Promise<void>> = [];

        for (const response of responses) {
            for (const match of response.matches) {
                const promise = new Promise<void>(async (resolve) => {
                    Logger.info(
                        `Matched message "${match[0]}" to site ${response.site.identifier}`,
                        identifier
                    );

                    const processed = await response.site.process(match, null);

                    if (processed === false) {
                        interaction.editReply('Provided URL cannot be sauced');
                        return resolve();
                    }

                    await sender.send(interaction, processed);

                    return resolve();
                });

                playbook.push(promise);
            }
        }

        Promise.all(playbook);
    } catch (ex) {
        interaction.editReply('Provided URL cannot be sauced');
        Sentry.captureException(ex);
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
