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
        let messages: MessageTypes;

        if (response.embeds.length > 1) {
            messages = await this.handleMultipleEmbeds(response);
        }

        if (response.embeds.length == 1) {
            messages = await this.handleSingleEmbed(response);
        }

        if (response.files.length > 1) {
            messages = await this.handleMultipleFiles(response);
        }

        for (const message of messages) {
            await recieved.channel.send(message);
        }

        /**
        if (response.embeds.length <= 1) {
            recieved.channel
                .send({
                    embed: response.embeds.find((x) => x !== undefined), // This safely gets the first element of our array
                    files: response.files,
                    content: response.text,
                })
                .catch(console.error);
        } else {
            if (response.text) {
                recieved.channel.send(response.text).catch(console.error);
            }

            recieved.channel.send(response.embeds).catch(console.error);
        }
        */

        return Promise.resolve();
    }

    /**
     * When no embeds are detected but there are multiple files
     */
    async handleMultipleFiles(
        response: ProcessResponse
    ): Promise<MessageTypes> {
        const messages: MessageTypes = [];

        if (response.text) {
            messages.push(response.text);
        }

        // We split up file messages into groups of files under the file size limit
        // This is faster than sending the images back one-by-one

        const splitFiles: FileOptions[][] = [];

        for (const file of response.files) {
            if (splitFiles.length == 0) {
                splitFiles.push([file]);

                continue;
            }

            for (const index in splitFiles) {
                const totalSize: number = splitFiles[index].reduce(
                    (total, item) => total + (item.attachment as Buffer).length,
                    0
                );

                // If we're about tot reach maximum message size, move onto the next index
                // If we've reached the end of the array, add a new item to the array as well
                if (
                    (file.attachment as Buffer).length + totalSize >=
                    MAX_FILESIZE
                ) {
                    // If we're on the last index, we push to a new array
                    if (parseInt(index) + 1 == splitFiles.length) {
                        splitFiles.push([file]);
                    }

                    continue;
                }

                // If we've not reached the maximum message size, add to the current index
                splitFiles[index].push(file);
            }
        }

        for (const files of splitFiles) {
            messages.push({
                files: files,
            });
        }

        return Promise.resolve(messages);
    }

    /**
     * When only a single embed is detected
     */
    async handleSingleEmbed(response: ProcessResponse): Promise<MessageTypes> {
        const messages: MessageTypes = [];

        messages.push({
            embed: response.embeds.find((x) => x !== undefined),
            content: response.text,
        });

        return Promise.resolve(messages);
    }

    /**
     * When multiple embeds are detected
     */
    async handleMultipleEmbeds(
        response: ProcessResponse
    ): Promise<MessageTypes> {
        const messages: MessageTypes = [];

        if (response.text) {
            messages.push(response.text);
        }

        messages.concat(response.embeds);

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
