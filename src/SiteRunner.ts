import HentaiFoundry from "./sites/HentaiFoundry";
import BaseSite from "./sites/BaseSite";
import { ProcessResponse } from "./sites/ProcessResponse";
import ArtStation from "./sites/ArtStation";
import Newgrounds from "./sites/Newgrounds";
import Pixiv from "./sites/Pixiv";
import ExHentai from "./sites/ExHentai";

class SiteRunner
{
    static async process(message: string): Promise<ProcessResponse|false>
    {
        const sites: Array<BaseSite> = [
            new HentaiFoundry(),
            new ArtStation(),
            new Newgrounds(),
            new Pixiv(),
            new ExHentai(),
        ];

        for (const site of sites) {
            const match = site.match(message)

            if (match) {
                console.log(`Matched ${message} to site ${site.name}`)

                return site.process(match)
            }
        }

        return Promise.resolve(false);
    }
}

export default SiteRunner;
