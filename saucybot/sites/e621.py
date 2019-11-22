import requests
import io
import discord
import json
from sites.base import Base


class e621(Base):

    def __init__(self):
        self.name = 'e621'
        self.pattern = 'e621\.net\/post/show\/(\d+)'
        self.api_url = 'https://e621.net/post/show.json?id={}'

    def process(self, match):

        for (e621_id) in match.groups():
            response = requests.get(self.api_url.format(e621_id), headers={'User-Agent': 'SaucyBot/0.1.0'})

            if not response:
                return None

            parsed_response = json.loads(response.text)

            ret = {}

            discord_embed = discord.Embed(title=parsed_response['artist'][0])
            discord_embed.set_image(url=parsed_response['file_url'])

            ret['embed'] = discord_embed

            return ret
