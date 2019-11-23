import requests
import io
import discord
from sites.base import Base
import config.pixiv
import pixivpy3


class Pixiv(Base):

    def __init__(self):
        pixivapi = pixivpy3.AppPixivAPI()
        pixivapi.login(
            config.pixiv.config['pixiv_login'], config.pixiv.config['pixiv_password'])

        self.name = 'Pixiv'
        self.api = pixivapi
        self.pattern = 'pixiv.net\/.*artworks\/(\d*)'

    def process(self, match):
        self.api.auth()

        (pixiv_id) = match.groups()

        pixiv_result = self.api.illust_detail(pixiv_id)

        if not 'illust' in pixiv_result:
            return None

        pixiv_meta_page_count = len(pixiv_result.illust.meta_pages)

        # We only grab a maximum of 5 to prevent issues with rate-limiting
        # As well as hopefully prevent spam issues
        pixiv_meta_pages = pixiv_result.illust.meta_pages[:5]

        ret = {}

        # If is this a single image post, just send the main illustration image
        if pixiv_meta_page_count == 0:
            pixiv_image_link = pixiv_result.illust.image_urls.large

            image = self.get_image(pixiv_image_link)

            ret['files'] = [discord.File(image)]

            return ret

        # Otherwise, send through all images on the post
        files = []

        for pixiv_meta_page in pixiv_meta_pages:
            pixiv_image_link = pixiv_meta_page.image_urls.large

            image = self.get_image(pixiv_image_link)

            files.append(discord.File(image))

        # Display a message saying this is an incomplete image set
        if (pixiv_meta_page_count > 5):
            ret['message'] = 'This is part of a {} image set.'.format(pixiv_meta_page_count)
        
        ret['files'] = files

        return ret

    def get_image(self, url):
        image_resp = requests.get(
            url, headers={'Referer': 'https://app-api.pixiv.net/'}, stream=True)

        image_rsp_fp = io.BytesIO(image_resp.content)
        image_rsp_fp.name = url.rsplit('/', 1)[-1]

        return image_rsp_fp
