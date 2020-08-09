import BaseSite from "./BaseSite";
import { ProcessResponse } from "./ProcessResponse";
import cheerio from 'cheerio';
import got from 'got';
import { MessageEmbed } from "discord.js";
import htmlToText from 'html-to-text';

class Newgrounds extends BaseSite
{
    name = 'Newgrounds';

    pattern = /newgrounds\.com\/art\/view\/(?<user>.*)\/(?<slug>.*)/;

    color = 0xFFF17A;

    async process (match: RegExpMatchArray): Promise<ProcessResponse|false> {
        const message: ProcessResponse = {
            embeds: [],
            files: [],
        };

        const response = await got.get(match.input);

        const $ = cheerio.load(response.body);

        const title = $('.body-guts .column.wide.right .pod-head h2');
        const image = $('.pod-body img');
        const description = $('#author_comments');

        const authorLink = $('.body-guts .column.thin .pod-body .item-details a');
        const authorImage = $('.body-guts .column.thin .pod-body .user-icon-bordered img');

        const views = $('.sidestats dt:contains("Views")')
            .siblings()
            .first();

        const score = $('#score_number');

        let descriptionText = htmlToText.fromString(description.html());

        if (descriptionText.length > 300) {
            descriptionText = descriptionText.substring(0, 300) + '...';
        }

        const embed = new MessageEmbed({
            title: title.text(),
            url: match.input,
            description: descriptionText,
            color: this.color,
            image: {
                url: image.attr('src'),
            },
            author: {
                name: authorLink.text(),
                url: `${authorLink.attr('href')}`,
                iconURL: `https:${authorImage.attr('src')}`,
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
            ]
        })

        message.embeds.push(embed);

        return Promise.resolve(message);
    }
}

export default Newgrounds;
