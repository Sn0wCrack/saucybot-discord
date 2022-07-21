import { AttachmentPayload, EmbedBuilder } from 'discord.js';

export default interface ProcessResponse {
    embeds: Array<EmbedBuilder>;
    files: Array<AttachmentPayload>;
    text?: string;
}
