#!/usr/bin/python3

__title__ = 'saucybot'
__author__ = 'Sn0wCrack'
__licnese__ = 'MIT'
__version__ = '0.1.0'

from dotenv import load_dotenv
load_dotenv()

from site_runner import SiteRunner
import re
import config.discord
import discord

client = discord.Client()
runner = SiteRunner()

@client.event
async def on_message(message):

    # Skip messages from bot
    if message.author == client.user:
        return

    # If Message is sorrounded by < > it'll be ignored
    if re.match('(<|\|\|)(?!@|#|:|a:).*(>|\|\|)', message.content):
        return

    response = runner.process(message.content)

    if response:
        if 'message' in response:
            await message.channel.send(response['message'])

        if 'files' in response:            
            await message.channel.send(files=response['files'])

        if 'embed' in response:
            await message.channel.send(embed=response['embed'])

        # This is a workaround until Discord supports multiple embeds
        if 'embeds' in response:
            for embed in response['embeds']:
                await message.channel.send(embed=embed)

client.run(config.discord.config['discord_token'])
