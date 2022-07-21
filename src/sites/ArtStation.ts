import BaseSite from './BaseSite';
import ProcessResponse from './ProcessResponse';
import got from 'got';
import { version } from '../../package.json';
import Environment from '../Environment';
import path from 'path';
import { EmbedBuilder, Message } from 'discord.js';
import { DateTime } from 'luxon';
import { URL } from 'url';
import CacheManager from '../CacheManager';
import { htmlToText } from 'html-to-text';
import { ARTSTATION_ICON_URL } from '../Constants';

class ArtStation extends BaseSite {
    identifier = 'ArtStation';

    pattern = /https?:\/\/(www\.)?artstation\.com\/artwork\/(?<hash>\S+)\/?/gim;

    async process(
        match: RegExpMatchArray,
        /* eslint-disable @typescript-eslint/no-unused-vars */
        source: Message | null
    ): Promise<ProcessResponse | false> {
        const message: ProcessResponse = {
            embeds: [],
            files: [],
        };

        if (!match.groups?.hash) {
            return Promise.resolve(false);
        }

        const response = await this.getProject(match.groups.hash);

        if (!response) {
            return Promise.resolve(false);
        }

        // Discord embeds the first ArtStation item, so if there's only one, ignore the request
        if (response.assets.length == 1) {
            return Promise.resolve(false);
        }

        const limit = Environment.get('ARTSTATION_POST_LIMIT', 8) as number;

        if (response.assets.length - 1 > limit) {
            message.text = `This is part of a ${response.assets.length} image set.`;
        }

        const parsed = new URL(response.cover_url);

        const coverFileName = path.basename(parsed.pathname);

        const assets = response.assets
            .slice(1)
            .filter((asset) => ['image', 'cover'].includes(asset.asset_type))
            .slice(0, limit);

        console.log(response.assets.length, assets.length);

        for (const asset of assets) {
            // If this is the same as the cover, skip it.
            if (asset.image_url.includes(coverFileName)) {
                continue;
            }

            const embed = new EmbedBuilder({
                title: htmlToText(response.title),
                url: response.permalink,
                color: this.color,
                timestamp: DateTime.fromISO(response.published_at)
                    .toUTC()
                    .toMillis(),
                image: {
                    url: asset.image_url,
                },
                author: {
                    name: response.user.full_name,
                    url: response.user.permalink,
                    icon_url: response.user.medium_avatar_url,
                },
                fields: [
                    {
                        name: 'Views',
                        value: response.views_count.toString(),
                        inline: true,
                    },
                    {
                        name: 'Likes',
                        value: response.likes_count.toString(),
                        inline: true,
                    },
                ],
                footer: {
                    iconURL: ARTSTATION_ICON_URL,
                    text: 'ArtStation',
                },
            });

            message.embeds.push(embed);
        }

        return Promise.resolve(message);
    }

    async getProject(hash: string): Promise<ArtStationProject | null> {
        const cacheKey = `artstation.project_${hash}`;
        const cacheManager = await CacheManager.getInstance();

        const cachedValue = await cacheManager.remember(cacheKey, async () => {
            const results = await got
                .get(`https://www.artstation.com/projects/${hash}.json`, {
                    responseType: 'json',
                    headers: {
                        'User-Agent': `SaucyBot/${version}`,
                        Referer: 'https://www.artstation.com/',
                    },
                })
                .json<ArtStationProject>();

            return Promise.resolve(JSON.stringify(results));
        });

        if (!cachedValue) {
            return Promise.resolve(null);
        }

        const results = JSON.parse(cachedValue) as ArtStationProject;

        return Promise.resolve(results);
    }
}

interface ArtStationProject {
    id: number;
    user_id: number;
    title: string;
    description: string;
    cover_url: string;
    permalink: string;
    hash_id: string;
    user: {
        username: string;
        full_name: string;
        permalink: string;
        medium_avatar_url: string;
        large_avatar_url: string;
        small_cover_url: string;
    };
    assets: {
        has_image: boolean;
        has_embedded_player: boolean;
        id: number;
        title: string;
        image_url: string;
        width: number;
        height: number;
        position: number;
        asset_type: 'image' | 'cover' | 'video' | 'video_clip';
    }[];
    likes_count: number;
    views_count: number;
    published_at: string;
}

export default ArtStation;
