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

    async process(message: string): Promise<Array<RunnerResponse> | false> {
        const results = [];

        for (const site of this.sites) {
            const matches = Array.from(site.match(message));

            if (matches.length === 0) {
                continue;
            }

            results.push({
                site,
                matches,
            });
        }

        return Promise.resolve(results.length > 0 ? results : false);
    }
}

export default SiteRunner;
