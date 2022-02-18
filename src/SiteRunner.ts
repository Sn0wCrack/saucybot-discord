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

        let embedCount = 0;
        const maximumEmbeds = Environment.get('MAXIMUM_EMBEDS', 5) as number;

        for (const site of this.sites) {
            let matches = Array.from(site.match(message));

            if (matches.length === 0) {
                continue;
            }

            // If the number of matches is greather than our maximum embed count, only get the first X elements instead
            if (embedCount + matches.length > maximumEmbeds) {
                matches = matches.slice(0, maximumEmbeds - embedCount);
            }

            embedCount += matches.length;

            // If we go over our maximum embed limit, return the results now and display everything we've matched
            if (embedCount > maximumEmbeds) {
                return results;
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
