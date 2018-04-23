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
import twitter

discord_token = os.environ["DISCORD_API_KEY"]
weasyl_headers = {'X-Weasyl-API-Key': os.environ["WEASYL_API_KEY"]}
twitter_consumer_key = os.environ["TWITTER_CONSUMER_KEY"]
twitter_consumer_secret = os.environ["TWITTER_CONSUMER_SECRET"]
twitter_access_token_key = os.environ["TWITTER_TOKEN_KEY"]
twitter_access_token_secret = os.environ["TWITTER_TOKEN_SECRET"]

fa_pattern = re.compile('(furaffinity\.net/view/(\d+))')
ws_pattern = re.compile('weasyl\.com\/~\w+\/submissions\/(\d+)')
wschar_pattern = re.compile('weasyl\.com\/character\/(\d+)')
da_pattern = re.compile('deviantart\.com.*.\d')
e621_pattern = re.compile('e621\.net\/post/show\/(\d+)')
twitter_pattern = re.compile('twitter.com/\w+/status/(\d+)')

fapi_url = "https://bawk.space/fapi/submission/{}"
wsapi_url = "https://www.weasyl.com/api/submissions/{}/view"
wscharapi_url = "https://www.weasyl.com/api/characters/{}/view"
daapi_url = "https://backend.deviantart.com/oembed?url={}"
e621api_url = "https://e621.net/post/show.json?id={}"


twitterapi = twitter.Api(consumer_key=twitter_consumer_key,
                  consumer_secret=twitter_consumer_secret,
                  access_token_key=twitter_access_token_key,
                  access_token_secret=twitter_access_token_secret)
                  
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

        # FA now embeds general submissions, skip in that case
        if fapi["rating"] == "general":
            continue

        print(message.author.name + '#' + message.author.discriminator + '@' + message.server.name + ':' + message.channel.name + ': ' + fapi["image_url"])

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
        print(message.author.name + '#' + message.author.discriminator + '@' + message.server.name + ':' + message.channel.name + ': ' + wsapi["media"]["submission"][0]["links"]["cover"][0]["url"])

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
        print(message.author.name + '#' + message.author.discriminator + '@' + message.server.name + ':' + message.channel.name + ': ' + wscharapi["media"]["submission"][0]["links"]["cover"][0]["url"])

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
        print(message.author.name + '#' + message.author.discriminator + '@' + message.server.name + ':' + message.channel.name + ': ' + daapi["url"])

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
        print(message.author.name + '#' + message.author.discriminator + '@' + message.server.name + ':' + message.channel.name + ': ' + e621api["file_url"])

        em = discord.Embed(
            title=e621api["artist"][0])

        em.set_image(url=e621api["file_url"])

        await client.send_message(message.channel, embed=em)


    twitter_links = twitter_pattern.findall(message.content)
    tweet_media = ''
    
    # Process each twitter link
    for (tweet) in twitter_links:
        # Request tweet
        tweet_status = twitterapi.GetStatus(tweet)
        
        # Check for success from API
        if not tweet_status:
            continue

        # Get media links in tweet
        for (media_num, media_item) in enumerate(tweet_status.media):
            # Check if media is an image and not first image (disp. by embed)
            if (media_item.type == 'photo') and (media_num > 0):
                tweet_media += media_item.media_url_https + ' \n '
            
            # Disabling video feature since it can be played in the embed
            # Can there be multiple videos per tweet?
            #elif (media_item.type == 'video'):
            #    tweet_media += media_item.video_info['variants'][0]['url']
    
    # Check if any non-displayed media was found in any tweets in msg
    if len(tweet_media) > 0:        
        print(message.author.name + '#' + message.author.discriminator + '@' + message.server.name + ':' + message.channel.name + ': ' + tweet_media)

        await client.send_message(message.channel, tweet_media)


@client.event
async def on_ready():
    print('Logged in as')
    print(client.user.name)
    print(client.user.id)
    print('------')

client.run(discord_token)
