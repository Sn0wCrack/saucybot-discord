import BaseSite from './BaseSite';
import ProcessResponse from './ProcessResponse';
import { TwitterClient } from 'twitter-api-client';
import Environment from '../Environment';
import { MAX_FILESIZE } from '../Constants';
import { FileOptions } from 'discord.js';
import path from 'path';
import got from 'got';

class TwitterVideo extends BaseSite {
    identifier = 'Twitter Video';

    pattern =
        /https?:\/\/(www\.)?twitter\.com\/(?<user>.*)\/status\/(?<id>\S+)(\?\=.*)?/i;

    color = 0x1da1f2;

    api: TwitterClient;

    constructor() {
        super();

        this.api = new TwitterClient({
            apiKey: Environment.get('TWITTER_API_KEY') as string,
            apiSecret: Environment.get('TWITTER_API_SECRET') as string,
            accessToken: Environment.get('TWITTER_ACCESS_TOKEN') as string,
            accessTokenSecret: Environment.get(
                'TWITTER_ACCESS_SECRET'
            ) as string,
        });
    }

    async process(match: RegExpMatchArray): Promise<ProcessResponse | false> {
        const results = await this.api.tweets.statusesShow({
            id: match.groups.id,
            include_entities: true,
            trim_user: false,
            tweet_mode: 'extended',
        });

        const media = results?.extended_entities?.media;

        if (!media) {
            return false;
        }

        const video = media.find((item) =>
            ['video', 'animated_gif'].includes(item.type)
        );

        if (!video) {
            return false;
        }

        const variants = video.video_info.variants
            .filter((item) => item?.bitrate !== null)
            .sort((a, b) => b.bitrate - a.bitrate)
            .map((item) => item.url);

        const variant = await this.determineHighestQuality(variants);

        if (!variant) {
            return false;
        }

        const videoFile = await this.getFile(variant);

        const message: ProcessResponse = {
            embeds: [],
            files: [videoFile],
        };

        return message;
    }

    /**
     * Determines the highest quality of a video that can be posted to Discord inside of its size limit
     *
     * @param urls a list of urls from highest quality to lowest quality
     */
    async determineHighestQuality(urls: string[]): Promise<string | false> {
        for (const url of urls) {
            const response = await got.head(url);

            if (parseInt(response.headers['content-length']) < MAX_FILESIZE) {
                return Promise.resolve(url);
            }
        }

        return Promise.resolve(false);
    }

    async getFile(url: string): Promise<FileOptions> {
        const response = await got.get(url).buffer();

        const parsed = new URL(url);

        const file: FileOptions = {
            attachment: response,
            name: path.basename(parsed.pathname),
        };

        return Promise.resolve(file);
    }
}

export default TwitterVideo;
