#!/usr/bin/python3

__title__ = 'saucybot'
__author__ = 'Sn0wCrack'
__licnese__ = 'MIT'
__version__ = '0.1.0'

from dotenv import load_dotenv
load_dotenv()

from site_runner import SiteRunner
import config.discord
import discord


client = discord.Client()
runner = SiteRunner()

@client.event
async def on_message(message):

    # Skip messages from bot
    if message.author == client.user:
        return

    response = runner.process(message.content)

    if response:
        if 'message' in response:
            await message.channel.send(response['message'])

        if 'files' in response:
            # Convert array of file-objects to Discord Files
            # This is better than sending individual messages
            discord_files = []
            for file in response['files']:
                discord_files.append(discord.File(file))
            
            await message.channel.send(files=discord_files)

client.run(config.discord.config['discord_token'])
