import { GatewayIntentBits, PermissionFlagsBits } from 'discord.js';

export const MAX_FILESIZE = 8_388_119;

export const MAX_EMBEDS_PER_MESSAGE = 4;

export const TWITTER_ICON_URL =
    'https://images-ext-1.discordapp.net/external/bXJWV2Y_F3XSra_kEqIYXAAsI3m1meckfLhYuWzxIfI/https/abs.twimg.com/icons/apple-touch-icon-192x192.png';

export const ARTSTATION_ICON_URL = 'https://i.imgur.com/Wv9JJlS.png';

export const EHENTAI_ICON_URL = 'https://i.imgur.com/PSEmcWO.png';

export const HENTAI_FOUNDRY_ICON_URL = 'https://i.imgur.com/dOECfxG.png';

export const REQUIRED_CHANNEL_PERMISSIONS = [
    PermissionFlagsBits.ReadMessageHistory,
    PermissionFlagsBits.SendMessages,
    PermissionFlagsBits.EmbedLinks,
    PermissionFlagsBits.AttachFiles,
];

export const REQUIRED_GATEWAY_INTENTS = [
    GatewayIntentBits.Guilds,
    GatewayIntentBits.GuildMessages,
    GatewayIntentBits.MessageContent,
];
