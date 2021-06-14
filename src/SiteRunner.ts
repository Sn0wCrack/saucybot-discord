import RunnerResponse from './RunnerResponse';
import BaseSite from './sites/BaseSite';
import Sites from './sites';
import { Message } from 'discord.js';
import Environment from './Environment';
import Logger from './Logger';

class SiteRunner {
    sites: Array<BaseSite>;

    constructor() {
        const list: string = Environment.get('DISABLED_SITES', '') as string;
        const disabled: Array<string> = list.split(',');

        this.sites = Sites.map((s) => new s()).filter(
            (s) => !disabled.includes(s.identifier)
        );
    }

    async process(message: Message): Promise<RunnerResponse | false> {
        for (const site of this.sites) {
            const match = site.match(message.content);

            if (!match) {
                continue;
            }

            Logger.info(
                `${message.guild.name} - Matched message "${message.content}" to site ${site.identifier}`
            );

            return Promise.resolve({
                site: site,
                match: match,
            });
        }

        return Promise.resolve(false);
    }
}

export default SiteRunner;
