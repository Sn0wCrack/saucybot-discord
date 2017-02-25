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

discord_token = 'PASTE YOUR DISCORD BOT TOKEN HERE'

fa_pattern = re.compile('furaffinity\.net/view/\d+')


client = discord.Client()

@client.event
async def on_message(message):
    
    fa_dir_link_list = ''
    
    # we do not want the bot to reply to itself
    if message.author == client.user:
        return

    # Precheck for FA URL
    if 'furaffinity.net/view/' in message.content:
        # Check for all FA links in msg
        fa_links = re.findall(fa_pattern, message.content)
        
        # Process each link
        for (fa_link) in fa_links:
            # Construct API request, chop apart FA URL
            fa_link_url = 'https://faexport.boothale.net/submission/{}.json'.format(fa_link[21:])
            
            # Request submission info
            fa_get = requests.get(fa_link_url)

            # Check for success from API
            if fa_get:
                fa_get_dict = json.loads(fa_get.text)
                    
                # Compile URL list
                fa_dir_link_list = fa_dir_link_list + ' ' + fa_get_dict['download']

    # TODO: Add some additional features here: reverse source lookup, weasyl, etc.
    
        # Send the message to the right channel
        await client.send_message(message.channel, fa_dir_link_list)

@client.event
async def on_ready():
    print('Logged in as')
    print(client.user.name)
    print(client.user.id)
    print('------')

client.run(discord_token)
