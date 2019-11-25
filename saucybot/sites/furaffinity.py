import requests
import io
import discord
import json
import config.furaffinity
from pyquery import PyQuery as pq
from sites.base import Base


class FurAffinity(Base):

    def __init__(self):
        self.name = 'FurAffinity'
        self.pattern = '(furaffinity\.net/(?:view|full)/(\d+))'
        self.colour = discord.Colour(0x000000)

    def process(self, match):
        (fa_link, fa_id) = match.groups() 

        session = requests.Session()

        session.cookies.set(name='a', value=config.furaffinity.config['cookie_a'])
        session.cookies.set(name='b', value=config.furaffinity.config['cookie_b'])

        respone = session.get(fa_link)

        if not response:
            return None

        d = pq(response.text)

        title = d('.cat .container .information h2')
        image = d('#submissionImg')

        ret = {}

        discord_embed = discord.Embed(title=title.text())

        discord_embed.set_image(url='https:' + image.attr('src'))

        ret['embed'] = discord_embed

        return ret