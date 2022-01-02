import RunnerResponse from './RunnerResponse';
import BaseSite from './sites/BaseSite';
import Sites from './sites';
import Environment from './Environment';
class SiteRunner {
    sites: Array<BaseSite>;

    constructor() {
        const list: string = Environment.get('DISABLED_SITES', '') as string;
        const disabled: Array<string> = list.split(',');

        this.sites = Sites.map((s) => new s()).filter(
            (s) => !disabled.includes(s.identifier)
        );
    }

    async process(message: string): Promise<RunnerResponse | false> {
        for (const site of this.sites) {
            const match = site.match(message);

            if (!match) {
                continue;
            }

            return Promise.resolve({
                site: site,
                match: match,
            });
        }

        return Promise.resolve(false);
    }
}

export default SiteRunner;
