import requests
import io
import discord
import zipfile
import glob
import shutil
from PIL import Image
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

        if pixiv_result.illust.type == 'ugoira':
            return self.process_gif(pixiv_result)
        else:
            return self.process_image(pixiv_result)

    def process_image(self, pixiv_result):
        pixiv_meta_page_count = len(pixiv_result.illust.meta_pages)

        # We only grab a maximum of 5 to prevent issues with rate-limiting
        # As well as hopefully prevent spam issues
        pixiv_meta_pages = pixiv_result.illust.meta_pages[:5]

        ret = {}

        # If is this a single image post, just send the main illustration image
        if pixiv_meta_page_count == 0:
            pixiv_image_link = pixiv_result.illust.image_urls.large

            image = self.get_file(pixiv_image_link)

            ret['files'] = [discord.File(image)]

            return ret

        # Otherwise, send through all images on the post
        files = []

        for pixiv_meta_page in pixiv_meta_pages:
            pixiv_image_link = pixiv_meta_page.image_urls.large

            image = self.get_file(pixiv_image_link)

            files.append(discord.File(image))

        # Display a message saying this is an incomplete image set
        if (pixiv_meta_page_count > 5):
            ret['message'] = 'This is part of a {} image set.'.format(pixiv_meta_page_count)
        
        ret['files'] = files

        return ret

    def process_gif(self, pixiv_result):
        metadata = self.api.ugoira_metadata(pixiv_result.illust.id)

        z = zipfile.ZipFile(
            self.get_file(metadata.ugoira_metadata.zip_urls.medium)
        )

        z.extractall(path='/tmp/{}'.format(pixiv_result.illust.id))

        frames = []
        images = glob.glob('/tmp/{}/*'.format(pixiv_result.illust.id))

        for i in images:
            new_frame = Image.open(i)
            frames.append(new_frame)

        timings = []

        for f in metadata.ugoira_metadata.frames:
            timings.append(f.delay)

        ret = {}

        stream = io.BytesIO()
        stream.name = 'ugoira.gif'

        frames[0].save(stream, format='GIF', append_images=frames[1:], save_all=True, duration=timings, loop=True)

        # Need to reset stream or discord.py freaks out
        stream.seek(0)

        # We only havea maximum of 8MBs that we can upload at a time
        if (len(stream.getvalue()) >= 8 * (10 ** 6)):
            ret['message'] = 'Ugoira tool large to upload, displaying preview only'

            pixiv_image_link = pixiv_result.illust.image_urls.large

            image = self.get_file(pixiv_image_link)

            ret['files'] = [discord.File(image)]

            return ret

        shutil.rmtree('/tmp/{}'.format(pixiv_result.illust.id))

        ret = {}

        ret['files'] = [discord.File(stream)]

        return ret

    def get_file(self, url):
        file_resp = requests.get(
            url, headers={'Referer': 'https://app-api.pixiv.net/'}, stream=True)

        file_resp_fp = io.BytesIO(file_resp.content)
        file_resp_fp.name = url.rsplit('/', 1)[-1]

        return file_resp_fp