import discord, {
    ActivityType,
    GatewayIntentBits,
    PermissionFlagsBits,
    TextChannel,
} from 'discord.js';
import dotenv from 'dotenv';
import Environment from './Environment';
import Logger from './Logger';
import MessageSender from './MessageSender';
import SiteRunner from './SiteRunner';
import * as Sentry from '@sentry/node';

dotenv.config();

const intents = [
    GatewayIntentBits.Guilds,
    GatewayIntentBits.GuildMessages,
    GatewayIntentBits.MessageContent,
];

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

    // If the message is surrounded by < > it'll be ignored.
    if (message.content.match(/(<|\|\|)(?!@|#|:|a:).*(>|\|\|)/)) {
        return;
    }

    const permissions = message.guild?.members?.me?.permissionsIn(
        message.channel as TextChannel
    );

    if (
        !permissions?.has(PermissionFlagsBits.ReadMessageHistory) ||
        !permissions?.has(PermissionFlagsBits.SendMessages) ||
        !permissions?.has(PermissionFlagsBits.EmbedLinks) ||
        !permissions?.has(PermissionFlagsBits.AttachFiles)
    ) {
        return;
    }

    try {
        const responses = await runner.process(message.content);

        // If the response is false, then we didn't find anything.
        if (responses === false) {
            return;
        }

        // In order to process our messages simultaneously we build up an array of Promises.
        // We then run Promise.all over this array to roughly execute
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
                            await waitMessage.delete();
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

        await Promise.all(playbook);
    } catch (ex) {
        Sentry.captureException(ex);
        Logger.error(ex?.message, identifier);
    }
});

client.on('interactionCreate', async (interaction) => {
    if (!interaction.isChatInputCommand()) {
        return;
    }

    const { commandName } = interaction;

    if (commandName !== 'sauce') {
        return;
    }

    const permissions = interaction.guild?.members?.me?.permissionsIn(
        interaction.channel as TextChannel
    );

    if (
        !permissions?.has(PermissionFlagsBits.ReadMessageHistory) ||
        !permissions?.has(PermissionFlagsBits.SendMessages) ||
        !permissions?.has(PermissionFlagsBits.EmbedLinks) ||
        !permissions?.has(PermissionFlagsBits.AttachFiles)
    ) {
        return;
    }

    await interaction.deferReply();

    try {
        const url = interaction.options.getString('url');

        if (!url) {
            await interaction.editReply('No URL appeared to be provided');
            return;
        }

        const responses = await runner.process(url);

        // If the response is false, then we didn't find anything.
        if (responses === false) {
            await interaction.editReply('Provided URL cannot be sauced');
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
                        await interaction.editReply(
                            'Provided URL cannot be sauced'
                        );
                        return resolve();
                    }

                    await sender.send(interaction, processed);

                    return resolve();
                });

                playbook.push(promise);
            }
        }

        await Promise.all(playbook);
    } catch (ex) {
        await interaction.editReply('Provided URL cannot be sauced');
        Sentry.captureException(ex);
        Logger.error(ex?.message, identifier);
    }
});

// Capture any unhandled client errors here
client.on('error', async (error) => {
    Logger.error(error, identifier);
});

client.on('warn', async (warn) => {
    Logger.warn(warn, identifier);
});

// Only register debug info
if (Environment.isDevelopment()) {
    client.on('debug', async (debug) => {
        Logger.debug(debug, identifier);
    });
}

client.once('ready', async () => {
    Logger.info('Ready', identifier);

    setInterval(async () => {
        let guilds = 0;

        if (client.shard !== null) {
            try {
                const results = await client.shard.broadcastEval(
                    (client) => client.guilds.cache.size
                );

                guilds = results.reduce(
                    (acc: number, guildCount: number) => acc + guildCount,
                    0
                ) as number;
            } catch (ex) {
                Logger.error(ex?.message, identifier);
            }
        } else {
            guilds = client.guilds.cache.size;
        }

        client.user?.setActivity(`Your Links... | Servers: ${guilds}`, {
            type: ActivityType.Watching,
        });
    }, 5000);
});

client.login(Environment.get('DISCORD_API_KEY') as string).catch((err) => {
    Logger.error(err.message, identifier);
});
