import requests
import io
import discord
import json
from pyquery import PyQuery as pq
from sites.base import Base


class Newgrounds(Base):

    def __init__(self):
        self.name = 'Newgrounds'
        self.base_url = 'https://www.newgrounds.com'
        self.post_url = 'https://www.newgrounds.com/art/view/{}/{}'
        self.pattern = 'newgrounds.com\/art\/view\/(.*)\/(.*)'
        self.colour = discord.Colour(0xFFF17A)

    def process(self, match):
        (ng_user, ng_slug) = match.groups()

        session = requests.Session()

        full_url = self.post_url.format(ng_user, ng_slug)

        response = session.get(full_url)

        d = pq(response.text)

        title = d('.body-guts .column.wide.right .pod-head h2')
        image = d('#portal_item_view img')
        author_link = d('.body-guts .column.thin .pod-body .item-details a')
        author_img = d(
            '.body-guts .column.thin .pod-body .user-icon-bordered img')
        description = d('#author_comments')
        score = d('#score_number')

        ret = {}

        description_text = description.text()

        if len(description_text) > 300:
            description_text = description_text[0:300] + '...'

        discord_embed = discord.Embed(
            title=title.text(), url=full_url, description=description_text, colour=self.colour)

        discord_embed.set_image(url=image.attr('src'))

        discord_embed.set_author(name=author_link.text(
        ), url='https:' + author_link.attr('href'), icon_url='https:' + author_img.attr('src'))

        discord_embed.add_field(name='Score', value='{} / 5.00'.format(score.text()))

        ret['embed'] = discord_embed

        return ret
