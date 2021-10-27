import got from 'got';
import ProcessResponse from './ProcessResponse';
import { version } from '../../package.json';
import BaseSite from './BaseSite';
import { EmbedField, MessageEmbed } from 'discord.js';
import { DateTime } from 'luxon';

class E621 extends BaseSite {
    identifier = 'E621';

    pattern = /https?:\/\/(www\.)?e621.net\/posts\/(?<id>\d+)/i;

    color = 0x00549e;

    async process(match: RegExpMatchArray): Promise<ProcessResponse | false> {
        const message: ProcessResponse = {
            embeds: [],
            files: [],
        };

        const url = match[0];

        /* eslint-disable  @typescript-eslint/no-explicit-any */
        const response: E621Post = await got
            .get(`https://e621.net/posts/${match.groups.id}.json`, {
                responseType: 'json',
                headers: {
                    'User-Agent': `SaucyBot/${version}`,
                    Referer: 'https://e621.net/',
                },
            })
            .json();

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
