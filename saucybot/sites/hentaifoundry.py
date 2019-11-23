import requests
import io
import discord
import json
from pyquery import PyQuery as pq
from sites.base import Base


class HentaiFoundry(Base):

    def __init__(self):
        self.name = 'Hentai Foundry'
        self.base_url = 'https://www.hentai-foundry.com'
        self.post_url = 'https://www.hentai-foundry.com/pictures/user/{}/{}/{}'
        self.pattern = 'hentai-foundry.com\/pictures\/user\/(.*)\/(\d+)\/(.*)'

    def process(self, match):
        (hf_user, hf_id, hf_slug) = match.groups()

        session = requests.Session()

        # HF requires us to accept we're over 18 first before entering the site
        # This request emulates that into our session
        session.get(self.base_url + '?enterAgree=1')

        full_url = self.post_url.format(hf_user, hf_id, hf_slug)

        response = session.get(full_url)
        
        d = pq(response.text)

        title = d('.imageTitle')
        image = d('#picBox .boxbody img')
        author_link = d('#descriptionBox .boxbody a')
        author_img = d('#descriptionBox .boxbody a img')
        description = d('.picDescript')

        ret = {}
        
        discord_embed = discord.Embed(title=title.text(), url=full_url, description=description.text())

        discord_embed.set_image(url='https:' + image.attr('src'))

        discord_embed.set_author(name=author_img.attr('title'), url=self.base_url + author_link.attr('href'), icon_url='https:' + author_img.attr('src'))

        ret['embed'] = discord_embed

        return ret
