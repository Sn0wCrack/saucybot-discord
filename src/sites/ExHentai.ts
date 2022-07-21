import BaseSite from './BaseSite';
import ProcessResponse from './ProcessResponse';
import cheerio from 'cheerio';
import got from 'got';
import { DateTime } from 'luxon';
import { Message, EmbedBuilder } from 'discord.js';
import { CookieJar } from 'tough-cookie';
import Environment from '../Environment';
import CacheManager from '../CacheManager';
import { processDescription } from '../Helpers';
import { EHENTAI_ICON_URL } from '../Constants';

class ExHentai extends BaseSite {
    identifier = 'ExHentai';

    pattern =
        /https?:\/\/(www\.)?e[x-]hentai\.org\/g\/(?<id>\d+)\/(?<hash>\S+)\/?/gim;

    color = 0x660611;

    async process(
        match: RegExpMatchArray,
        /* eslint-disable @typescript-eslint/no-unused-vars */
        source: Message | null
    ): Promise<ProcessResponse | false> {
        const message: ProcessResponse = {
            embeds: [],
            files: [],
        };

        if (!match.groups?.id || !match.groups?.hash) {
            return Promise.resolve(false);
        }

        const url = match[0];

        const jar = new CookieJar();

        const memberId = Environment.get('EHENTAI_IPB_ID') as string | null;
        const passHash = Environment.get('EHENTAI_IPB_PASS') as string | null;

        // If we're processing for exhentai, we require these cookies
        // So if that's the case, then we just simply bail out early as this will never work
        if (
            url.toLowerCase().includes('exhentai') &&
            memberId === null &&
            passHash === null
        ) {
            return Promise.resolve(false);
        }

        await jar.setCookie(
            `ipb_member_id=${memberId})}`,
            'https://exhentai.org'
        );
        await jar.setCookie(
            `ipb_pass_hash=${passHash}`,
            'https://exhentai.org'
        );

        const body = await this.getPage(
            url,
            match.groups.id,
            match.groups.hash,
            jar
        );

        if (!body) {
            return Promise.resolve(false);
        }

        const $ = cheerio.load(body);

        const title = $('.gm h1#gn');
        const image = $('.gm #gd1 > div');
        const description = $('div#comment_0');

        const imageUrl = image.css('background')?.match(/url\((?<url>.*)\)/)
            ?.groups?.url;

        const metaContainer = $('.gm #gmid #gd3 #gdd tbody');

        const posted = metaContainer.children('tr').first().children('.gdt2');
        const language = metaContainer
            .children('tr')
            .children('td:contains("Language:")')
            .siblings()
            .first();
        const pages = metaContainer
            .children('tr')
            .children('td:contains("Length:")')
            .siblings()
            .first();

        const rating = $('td#rating_label');

        const authorLink = $('.gm #gmid #gdn a');

        const embed = new EmbedBuilder({
            title: title.text(),
            url: url,
            description: processDescription(description?.html() ?? ''),
            color: this.color,
            timestamp: DateTime.fromFormat(posted.text(), 'yyyy-MM-dd HH:mm')
                .toUTC()
                .toMillis(),
            image: {
                url: imageUrl ?? '',
            },
            author: {
                name: authorLink.text(),
                url: authorLink.attr('href'),
            },
            fields: [
                {
                    name: 'Language',
                    value: language.text().trim() ?? 'N/A',
                    inline: true,
                },
                {
                    name: 'Pages',
                    value: pages.text().replace('pages', '').trim() ?? 'N/A',
                    inline: true,
                },
                {
                    name: 'Rating',
                    value: `${rating
                        .text()
                        .replace('Average:', '')
                        .trim()} / 5.00`,
                    inline: true,
                },
            ],
            footer: {
                iconURL: EHENTAI_ICON_URL,
                text: url.toLowerCase().includes('exhentai')
                    ? 'exhentai'
                    : 'e-hentai',
            },
        });

        message.embeds.push(embed);

        return Promise.resolve(message);
    }

    async getPage(
        url: string,
        id: string,
        hash: string,
        jar: CookieJar
    ): Promise<string | null> {
        const cacheKey = `ehentai.gallery_${id}_${hash}`;
        const cacheManager = await CacheManager.getInstance();

        const cachedValue = await cacheManager.remember(cacheKey, async () => {
            const response = await got.get(url, { cookieJar: jar });
            return Promise.resolve(response.body);
        });

        if (!cachedValue) {
            return Promise.resolve(null);
        }

        return Promise.resolve(cachedValue);
    }
}

export default ExHentai;
