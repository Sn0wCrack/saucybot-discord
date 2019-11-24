from sites import (
    pixiv,
    e621,
    hentaifoundry,
    artstation
)


class SiteRunner():

    def __init__(self):
        # TODO: Make this load all of the modules dynamtically
        self.loaded_sites = [
            pixiv.Pixiv(),
            e621.e621(),
            hentaifoundry.HentaiFoundry(),
            artstation.ArtStation()
        ]

    def process(self, message):
        for site in self.loaded_sites:
            match = site.match(message)

            if match:
                print(site.name, match.groups())
                return site.process(match)

        return None
