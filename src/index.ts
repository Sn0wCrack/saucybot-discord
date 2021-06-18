import { ShardingManager } from 'discord.js';
import dotenv from 'dotenv';
import { join } from 'path';
import Environment from './Environment';
import Logger from './Logger';

dotenv.config();

const manager = new ShardingManager(join(__dirname, 'bot.js'), {
    totalShards: 'auto',
    token: Environment.get('DISCORD_API_KEY') as string,
});

manager.on('shardCreate', (shard) =>
    Logger.info(`Launched Shard ${shard.id}`, 'Manager')
);
manager.spawn();
