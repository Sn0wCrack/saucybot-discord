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
ws_pattern = re.compile('weasyl\.com\/~\w+\/submissions\/(\d+)')
wschar_pattern = re.compile('weasyl\.com\/character\/(\d+)')
da_pattern = re.compile('deviantart\.com.*.\d')
e621_pattern = re.compile('e621\.net\/post/show\/(\d+)')

fapi_url = "https://bawk.space/fapi/submission/{}"
wsapi_url = "https://www.weasyl.com/api/submissions/{}/view"
wscharapi_url = "https://www.weasyl.com/api/characters/{}/view"
daapi_url = "https://backend.deviantart.com/oembed?url={}"
e621api_url = "https://e621.net/post/show.json?id={}"

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
        
        
    ws_links = ws_pattern.findall(message.content)
    
    # Process each ws link
    for (ws_id) in ws_links:
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


    wschar_links = wschar_pattern.findall(message.content)
    
    # Process each ws character page link
    for (wschar_id) in wschar_links:
        # Request submission info
        wschar_get = requests.get(wscharapi_url.format(wschar_id), headers=weasyl_headers)

        # Check for success from API
        if not wschar_get:
            continue

        wscharapi = json.loads(wschar_get.text)
        print(wscharapi)

        em = discord.Embed(
            title=wscharapi["title"])

        # Discord didn't want to load the submission image, but the link worked
        em.set_image(url=wscharapi["media"]["submission"][0]["links"]["cover"][0]["url"])
        em.set_author(
            name=wscharapi["owner"],
            icon_url=wscharapi["owner_media"]["avatar"][0]["url"])

        await client.send_message(message.channel, embed=em)
        
        
    da_links = da_pattern.findall(message.content)
    
    # Process each da link
    for (da_id) in da_links:
        # Request submission info
        da_get = requests.get(daapi_url.format(da_id))

        # Check for success from API
        if not da_get:
            continue

        daapi = json.loads(da_get.text)
        print(daapi)

        em = discord.Embed(
            title=daapi["title"])

        em.set_image(url=daapi["url"])
        em.set_author(
            name=daapi["author_name"],
            icon_url=em.Empty)

        await client.send_message(message.channel, embed=em)


    e621_links = e621_pattern.findall(message.content)

    # Process each e621 link
    for (e621_id) in e621_links:
        # Request submission info
        e621_get = requests.get(e621api_url.format(e621_id))

        # Check for success from API
        if not e621_get:
            continue

        e621api = json.loads(e621_get.text)
        print(e621api)

        em = discord.Embed(
            title=e621api["artist"][0])

        em.set_image(url=e621api["file_url"])

        await client.send_message(message.channel, embed=em)
 
 
@client.event
async def on_ready():
    print('Logged in as')
    print(client.user.name)
    print(client.user.id)
    print('------')

client.run(discord_token)
