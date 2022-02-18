import BaseSite from './BaseSite';
import ProcessResponse from './ProcessResponse';
import { TwitterClient, StatusesShow } from 'twitter-api-client';
import Environment from '../Environment';
import { MAX_FILESIZE, TWITTER_ICON_URL } from '../Constants';
import { FileOptions, Message, MessageEmbed } from 'discord.js';
import path from 'path';
import got from 'got';
import { URL } from 'url';
import { DateTime } from 'luxon';
import { delay } from '../Helpers';

class TwitterVideo extends BaseSite {
    identifier = 'Twitter';

    pattern =
        /https?:\/\/(www\.)?twitter\.com\/(?<user>.*)\/status\/(?<id>\S+)(\?\=.*)?/gim;

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

    async process(
        match: RegExpMatchArray,
        source: Message | null
    ): Promise<ProcessResponse | false> {
        const results = await this.api.tweets.statusesShow({
            id: match.groups.id,
            include_entities: true,
            trim_user: false,
            tweet_mode: 'extended',
        });

        const hasVideo = results?.extended_entities?.media.find((item) =>
            ['video', 'animated_gif'].includes(item.type)
        );

        let hasTwitterEmbed: MessageEmbed | null | undefined = null;

        if (source) {
            await delay(600);

            source = await source.fetch(true);

            hasTwitterEmbed = source.embeds?.find((item) => {
                return (
                    item.url.includes('twitter.com') ||
                    item.url.includes('t.co')
                );
            });
        }

        // Only try and embed this twitter link if one of the following is true:
        //  - Discord has failed to create an embed for Twitter
        //  - The result is "sensitive" and it has a video, as Discord often fails to play these inline
        if (hasTwitterEmbed || (hasVideo && !results.possibly_sensitive)) {
            return Promise.resolve(false);
        }

        return hasVideo
            ? this.handleVideo(results)
            : this.handleRegular(results, match[0]);
    }

    async handleVideo(status: StatusesShow): Promise<ProcessResponse | false> {
        const video = status?.extended_entities?.media.find((item) =>
            ['video', 'animated_gif'].includes(item.type)
        );

        if (!video) {
            return Promise.resolve(false);
        }

        const variants = video.video_info.variants
            .filter((item) => item?.bitrate !== undefined)
            .sort((a, b) => b.bitrate - a.bitrate)
            .map((item) => item.url);

        const variant = await this.determineHighestQuality(variants);

        if (!variant) {
            return Promise.resolve(false);
        }

        const videoFile = await this.getFile(variant);

        const message: ProcessResponse = {
            embeds: [],
            files: [videoFile],
        };

        return Promise.resolve(message);
    }

    async handleRegular(
        status: StatusesShow,
        url: string
    ): Promise<ProcessResponse | false> {
        const message: ProcessResponse = {
            embeds: [],
            files: [],
        };

        const photo = status?.extended_entities?.media.find((item) =>
            ['photo'].includes(item.type)
        );

        const time = DateTime.fromFormat(
            status.created_at,
            'ccc LLL d HH:mm:ss ZZZ y'
        );

        const embed = new MessageEmbed({
            url: url,
            timestamp: time.toMillis(),
            color: this.color,
            description: status.full_text,
            author: {
                name: `${status.user.name} (@${status.user.screen_name})`,
                iconURL: status.user.profile_image_url_https,
                url: status.user.url,
            },
            fields: [
                {
                    name: 'Likes',
                    value: status.favorite_count.toString(),
                    inline: true,
                },
                {
                    name: 'Retweets',
                    value: status.retweet_count.toString(),
                    inline: true,
                },
            ],
            image: {
                url: photo?.media_url_https ?? '',
            },
            footer: {
                iconURL: TWITTER_ICON_URL,
                text: 'Twitter',
            },
        });

        message.embeds.push(embed);

        return Promise.resolve(message);
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
