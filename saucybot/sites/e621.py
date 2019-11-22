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
            response = requests.get(self.api_url.format(e621_id), headers={
                                    'User-Agent': 'SaucyBot/0.1.0'})

            if not response:
                return None

            parsed_response = json.loads(response.text)

            # Don't try and embed videos and flash files
            if parsed_response['file_ext'] in ['webm', 'mp4', 'swf']: 
                return None

            ret = {}

            discord_embed = discord.Embed(title='#{}: {}'.format(
                parsed_response['id'], parsed_response['artist'][0]), description=parsed_response['description'])
            
            discord_embed.set_image(url=parsed_response['file_url'])

            discord_embed.set_author(name=parsed_response['author'])

            ret['embed'] = discord_embed

            return ret
