import BaseSite from './BaseSite';
import ProcessResponse from './ProcessResponse';
import { CookieJar } from 'tough-cookie';
import cheerio from 'cheerio';
import got from 'got';
import { MessageEmbed } from 'discord.js';
import { DateTime } from 'luxon';

class HentaiFoundry extends BaseSite {
    identifier = 'Hentai Foundry';

    pattern =
        /https:?\/\/(www\.)?hentai-foundry\.com\/pictures\/user\/(?<user>.*)\/(?<id>\d+)\/(?<slug>\S+)/gim;

    color = 0xff67a2;

    baseUrl = 'https://www.hentai-foundry.com';

    async process(match: RegExpMatchArray): Promise<ProcessResponse | false> {
        const message: ProcessResponse = {
            embeds: [],
            files: [],
        };

        const url = match[0];

        const jar = new CookieJar();

        await got.get(`${this.baseUrl}/?enterAgree=1`, { cookieJar: jar });

        const response = await got.get(url, { cookieJar: jar });

        const $ = cheerio.load(response.body);

        const title = $('.imageTitle');
        const description = $('.picDescript');
        const image = $('#picBox .boxbody img');

        // We basically rely on the fact the posted at field is going to use this time element.
        const postedAt = $('#yw0 time');

        // HF has no specific selectors for these elements, so we do this jank method
        // That involves getting the label, going up one level (from the <b></b> tags)
        // and then getting the next sibling
        const views = $('#yw0 b:contains("Views")').parent().siblings().first();

        const votes = $('#yw0 b:contains("Vote Score")')
            .parent()
            .siblings()
            .first();

        const authorLink = $('#descriptionBox .boxbody a');
        const authorImage = $('#descriptionBox .boxbody a img');

        let descriptionText = description.text();

        // Since Discord has a maximum character limit of 6000, and some HF descriptions are long
        // We simply trim the description after 300 characters to max sure we can send the embed properly
        if (descriptionText.length > 300) {
            descriptionText = descriptionText.substring(0, 300) + '...';
        }

        const embed = new MessageEmbed({
            title: title.text(),
            url: url,
            timestamp: DateTime.fromISO(postedAt.attr('datetime'))
                .toUTC()
                .toMillis(),
            description: descriptionText,
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
                    value: views.text(),
                    inline: true,
                },
                {
                    name: 'Votes',
                    value: votes.text(),
                    inline: true,
                },
            ],
        });

        message.embeds.push(embed);

        return Promise.resolve(message);
    }
}

export default HentaiFoundry;
