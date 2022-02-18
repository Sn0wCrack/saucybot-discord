import BaseSite from './BaseSite';
import ProcessResponse from './ProcessResponse';
import got from 'got';
import { version } from '../../package.json';
import Environment from '../Environment';
import Deviant, {
    AuthenticationOptions,
    HttpOptions,
} from '@sn0wcrack/deviant';
import cheerio from 'cheerio';
import { DeviantionResponse } from '@sn0wcrack/deviant/dist/Responses';
import { MessageEmbed } from 'discord.js';
import { DateTime } from 'luxon';

const OMEBED_URL = 'https://backend.deviantart.com/oembed';

class DeviantArt extends BaseSite {
    identifier = 'DeviantArt';

    pattern =
        /https?:\/\/(www\.)?deviantart.com\/(?<author>\S+)\/art\/(?<slug>\S+)/gim;

    color = 0x00e59b;

    private api: Deviant;

    constructor() {
        super();

        const auth: AuthenticationOptions = {
            clientId: Environment.get('DEVIANTART_CLIENT_ID') as number,
            clientSecret: Environment.get('DEVIANTART_CLIENT_SECRET') as string,
        };

        const http: HttpOptions = {
            userAgent: `SaucyBot/${version}`,
        };

        this.api = new Deviant(auth, http);

        this.api.authorize();
    }

    async process(match: RegExpMatchArray): Promise<ProcessResponse | false> {
        const message: ProcessResponse = {
            embeds: [],
            files: [],
        };

        const url = match[0];

        try {
            const response = await got.get(url);

            const $ = cheerio.load(response.body);

            const appUrl = $('meta[property="da:appurl"]').attr('content');

            const parsed = appUrl.match(
                /DeviantArt:\/\/deviation\/(?<uuid>.*)/i
            );

            if (!parsed.groups?.uuid) {
                return Promise.resolve(false);
            }

            const deviation: DeviantionResponse = await this.api
                .deviations()
                .get(parsed.groups?.uuid);

            const embed = new MessageEmbed({
                title: deviation.title,
                url: url,
                timestamp: DateTime.fromSeconds(
                    parseInt(deviation.published_time)
                ).toMillis(),
                color: this.color,
                image: {
                    url:
                        deviation.content?.src ??
                        deviation.thumbs?.[0].src ??
                        '',
                },
                author: {
                    name: deviation.author.username,
                    url: `https://deviantart.com/${deviation.author.username}`,
                    iconURL: deviation.author.usericon,
                },
                fields: [
                    {
                        name: 'Favourites',
                        value: deviation.stats.favourites.toString(),
                        inline: true,
                    },
                    {
                        name: 'Comments',
                        value: deviation.stats.comments.toString(),
                        inline: true,
                    },
                ],
            });

            message.embeds.push(embed);

            return Promise.resolve(message);
        } catch (ex) {
            return await this.oembed(match);
        }
    }

    async oembed(match: RegExpMatchArray): Promise<ProcessResponse | false> {
        const message: ProcessResponse = {
            embeds: [],
            files: [],
        };

        const url = match[0];

        const response: oEmbedReponse = await got
            .get(OMEBED_URL, {
                searchParams: { url: url },
                headers: {
                    'User-Agent': `SaucyBot/${version}`,
                },
            })
            .json<oEmbedReponse>();

        const embed = new MessageEmbed({
            title: response.title,
            url: url,
            color: this.color,
            image: {
                url: response.url,
            },
            author: {
                name: response.author_name,
                url: response.author_url,
            },
        });

        message.embeds.push(embed);

        return Promise.resolve(message);
    }
}

export default DeviantArt;

export interface oEmbedReponse {
    version: string;
    type: string;
    title: string;
    url: string;
    author_name: string;
    author_url: string;
    provider_name: string;
    provider_url: string;
    thumbnail_url: string;
    thumbnail_width: number;
    thumbnail_height: number;
    width: number;
    height: number;
}
