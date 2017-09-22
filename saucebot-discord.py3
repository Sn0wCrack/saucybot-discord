#!/usr/bin/python3

# Saucebot, a discord bot for interacting with furaffinity URLs

__title__ = 'saucebot-discord'
__author__ = 'Goopypanther'
__license__ = 'GPL'
__copyright__ = 'Copyright 2017 Goopypanther'
__version__ = '0.1'

import discord
import re
import requests
import json
import os

discord_token = os.environ["DISCORD_API_KEY"]

fa_pattern = re.compile('(furaffinity\.net/view/(\d+))')

fapi_url = "https://bawk.space/fapi/submission/{}"

client = discord.Client()


@client.event
async def on_message(message):
    # we do not want the bot to reply to itself
    if message.author == client.user:
        return

    fa_links = fa_pattern.findall(message.content)

    # Process each link
    for (fa_link, fa_id) in fa_links:
        # Request submission info
        fa_get = requests.get(fapi_url.format(fa_id))

        # Check for success from API
        if not fa_get:
            continue

        fapi = json.loads(fa_get.text)
        print(fapi)

        em = discord.Embed(
            title=fapi["title"])
        # discord api does not like it when embed urls are set?
        # it's not of critical importance as the original url will be near
        # em.url = fa_link

        em.set_image(url=fapi["image_url"])
        em.set_author(
            name=fapi["author"],
            icon_url=fapi["avatar"])

        await client.send_message(message.channel, embed=em)


@client.event
async def on_ready():
    print('Logged in as')
    print(client.user.name)
    print(client.user.id)
    print('------')

client.run(discord_token)
