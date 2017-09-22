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
weasyl_headers = {'X-Weasyl-API-Key': os.environ["WEASYL_API_KEY"]}

fa_pattern = re.compile('(furaffinity\.net/view/(\d+))')
ws_reverse_pattern = re.compile('(\d+)(?=/snoissimbus/.*moc.lysaew)')

fapi_url = "https://bawk.space/fapi/submission/{}"
wsapi_url = "https://www.weasyl.com/api/submissions/{}/view"

client = discord.Client()


@client.event
async def on_message(message):
    # we do not want the bot to reply to itself
    if message.author == client.user:
        return

    fa_links = fa_pattern.findall(message.content)

    # Process each fa link
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
        
    # Reverse message content to perform regex using lookahead instead of
    # lookbehind because lookbehind does not support matching to indeterminate
    # length substrings which we need to catch a possible username.
    ws_links = ws_reverse_pattern.findall(message.content[::-1])
    
        # Process each ws link
    for (ws_id) in ws_links:
        ws_id = ws_id[::-1] # Un-reverse ID matched from revered message content
        
        # Request submission info
        ws_get = requests.get(wsapi_url.format(ws_id), headers=weasyl_headers)

        # Check for success from API
        if not ws_get:
            continue

        wsapi = json.loads(ws_get.text)
        print(wsapi)

        em = discord.Embed(
            title=wsapi["title"])

        # Discord didn't want to load the submission image, but the link worked
        em.set_image(url=wsapi["media"]["submission"][0]["links"]["cover"][0]["url"])
        em.set_author(
            name=wsapi["owner"],
            icon_url=wsapi["owner_media"]["avatar"][0]["url"])

        await client.send_message(message.channel, embed=em)


@client.event
async def on_ready():
    print('Logged in as')
    print(client.user.name)
    print(client.user.id)
    print('------')

client.run(discord_token)
