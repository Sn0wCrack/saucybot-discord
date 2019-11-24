import requests
import io
import discord
import json
from sites.base import Base


class ArtStation(Base):

    def __init__(self):
        self.name = 'ArtStation'
        self.pattern = 'artstation.com\/artwork\/(\S*)'
        self.api_url = 'https://www.artstation.com/projects/{}.json'
        self.colour = discord.Colour(0x000000)

    def process(self, match):
        (as_hash,) = match.groups()

        response = requests.get(self.api_url.format(as_hash), headers={
                                'User-Agent': 'SaucyBot/0.1.0',
                                'Referer': 'https://www.artstation.com/'})

        if not response:
            return None

        parsed_response = json.loads(response.text)

        if len(parsed_response['assets']) == 1:
            return None

        ret = {}

        embeds = []

        for assest in parsed_response['assets'][1:]:
            
            if assest['type'] in ['image', 'cover']:
                discord_embed = discord.Embed(title=parsed_response['title'], colour=self.colour)

                discord_embed.set_image(url=assest['image_url'])

                embeds.append(discord_embed)

            # TODO: Video Embeds

        ret['embeds'] = embeds

        return ret
