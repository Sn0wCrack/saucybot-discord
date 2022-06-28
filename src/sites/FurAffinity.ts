import BaseSite from './BaseSite';
import ProcessResponse from './ProcessResponse';
import got from 'got';
import { version } from '../../package.json';
import { Message, MessageEmbed } from 'discord.js';
import CacheManager from '../CacheManager';
import { processDescription } from '../Helpers';

class FurAffinity extends BaseSite {
    identifier = 'FurAffinity';

    pattern =
        /https?:\/\/(www\.)?furaffinity\.net\/(?:view|full)\/(?<id>\d+)\/?/gim;

    async process(
        match: RegExpMatchArray,
        /* eslint-disable @typescript-eslint/no-unused-vars */
        source: Message | null
    ): Promise<ProcessResponse | false> {
        const message: ProcessResponse = {
            embeds: [],
            files: [],
        };

        if (!match.groups?.id) {
            return Promise.resolve(false);
        }

        const response = await this.getSubmission(match.groups.id);

        if (!response) {
            return Promise.resolve(false);
        }

        const embed = new MessageEmbed({
            title: response.title,
            url: match[0],
            color: this.color,
            timestamp: response.posted_at,
            description: processDescription(response.description),
            image: {
                url: response.download,
            },
            author: {
                name: response.profile_name,
                url: response.profile,
                iconURL: response.avatar,
            },
            fields: [
                {
                    name: 'Views',
                    value: response.views ?? '0',
                    inline: true,
                },
                {
                    name: 'Favorites',
                    value: response.favorites ?? '0',
                    inline: true,
                },
                {
                    name: 'Comments',
                    value: response.comments ?? '0',
                    inline: true,
                },
            ],
            footer: {
                text: 'FurAffinity',
            },
        });

        message.embeds.push(embed);

        return Promise.resolve(message);
    }

    async getSubmission(id: string): Promise<FAExportSubmission | null> {
        const cacheKey = `furaffinity.submission_${id}`;
        const cacheManager = await CacheManager.getInstance();

        const cachedValue = await cacheManager.remember(cacheKey, async () => {
            const results = await got
                .get(`https://faexport.spangle.org.uk/submission/${id}.json`, {
                    responseType: 'json',
                    headers: {
                        'User-Agent': `SaucyBot/${version}`,
                    },
                })
                .json<FAExportSubmission>();

            return Promise.resolve(JSON.stringify(results));
        });

        if (!cachedValue) {
            return Promise.resolve(null);
        }

        const results = JSON.parse(cachedValue) as FAExportSubmission;

        return Promise.resolve(results);
    }
}

interface FAExportSubmission {
    title: string;
    description: string;
    name: string;
    profile: string;
    profile_name: string;
    avatar: string;
    download: string;
    full: string;
    posted_at: string;
    views: string;
    comments: string;
    favorites: string;
}

export default FurAffinity;
