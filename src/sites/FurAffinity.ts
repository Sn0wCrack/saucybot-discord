import BaseSite from './BaseSite';
import ProcessResponse from './ProcessResponse';
import got from 'got';
import { MessageEmbed } from 'discord.js';

class FurAffinity extends BaseSite {
    identifier = 'FurAffinity';

    pattern = /https?:\/\/(www\.)?furaffinity\.net\/(?:view|full)\/(?<id>\d+)/;

    async process(match: RegExpMatchArray): Promise<ProcessResponse | false> {
        const message: ProcessResponse = {
            embeds: [],
            files: [],
        };

        const response: BawkSubmission = await got
            .get(`https://bawk.space/fapi/submission/${match.groups.id}`, {
                responseType: 'json',
            })
            .json();

        const embed = new MessageEmbed({
            type: 'image',
            title: response.title,
            url: match[0],
            color: this.color,
            image: {
                url: response.image_url,
            },
            author: {
                name: response.author,
                iconURL: response.avatar,
            },
        });

        message.embeds.push(embed);

        return Promise.resolve(message);
    }
}

interface BawkSubmission {
    author: string;
    avatar: string;
    image_url: string;
    rating: string;
    title: string;
}

export default FurAffinity;
