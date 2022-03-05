import BaseSite from './BaseSite';
import ProcessResponse from './ProcessResponse';
import got from 'got';
import { version } from '../../package.json';
import { Message, MessageEmbed } from 'discord.js';
import CacheManager from '../CacheManager';

class FurAffinity extends BaseSite {
    identifier = 'FurAffinity';

    pattern =
        /https?:\/\/(www\.)?furaffinity\.net\/(?:view|full)\/(?<id>\d+)/gim;

    async process(
        match: RegExpMatchArray,
        source: Message | null
    ): Promise<ProcessResponse | false> {
        const message: ProcessResponse = {
            embeds: [],
            files: [],
        };

        const response = await this.getSubmission(match.groups.id);

        const embed = new MessageEmbed({
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

    async getSubmission(id: string): Promise<BawkSubmission> {
        const cacheKey = `furaffinity.submission_${id}`;
        const cacheManager = await CacheManager.getInstance();

        const cachedValue = await cacheManager.remember(cacheKey, async () => {
            const results = await got
                .get(`https://bawk.space/fapi/submission/${id}`, {
                    responseType: 'json',
                    headers: {
                        'User-Agent': `SaucyBot/${version}`,
                    },
                })
                .json<BawkSubmission>();

            return Promise.resolve(JSON.stringify(results));
        });

        const results = JSON.parse(cachedValue) as BawkSubmission;

        return Promise.resolve(results);
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
