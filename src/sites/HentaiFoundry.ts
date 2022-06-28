import BaseSite from './BaseSite';
import ProcessResponse from './ProcessResponse';
import { CookieJar } from 'tough-cookie';
import cheerio from 'cheerio';
import got from 'got';
import { Message, MessageEmbed } from 'discord.js';
import { DateTime } from 'luxon';
import CacheManager from '../CacheManager';
import { processDescription } from '../Helpers';
import { HENTAI_FOUNDRY_ICON_URL } from '../Constants';

class HentaiFoundry extends BaseSite {
    identifier = 'Hentai Foundry';

    pattern =
        /https:?\/\/(www\.)?hentai-foundry\.com\/pictures\/user\/(?<user>.*)\/(?<id>\d+)\/(?<slug>\S+)\/?/gim;

    color = 0xff67a2;

    baseUrl = 'https://www.hentai-foundry.com';

    jar: CookieJar;

    constructor() {
        super();

        this.jar = new CookieJar();
    }

    async process(
        match: RegExpMatchArray,
        /* eslint-disable @typescript-eslint/no-unused-vars */
        source: Message | null
    ): Promise<ProcessResponse | false> {
        const message: ProcessResponse = {
            embeds: [],
            files: [],
        };

        if (!match.groups?.id || !match.groups?.slug) {
            return Promise.resolve(false);
        }

        await got.get(`${this.baseUrl}/?enterAgree=1`, { cookieJar: this.jar });

        const url = match[0];

        const response = await this.getPage(
            url,
            match.groups.id,
            match.groups.slug
        );

        if (!response) {
            return Promise.resolve(false);
        }

        const $ = cheerio.load(response);

        const title = $('.imageTitle');
        const description = $('.picDescript');
        const image = $('#picBox .boxbody img');

        // We basically rely on the fact the posted at field is going to use this time element.
        const postedAt = $('#pictureGeneralInfoBox time');

        // HF has no specific selectors for these elements, so we do this jank method
        // That involves finding the views label and then getting the next text node's content and trimming it
        const views = $(
            '#pictureGeneralInfoBox .boxbody .column span:contains("Views")'
        )?.[0]?.next?.data?.trim();

        const votes = $(
            '#pictureGeneralInfoBox .boxbody .column span:contains("Vote Score")'
        )?.[0]?.next?.data?.trim();

        const authorLink = $('#descriptionBox .boxbody a');
        const authorImage = $('#descriptionBox .boxbody a img');

        const embed = new MessageEmbed({
            title: title.text(),
            url: url,
            timestamp: DateTime.fromISO(postedAt?.attr('datetime') ?? '')
                .toUTC()
                .toMillis(),
            description: processDescription(description.text()),
            color: this.color,
            image: {
                url: `https:${image.attr('src')}`,
            },
            author: {
                name: authorImage.attr('title'),
                url: `${this.baseUrl}${authorLink.attr('href')}`,
                iconURL: `https:${authorImage.attr('src')}`,
            },
            fields: [
                {
                    name: 'Views',
                    value: views ?? '0',
                    inline: true,
                },
                {
                    name: 'Votes',
                    value: votes ?? '0',
                    inline: true,
                },
            ],
            footer: {
                iconURL: HENTAI_FOUNDRY_ICON_URL,
                text: 'HentaiFoundry',
            },
        });

        message.embeds.push(embed);

        return Promise.resolve(message);
    }

    async getPage(
        url: string,
        id: string,
        slug: string
    ): Promise<string | null> {
        const cacheKey = `hentaifoundry.picture_${id}_${slug}`;
        const cacheManager = await CacheManager.getInstance();

        const cachedValue = await cacheManager.remember(cacheKey, async () => {
            const response = await got.get(url, { cookieJar: this.jar });
            return Promise.resolve(response.body);
        });

        if (!cachedValue) {
            return Promise.resolve(null);
        }

        return Promise.resolve(cachedValue);
    }
}

export default HentaiFoundry;
