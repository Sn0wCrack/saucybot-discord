import BaseSite from './BaseSite';
import ProcessResponse from './ProcessResponse';
import got from 'got';
import { MessageEmbed } from 'discord.js';

class FurAffinity extends BaseSite {
    name = 'FurAffinity';

    pattern = /https?:\/\/(www\.)?furaffinity\.net\/(?:view|full)\/(?<id>\d+)/;

    async process(match: RegExpMatchArray): Promise<ProcessResponse | false> {
        const message: ProcessResponse = {
            embeds: [],
            files: [],
        };

        const response: Record<string, unknown> = await got
            .get(`https://bawk.space/fapi/submission/${match.groups.id}`, {
                responseType: 'json',
            })
            .json();

        const embed = new MessageEmbed({
            type: 'image',
            title: response.title as string,
            url: match[0],
            color: this.color,
            image: {
                url: response.image_url as string,
            },
            author: {
                name: response.author as string,
                iconURL: response.avatar as string,
            },
        });

        message.embeds.push(embed);

        return Promise.resolve(message);
    }
}

export default FurAffinity;
