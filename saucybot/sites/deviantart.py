import requests
import io
import discord
import json
import os
from sites.base import Base


class DeviantArt(Base):

    def __init__(self):
        self.name = 'DeviantArt'
        self.pattern = 'deviantart.com\/(.*)\/art\/(.*)'
        self.post_url = 'https://deviantart.com/{}/art/{}'
        self.api_url = 'https://backend.deviantart.com/oembed?url={}'
        self.colour = discord.Colour(0x06070D)

    def process(self, match):
        (da_artist, da_id) = match.groups()

        full_url = self.post_url.format(da_artist, da_id)

        response = requests.get(self.api_url.format(full_url), headers={
                                'User-Agent': 'SaucyBot/0.1.0',
                                'Referer': 'https://www.deviantart.com/'})

        if not response:
            return None

        parsed_response = json.loads(response.text)

        ret = {}

        discord_embed = discord.Embed(
            title=parsed_response['title'], url=full_url, colour=self.colour)

        discord_embed.set_image(url=parsed_response['url'])

        discord_embed.set_author(name=parsed_response['author_name'], url=parsed_response['author_url'])

        ret['embed'] = discord_embed

        return ret
