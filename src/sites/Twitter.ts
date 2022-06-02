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
import CacheManager from '../CacheManager';

class Twitter extends BaseSite {
    identifier = 'Twitter';

    pattern =
        /https?:\/\/(www\.|mobile\.)?twitter\.com\/(?<user>.*)\/status\/(?<id>\d+)(\?\=.*)?\/?/gim;

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
            disableCache: true,
        });
    }

    async process(
        match: RegExpMatchArray,
        source: Message | null
    ): Promise<ProcessResponse | false> {
        const tweet = await this.getTweet(match.groups?.id);

        const videoMedia = await this.findVideoElement(tweet);

        let hasTwitterEmbed: MessageEmbed | null | undefined = null;

        // If we have a message attached, we need to wait a bit for Discord to process the embed,
        // we when need to refetch the message and see if an embed has been added in that time.
        if (source) {
            await delay(Environment.get('TWITTER_READ_DELAY', 1000) as number);

            source = await source.fetch(true);

            hasTwitterEmbed = source.embeds?.find((item) => {
                const isFromTwitter =
                    item?.url?.includes('twitter.com') ||
                    item?.url?.includes('t.co');

                return isFromTwitter === true && item.author !== null;
            });
        }

        // Only try and embed this twitter link if one of the following is true:
        //  - Discord has failed to create an embed for Twitter
        //  - The result is "sensitive" and it has a video, as Discord often fails to play these inline

        if (videoMedia && tweet.possibly_sensitive) {
            return this.handleVideo(
                tweet,
                hasTwitterEmbed === null || hasTwitterEmbed === undefined
            );
        }

        if (hasTwitterEmbed) {
            return Promise.resolve(false);
        }

        return this.handleRegular(tweet);
    }

    private async findVideoElement(tweet: StatusesShow) {
        const video = tweet?.extended_entities?.media.find((item) =>
            ['video', 'animated_gif'].includes(item.type)
        );

        if (!video && tweet.is_quote_status) {
            const quotedTweet = await this.getTweet(tweet.quoted_status_id_str);

            const quotedVideo = quotedTweet?.extended_entities?.media.find(
                (item) => ['video', 'animated_gif'].includes(item.type)
            );

            return Promise.resolve(quotedVideo);
        }

        return Promise.resolve(video);
    }

    private async findPhotoElement(tweet: StatusesShow) {
        const photo = tweet?.extended_entities?.media.find((item) =>
            ['photo'].includes(item.type)
        );

        if (!photo && tweet.is_quote_status) {
            const quotedTweet = await this.getTweet(tweet.quoted_status_id_str);

            const quotedPhoto = quotedTweet?.extended_entities?.media.find(
                (item) => ['photo'].includes(item.type)
            );

            return Promise.resolve(quotedPhoto);
        }

        return Promise.resolve(photo);
    }

    private async findAllPhotoElements(tweet: StatusesShow) {
        const photos = tweet?.extended_entities?.media.filter((item) =>
            ['photo'].includes(item.type)
        );

        if (!photos && tweet.is_quote_status) {
            const quotedTweet = await this.getTweet(tweet.quoted_status_id_str);

            const quotedPhotos = quotedTweet?.extended_entities?.media.filter(
                (item) => ['photo'].includes(item.type)
            );

            return Promise.resolve(quotedPhotos);
        }

        return Promise.resolve(photos);
    }

    private async getTweet(id: string): Promise<StatusesShow> {
        const cacheKey = `twitter.tweet_${id}`;
        const cacheManager = await CacheManager.getInstance();

        const cachedValue = await cacheManager.remember(cacheKey, async () => {
            const results = await this.api.tweets.statusesShow({
                id: id,
                include_entities: true,
                trim_user: false,
                tweet_mode: 'extended',
            });

            return Promise.resolve(JSON.stringify(results));
        });

        const results = JSON.parse(cachedValue) as StatusesShow;

        return Promise.resolve(results);
    }

    private async handleVideo(
        status: StatusesShow,
        makeEmbed = false
    ): Promise<ProcessResponse | false> {
        const video = await this.findVideoElement(status);

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

        if (makeEmbed) {
            const time = DateTime.fromFormat(
                status.created_at,
                'ccc LLL d HH:mm:ss ZZZ y'
            );

            const embed = new MessageEmbed({
                url: this.getUrlFromStatus(status),
                timestamp: time.toUTC().toMillis(),
                color: this.color,
                description: status.full_text,
                author: {
                    name: `${status.user.name} (@${status.user.screen_name})`,
                    iconURL: status.user.profile_image_url_https,
                    url: `https://twitter.com/${status.user.screen_name}`,
                },
                video: {
                    url: `attachment://${videoFile.name}`,
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
                footer: {
                    iconURL: TWITTER_ICON_URL,
                    text: 'Twitter',
                },
            });

            message.embeds.push(embed);
        }

        return Promise.resolve(message);
    }

    private async handleRegular(
        status: StatusesShow
    ): Promise<ProcessResponse | false> {
        const message: ProcessResponse = {
            embeds: [],
            files: [],
        };

        const photos = await this.findAllPhotoElements(status);

        const time = DateTime.fromFormat(
            status.created_at,
            'ccc LLL d HH:mm:ss ZZZ y'
        );

        if (!photos) {
            const embed = new MessageEmbed({
                url: this.getUrlFromStatus(status),
                timestamp: time.toUTC().toMillis(),
                color: this.color,
                description: status.full_text,
                author: {
                    name: `${status.user.name} (@${status.user.screen_name})`,
                    iconURL: status.user.profile_image_url_https,
                    url: `https://twitter.com/${status.user.screen_name}`,
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
                footer: {
                    iconURL: TWITTER_ICON_URL,
                    text: 'Twitter',
                },
            });

            message.embeds.push(embed);

            return Promise.resolve(message);
        }

        for (const photo of photos) {
            const embed = new MessageEmbed({
                url: this.getUrlFromStatus(status),
                timestamp: time.toUTC().toMillis(),
                color: this.color,
                description: status.full_text,
                author: {
                    name: `${status.user.name} (@${status.user.screen_name})`,
                    iconURL: status.user.profile_image_url_https,
                    url: `https://twitter.com/${status.user.screen_name}`,
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
        }

        return Promise.resolve(message);
    }

    private getUrlFromStatus = (tweet: StatusesShow): string =>
        `https://twitter.com/${tweet.user.screen_name}/status/${tweet.id_str}`;

    /**
     * Determines the highest quality of a video that can be posted to Discord inside of its size limit
     *
     * @param urls a list of urls from highest quality to lowest quality
     */
    private async determineHighestQuality(
        urls: string[]
    ): Promise<string | false> {
        for (const url of urls) {
            const response = await got.head(url);

            if (parseInt(response.headers['content-length']) < MAX_FILESIZE) {
                return Promise.resolve(url);
            }
        }

        return Promise.resolve(false);
    }

    private async getFile(url: string): Promise<FileOptions> {
        const response = await got.get(url).buffer();

        const parsed = new URL(url);

        const file: FileOptions = {
            attachment: response,
            name: path.basename(parsed.pathname),
        };

        return Promise.resolve(file);
    }
}

export default Twitter;
