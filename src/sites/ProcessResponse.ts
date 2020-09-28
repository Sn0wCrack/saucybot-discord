import { FileOptions, MessageEmbed } from 'discord.js';

export default interface ProcessResponse {
    embeds: Array<MessageEmbed>;
    files: Array<FileOptions>;
    text?: string;
}
