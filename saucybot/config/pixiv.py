import os

config = {
    'pixiv_login': os.environ['PIXIV_LOGIN'],
    'pixiv_password': os.environ['PIXIV_PASSWORD'],
    'post_limit': int(os.environ['PIXIV_POST_LIMIT']),
    'ugoira_format': os.environ['PIXIV_UGOIRA_FORMAT']
}