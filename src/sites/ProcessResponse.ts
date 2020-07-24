import { FileOptions, MessageEmbed } from "discord.js";

export interface ProcessResponse {
    embeds: Array<MessageEmbed>,
    files: Array<FileOptions>,
    text?: string,
}