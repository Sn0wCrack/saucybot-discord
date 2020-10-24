import ProcessResponse from './sites/ProcessResponse';
import BaseSite from './sites/BaseSite';
import Sites from './sites';
import { Message } from 'discord.js';
import Environment from './Environment';

class SiteRunner {
    sites: Array<BaseSite>;

    constructor() {
        const list: string = Environment.get('DISABLED_SITES', '') as string;
        const disabled: Array<string> = list.split(',');

        // NOTE: Should do the filtering before instiating classes for performance sake, but isn't much of an issue right now
        this.sites = Sites.map((s) => new s()).filter(
            (s) => !disabled.includes(s.name)
        );
    }

    async process(message: Message): Promise<ProcessResponse | false> {
        for (const site of this.sites) {
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
