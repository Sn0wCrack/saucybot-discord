from sites import (
    pixiv
)

class SiteRunner():

    def __init__(self):
        self.loaded_sites = [
            pixiv.Pixiv()
        ]

    def process(self, message):
        for site in self.loaded_sites:
            match = site.match(message)

            if match:
                return site.process(match)
