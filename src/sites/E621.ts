import got from 'got';
import ProcessResponse from './ProcessResponse';
import { version } from '../../package.json';
import BaseSite from './BaseSite';
import { EmbedField, Message, MessageEmbed } from 'discord.js';
import { DateTime } from 'luxon';
import CacheManager from '../CacheManager';

class E621 extends BaseSite {
    identifier = 'E621';

    pattern = /https?:\/\/(www\.)?e621.net\/posts\/(?<id>\d+)/gim;

    color = 0x00549e;

    async process(
        match: RegExpMatchArray,
        source: Message | null
    ): Promise<ProcessResponse | false> {
        const message: ProcessResponse = {
            embeds: [],
            files: [],
        };

        const url = match[0];

        const response: E621Post = await this.getPost(match.groups.id);

        // If our meta tags contain "animated", then we prefix the post with "[ANIM]"
        // This indicates similar to the site itself the content is animated
        // Mostly to let the users know the content should be animated if it's not
        const prefix: string = response.post.tags.meta.includes('animated')
            ? '[ANIM]'
            : '';

        let imageUrl: string = response.post.file.url;

        // TODO: When discord adds video embeds, revist this
        // If we're a webm or swf file, find the best fit for the image to embed
        if (['webm', 'swf'].includes(response.post.file.ext)) {
            imageUrl = response.post.sample.has
                ? response.post.sample.url
                : response.post.preview.url;
        }

        const fields: EmbedField[] = [];

        // If we found the Artist in the tags, add their tag into the embed fields for credit
        if (response.post.tags.artist.length >= 1) {
            // Format them into Title Case from snake_case
            const value: string = response.post.tags.artist
                .map((tag: string) => {
                    return tag
                        .replace(
                            /([a-z])([A-Z])/g,
                            function (all, first, second) {
                                return first + ' ' + second;
                            }
                        )
                        .toLowerCase()
                        .replace(
                            /([ -_]|^)(.)/g,
                            function (all, first, second) {
                                return (
                                    (first ? ' ' : '') + second.toUpperCase()
                                );
                            }
                        );
                })
                .join(', ');

            fields.push({
                name:
                    'Artist' +
                    (response.post.tags.artist.length !== 1 ? 's' : ''),
                value: value,
                inline: true,
            });
        }

        fields.push({
            name: 'Score',
            value: response.post.score.total.toString(),
            inline: true,
        });

        const embed = new MessageEmbed({
            title: `${prefix} Post #${match.groups.id}`,
            url: url,
            color: this.color,
            timestamp: DateTime.fromISO(response.post.created_at)
                .toUTC()
                .toMillis(),
            description: response.post.description,
            image: {
                url: imageUrl,
            },
            fields: fields,
        });

        message.embeds.push(embed);

        return Promise.resolve(message);
    }

    async getPost(id: string): Promise<E621Post> {
        const cacheKey = `e621.post_${id}`;
        const cacheManager = await CacheManager.getInstance();

        const cachedValue = await cacheManager.remember(cacheKey, async () => {
            const results = await got.get(`https://e621.net/posts/${id}.json`, {
                responseType: 'json',
                headers: {
                    'User-Agent': `SaucyBot/${version}`,
                    Referer: 'https://e621.net/',
                },
            });

            return Promise.resolve(JSON.stringify(results));
        });

        const results = JSON.parse(cachedValue) as E621Post;

        return Promise.resolve(results);
    }
}

interface E621Post {
    post: {
        id: number;
        created_at: string;
        updated_at: string;
        file: {
            width: number;
            height: number;
            ext: string;
            size: number;
            md5: string;
            url: string;
        };
        preview: {
            width: number;
            height: number;
            url: string;
        };
        sample: {
            has: boolean;
            width: number;
            height: number;
            url: string;
        };
        score: {
            up: number;
            down: number;
            total: number;
        };
        tags: {
            artist: string[] | null;
            meta: string[] | null;
        };
        change_seq: number;
        rating: string;
        fav_count: string;
        approver_id: number | null;
        uploader_id: number;
        description: string;
        comment_count: number;
        is_favorited: boolean;
        has_notes: false;
        duration: number | null;
    };
}

export default E621;
