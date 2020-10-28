import {
    APIMessageContentResolvable,
    Message,
    MessageOptions,
    MessageAdditions,
    StringResolvable,
    FileOptions,
} from 'discord.js';
import { MAX_FILESIZE } from './Constants';
import ProcessResponse from './sites/ProcessResponse';

class MessageSender {
    async send(recieved: Message, response: ProcessResponse): Promise<void> {
        let messages: MessageTypes = [];

        // Pattern matching on a budget
        switch (true) {
            case response.embeds.length > 1:
                messages = await this.handleMultipleEmbeds(response);
                break;
            case response.embeds.length == 1:
                messages = await this.handleSingleEmbed(response);
                break;
            case response.files.length >= 1:
                messages = await this.handleFiles(response);
                break;
            default:
                messages = [response.text];
                break;
        }

        for (const message of messages) {
            try {
                await recieved.channel.send(message);
            } catch (ex) {
                console.error(ex);
            }
        }

        return Promise.resolve();
    }

    /**
     * When no embeds are detected but there are files
     */
    private async handleFiles(
        response: ProcessResponse
    ): Promise<MessageTypes> {
        const messages: MessageTypes = [];

        if (response.text) {
            messages.push(response.text);
        }

        // If we only have a single file, may as well push it and bail early to save cycles
        if (response.files.length == 1) {
            messages.push({
                files: response.files,
            });

            return messages;
        }

        // We split up file messages into groups of files under the file size limit
        // This is faster than sending the images back one-by-one

        const segments: FileOptions[][] = [];

        for (const file of response.files) {
            if (segments.length == 0) {
                segments.push([file]);

                continue;
            }

            for (const index in segments) {
                const totalSize: number = segments[index].reduce(
                    (total, item) => {
                        const attachment = item.attachment as Buffer;
                        return total + attachment.length;
                    },
                    0
                );

                const attachment = file.attachment as Buffer;

                // If we're about tot reach maximum message size, move onto the next index
                // If we've reached the end of the array, add a new item to the array as well
                if (attachment.length + totalSize >= MAX_FILESIZE) {
                    // If we're on the last index, we push to a new entry and continue the loop
                    if (parseInt(index) + 1 == segments.length) {
                        segments.push([file]);
                    }

                    continue;
                }

                // If we've not reached the maximum message size, add to the current index
                segments[index].push(file);
            }
        }

        for (const files of segments) {
            messages.push({
                files: files,
            });
        }

        return Promise.resolve(messages);
    }

    /**
     * When only a single embed is detected
     */
    private async handleSingleEmbed(
        response: ProcessResponse
    ): Promise<MessageTypes> {
        const messages: MessageTypes = [];

        const embed = response.embeds.find((x) => x !== undefined);
        // Map the embed attachment files down to their names
        const files = embed.files.map((item) => {
            let filename = '';

            if (typeof item == 'object') {
                filename = item.name;
            }

            if (typeof item == 'string') {
                filename = item;
            }

            return filename.replace('attachment://', '');
        });

        messages.push({
            embed: embed,
            files: response.files.filter((item) => !files.includes(item.name)), // Only send attachments that are related to this embed
            content: response.text,
        });

        return Promise.resolve(messages);
    }

    /**
     * When multiple embeds are detected
     */
    private async handleMultipleEmbeds(
        response: ProcessResponse
    ): Promise<MessageTypes> {
        const messages: MessageTypes = [];

        if (response.text) {
            messages.push(response.text);
        }

        messages.push(...response.embeds);

        return Promise.resolve(messages);
    }
}

/**
 * An amalgamation of all types that can be used to send messages to a channel
 */
type MessageTypes = (
    | MessageOptions
    | MessageAdditions
    | APIMessageContentResolvable
    | StringResolvable
)[];

export default MessageSender;
