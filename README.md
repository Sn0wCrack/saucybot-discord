SaucyBot
========

> A discord bot that fills in the gaps for art sites without proper inline embeds.

![GitHub Workflow Status](https://img.shields.io/github/actions/workflow/status/Sn0wCrack/saucybot-discord/continuous-integration.yml?branch=v2)
![Version](https://img.shields.io/github/v/release/Sn0wCrack/saucybot-discord)
![License](https://img.shields.io/github/license/sn0wcrack/saucybot-discord)
[![Support me on Patreon](https://img.shields.io/endpoint.svg?url=https%3A%2F%2Fshieldsio-patreon.vercel.app%2Fapi%3Fusername%3Dsaucybot%26type%3Dpatrons&style=flat)](https://patreon.com/saucybot)
<a target="_blank" href="https://discord.gg/E642ScHyHj">![Discord Server](https://img.shields.io/discord/928546369935917076?color=5764f4&label=discord&logo=discord&logoColor=fff)</a>

If you would like to add this bot to your server [click here](https://discord.com/oauth2/authorize?client_id=647368715742216193) and authorize it through your discord account.

**NOTE**: The live version specifically gates NSFW content to NSFW channels. If it fails to embed content, try again in a NSFW channel. This is a requirement set by Discord to me and I cannot change this.

* Currently, Supports:
  * ArtStation - Embeds up to 8 extra images (configurable)
  * Twitter - Embeds posts when native embeds fail
    * Utilises [fxtwitter](https://github.com/FixTweet/FixTweet)'s API
    * Will create an embed when Twitter fails to embed a Link itself
    * Will embed a video if it cannot be played natively in Discord
    * If the images or video are larger than the Discord maximum file size will reply with an fxtwitter link instead.
  * DeviantArt - Embeds main image or thumbnail, includes more information than built-in embed
    * **NOTE**: This is disabled on live version due to IP rate limiting from DeviantArt
  * Hentai Foundry - Creates embed as none exists for site.
  * Pixiv - Posts up to 5 images of the set (configurable)
    * Pixiv Ugoira - Uploads a video with correct framerate and frame-timing (Video format is configurable, requires ffmpeg)
  * FurAffinity - Creates image embed as none exists for site
  * Newgrounds - Creates embed for site as image isn't fully embedded, this also displays the rating the image has.
    * **NOTE**: No longer functions due to issues with Newgrounds authentication system.
    * **NOTE**: Doesn't support embedding videos
  * e621 - Creates an embed similar to what discord embeds but with higher quality image and slightly more information
    * **NOTE**: This is disabled on live version as I think it doesn't add much right now
  * E(x-)Hentai - Creates an embed to preview cover art, title, current score, etc.
    * **NOTE**: Live version only supports e-hentai.org right now
  * Misskey - Creates an embed for multi-image posts and NSFW posts
    * NOTE: Only supports misskey.io
  * Instagram - Rewrites URLs to kkinstagram.com for improved embeds
    * Supports Posts (/p/) and Reels (/reel/, /reels/)
    * No external API calls, simple URL domain rewrite
    * **NOTE**: This is disabled on live version as it is currently broken. 

Setup
----------

See [docs/setup](docs/setup) for installation and configuration guides covering Docker (production and development) and standalone setups.

FAQ
---

### Question: There is terminology I don't understand, can you please explain it to me?
**Answer:** A list of terms I use relating to SaucyBot, Discord Bots or Discord itself can be found on [this page](https://github.com/Sn0wCrack/saucybot-discord/wiki/Glossary)

### Question: Can I adjust the number of images embed by Pixiv or ArtStation?
**Answer:** Currently this is not supported if you are using the publicly hosted version of SaucyBot.
This will be something that will hopefully be configurable in SaucyBot v2 when that is completed.

### Question: Can I adjust the sites SaucyBot embeds on my server?
**Answer:** Currently this is not supported if you are using the publicly hosted version of SaucyBot.
This will be something that will hopefully be configurable in SaucyBot v2 when that is completed.

### Question: Can I have SaucyBot ignore certain channels?
**Answer:** This is not something SaucyBot needs to do itself and can be accomplished in Discord, you are able adjust the permissions on that channel and remove the SaucyBot groups permission to view that channel.
If you have provided Administrator privileges to SaucyBot that override that value, I would **highly suggest** removing SaucyBot from any additional groups that provide it with escalated privileges it does not require.

Credits
-------

**JeremyRuhland**:

* Based on their original ['SauceBot'](https://github.com/JeremyRuhland/saucebot-discord)
