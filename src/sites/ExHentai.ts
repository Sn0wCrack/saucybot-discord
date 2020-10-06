import BaseSite from './BaseSite';
import ProcessResponse from './ProcessResponse';
import cheerio from 'cheerio';
import got from 'got';
import { DateTime } from 'luxon';
import { MessageEmbed } from 'discord.js';
import htmlToText from 'html-to-text';
import { CookieJar } from 'tough-cookie';
import Environment from '../Environment';

class ExHentai extends BaseSite {
    name = 'ExHentai';

    pattern = /https?:\/\/(www\.)?e[x-]hentai.org\/g\/(?<id>\d+)\/(?<hash>.+)/;

    color = 0x660611;

    async process(match: RegExpMatchArray): Promise<ProcessResponse | false> {
        const message: ProcessResponse = {
            embeds: [],
            files: [],
        };

        const url = match[0];

        const jar = new CookieJar();

        const memberId = Environment.get('EHENTAI_IPB_ID');
        const passHash = Environment.get('EHENTAI_IPB_PASS');

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

        const response = await got.get(url, { cookieJar: jar });

        const $ = cheerio.load(response.body);

        const title = $('.gm h1#gn');
        const image = $('.gm #gd1 > div');
        const description = $('div#comment_0');

        const imageUrl = image.css('background').match(/url\((?<url>.*)\)/)
            .groups['url'];

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

        let descriptionText = htmlToText.fromString(description.html());

        if (descriptionText.length > 300) {
            descriptionText = descriptionText.substring(0, 300) + '...';
        }

        const embed = new MessageEmbed({
            title: title.text(),
            url: url,
            description: descriptionText,
            color: this.color,
            timestamp: DateTime.fromFormat(posted.text(), 'yyyy-MM-dd HH:mm')
                .toUTC()
                .toMillis(),
            image: {
                url: imageUrl,
            },
            author: {
                name: authorLink.text(),
                url: `${authorLink.attr('href')}`,
            },
            fields: [
                {
                    name: 'Language',
                    value: language.text().trim(),
                    inline: true,
                },
                {
                    name: 'Pages',
                    value: pages.text().replace('pages', '').trim(),
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
        });

        message.embeds.push(embed);

        return Promise.resolve(message);
    }
}

export default ExHentai;
