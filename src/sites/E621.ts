import got from 'got';
import ProcessResponse from './ProcessResponse';
import { version } from '../../package.json';
import BaseSite from './BaseSite';
import { MessageEmbed } from 'discord.js';
import { DateTime } from 'luxon';

class E621 extends BaseSite {
    name = 'e621';

    pattern = /https?:\/\/(www\.)?e621.net\/posts\/(?<id>\d+)/;

    color = 0x00549e;

    async process(match: RegExpMatchArray): Promise<ProcessResponse | false> {
        const message: ProcessResponse = {
            embeds: [],
            files: [],
        };

        const url = match[0];

        /* eslint-disable  @typescript-eslint/no-explicit-any */
        const response: Record<string, any> = await got
            .get(`https://e621.net/posts/${match.groups.id}.json`, {
                responseType: 'json',
                headers: {
                    'User-Agent': `SaucyBot/${version}`,
                    Referer: 'https://e621.net/',
                },
            })
            .json();

        const embed = new MessageEmbed({
            type: 'image',
            title: `Post #${match.groups.id}`,
            url: url,
            color: this.color,
            timestamp: DateTime.fromISO(response.post.created_at)
                .toUTC()
                .toMillis(),
            description: response.post.description,
            image: {
                url: response.post.file.url,
            },
            author: {
                name: '',
                url: '',
                icon_url: '',
            },
            fields: [
                {
                    name: 'Score',
                    value: response.post.score.total,
                    inline: true,
                },
            ],
        });

        message.embeds.push(embed);

        console.log(response);

        return Promise.resolve(message);
    }
}

export default E621;
