from sites import (
    pixiv,
    e621,
    hentaifoundry
)


class SiteRunner():

    def __init__(self):
        self.loaded_sites = [
            pixiv.Pixiv(),
            e621.e621(),
            hentaifoundry.HentaiFoundry()
        ]

    def process(self, message):
        for site in self.loaded_sites:
            match = site.match(message)

            if match:
                print(site.name, match.groups())
                return site.process(match)

        return None
