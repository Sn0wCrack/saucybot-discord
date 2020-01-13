import requests
import io
import discord
import json
import config.artstation
import urllib.parse
import os
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

        asset_count = len(parsed_response['assets'])

        if asset_count == 1:
            return None

        ret = {}

        embeds = []

        limit = config.artstation.config['post_limit'] + 1

        cover_filename = os.basename(urllib.parse.urlsplit(parsed_response['cover_url']).path)

        if asset_count > limit:
            ret['content'] = 'This is part of a {} image set.'.format(
                asset_count)

        for asset in parsed_response['assets'][1:limit]:

            if asset['asset_type'] in ['image', 'cover']:

                # Skip Image if it's the same as the cover
                if cover_filename in asset['image_url']:
                    continue

                discord_embed = discord.Embed(
                    title=parsed_response['title'], url=parsed_response['permalink'], colour=self.colour)

                discord_embed.set_image(url=asset['image_url'])

                discord_embed.set_author(name=parsed_response['user']['full_name'], url=parsed_response['user']
                                         ['permalink'], icon_url=parsed_response['user']['medium_avatar_url'])

                embeds.append(discord_embed)

            # TODO: Video Embeds

        ret['embeds'] = embeds

        return ret
