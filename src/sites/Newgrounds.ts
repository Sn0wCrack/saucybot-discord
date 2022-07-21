import BaseSite from './BaseSite';
import ProcessResponse from './ProcessResponse';
import cheerio from 'cheerio';
import got from 'got';
import { Message, EmbedBuilder } from 'discord.js';
import CacheManager from '../CacheManager';
import { processDescription } from '../Helpers';

class Newgrounds extends BaseSite {
    identifier = 'Newgrounds';

    pattern =
        /https?:\/\/(www\.)?newgrounds\.com\/art\/view\/(?<user>.*)\/(?<slug>\S+)\/?/gim;

    color = 0xfff17a;

    async process(
        match: RegExpMatchArray,
        /* eslint-disable @typescript-eslint/no-unused-vars */
        source: Message | null
    ): Promise<ProcessResponse | false> {
        const message: ProcessResponse = {
            embeds: [],
            files: [],
        };

        if (!match.groups?.user || !match.groups?.slug) {
            return Promise.resolve(false);
        }

        const url = match[0];

        const body = await this.getPage(
            url,
            match.groups.user,
            match.groups.slug
        );

        if (!body) {
            return Promise.resolve(false);
        }

        const $ = cheerio.load(body);

        const title = $('.body-guts .column.wide.right .pod-head h2');
        const image = $('.pod-body .image #portal_item_view img');

        const description = $('#author_comments');

        const authorLink = $(
            '.body-guts .column.thin .pod-body .item-details a'
        );
        const authorImage = $(
            '.body-guts .column.thin .pod-body .user-icon-bordered img'
        );

        const views = $('.sidestats dt:contains("Views")').siblings().first();

        const score = $('#score_number');

        const embed = new EmbedBuilder({
            title: title.text(),
            url: url,
            description: processDescription(description?.html() ?? ''),
            color: this.color,
            image: {
                url: image.attr('src') ?? '',
            },
            author: {
                name: authorLink.text(),
                url: authorLink.attr('href'),
                iconURL: authorImage.attr('src'),
            },
            fields: [
                {
                    name: 'Views',
                    value: views.text(),
                    inline: true,
                },
                {
                    name: 'Score',
                    value: `${score.text()} / 5.00`,
                    inline: true,
                },
            ],
            footer: {
                text: 'Newgrounds',
            },
        });

        message.embeds.push(embed);

        return Promise.resolve(message);
    }

    async getPage(
        url: string,
        user: string,
        slug: string
    ): Promise<string | null> {
        const cacheKey = `newgrounds.art_${user}_${slug}`;
        const cacheManager = await CacheManager.getInstance();

        const cachedValue = await cacheManager.remember(cacheKey, async () => {
            const response = await got.get(url);
            return Promise.resolve(response.body);
        });

        if (!cachedValue) {
            return Promise.resolve(null);
        }

        return Promise.resolve(cachedValue);
    }
}

export default Newgrounds;
