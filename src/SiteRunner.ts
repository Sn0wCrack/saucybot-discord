import ProcessResponse from './sites/ProcessResponse';
import BaseSite from './sites/BaseSite';
import Sites from './sites';
import { Message } from 'discord.js';

class SiteRunner {
    static async process(message: Message): Promise<ProcessResponse | false> {
        const sites: Array<BaseSite> = Sites.map((s) => new s());

        for (const site of sites) {
            const match = site.match(message.content);

            if (match) {
                console.log(
                    `${message.guild.name} - Matched ${message.content} to site ${site.name}`
                );

                return site.process(match);
            }
        }

        return Promise.resolve(false);
    }
}

export default SiteRunner;
