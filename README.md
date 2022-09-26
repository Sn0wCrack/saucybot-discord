Saucybot
========

> A discord bot that fills in the gaps for art sites without proper inline embeds.

![GitHub Workflow Status](https://img.shields.io/github/workflow/status/Sn0wCrack/saucybot-discord/CI)
![Version](https://img.shields.io/github/package-json/v/sn0wcrack/saucybot-discord)
![License](https://img.shields.io/github/license/sn0wcrack/saucybot-discord)
[![Support me on Patreon](https://img.shields.io/endpoint.svg?url=https%3A%2F%2Fshieldsio-patreon.vercel.app%2Fapi%3Fusername%3Dsaucybot%26type%3Dpatrons&style=flat)](https://patreon.com/saucybot)
<a target="_blank" href="https://discord.gg/E642ScHyHj">![Discord Server](https://img.shields.io/discord/928546369935917076?color=5764f4&label=discord&logo=discord&logoColor=fff)</a>

If you would like to add this bot to your server [click here](https://discordapp.com/api/oauth2/authorize?client_id=647368715742216193&permissions=67497024&scope=bot) and authorize it through your discord account.

* Currently Supports:
  * ArtStation - Embeds up to X extra images (X is configurable, default 5)
  * Twitter - Embeds posts when native embeds fail
    * NOTE: May not always work as expected due to API rate limits
    * Will create an embed when Twitter fails to embed a Link itself
    * Will embed a video if it cannot be played natively in Discord
  * DeviantArt - Embeds main image or thumbnail, includes more information than built-in embed
  * Hentai Foundry - Creates embed as none exists for site.
  * Pixiv - Posts up to X images of the set (X is configurable, default 5)
    * Pixiv Ugoira - Uploads a video with correct framerate and frametiming (Video format is configurable, requires ffmpeg)
  * FurAffinity - Creates image embed as none exists for site
  * Newgrounds - Creates embed for site as image isn't fully embeded, this also displays the rating the image has.
    * NOTE: Doesn't support embeding videos
  * e621 - Creates an embed similar to what discord embeds but with higher quality image and slightly more information
    * NOTE: This is disabled on live version as I think it doesn't add much right now
  * E(x-)Hentai - Creates an embed to preview cover art, title, current score, etc.
    * NOTE: Live version only supports e-hentai.org right now

Installation
----------

Clone repo

    git pull https://github.com/Sn0wCrack/saucybot-discord.git && cd saucybot-discord

Make a copy of the .env.example file, name it .env and edit the variables to suit your configuration.

    cp .env.example .env && nano .env

Pull the latest docker image, point it to the .env file, create container and run as a service.

    docker run -d --env-file .env ghcr.io/sn0wcrack/saucybot-discord:latest

FAQ
---

TODO

Credits
-------

**JeremyRuhland**:

* Based on their original ['SauceBot'](https://github.com/JeremyRuhland/saucebot-discord)
