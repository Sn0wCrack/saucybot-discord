SaucyBot
========

> A discord bot that fills in the gaps for art sites without proper inline embeds.

![GitHub Workflow Status](https://img.shields.io/github/actions/workflow/status/Sn0wCrack/saucybot-discord/continuous-integration.yml?branch=v2)
![Version](https://img.shields.io/github/v/release/Sn0wCrack/saucybot-discord)
![License](https://img.shields.io/github/license/sn0wcrack/saucybot-discord)
[![Support me on Patreon](https://img.shields.io/endpoint.svg?url=https%3A%2F%2Fshieldsio-patreon.vercel.app%2Fapi%3Fusername%3Dsaucybot%26type%3Dpatrons&style=flat)](https://patreon.com/saucybot)
<a target="_blank" href="https://discord.gg/E642ScHyHj">![Discord Server](https://img.shields.io/discord/928546369935917076?color=5764f4&label=discord&logo=discord&logoColor=fff)</a>

If you would like to add this bot to your server [click here](https://discordapp.com/api/oauth2/authorize?client_id=647368715742216193&permissions=67497024&scope=bot) and authorize it through your discord account.

* Currently, Supports:
  * ArtStation - Embeds up to 8 extra images (configurable)
  * Twitter - Embeds posts when native embeds fail
    * Utilises [fxtwitter](https://github.com/FixTweet/FixTweet)'s API
    * Will create an embed when Twitter fails to embed a Link itself
    * Will embed a video if it cannot be played natively in Discord
    * If the images or video are larger than the Discord maximum file size will reply with an fxtwitter link instead.
  * DeviantArt - Embeds main image or thumbnail, includes more information than built-in embed
  * Hentai Foundry - Creates embed as none exists for site.
  * Pixiv - Posts up to 5 images of the set (configurable)
    * Pixiv Ugoira - Uploads a video with correct framerate and frame-timing (Video format is configurable, requires ffmpeg)
  * FurAffinity - Creates image embed as none exists for site
  * Newgrounds - Creates embed for site as image isn't fully embedded, this also displays the rating the image has.
    * NOTE: Doesn't support embedding videos
  * e621 - Creates an embed similar to what discord embeds but with higher quality image and slightly more information
    * NOTE: This is disabled on live version as I think it doesn't add much right now
  * E(x-)Hentai - Creates an embed to preview cover art, title, current score, etc.
    * NOTE: Live version only supports e-hentai.org right now
  * Misskey - Creates an embed for multi-image posts and NSFW posts
    * NOTE: Only supports misskey.io

Installation
----------

**NOTE**: Information here is outdated and is intended for v1

### Production Docker (recommended)

Prerequisites:
 - Docker (https://docs.docker.com/get-docker/)

#### Windows, macOS and Linux

Save the following two files to the same folder, preferably named `SaucyBot`:
 - [docker-compose.yml](https://raw.githubusercontent.com/Sn0wCrack/saucybot-discord/master/docker-compose.prod.yml)
 - [.env](https://raw.githubusercontent.com/Sn0wCrack/saucybot-discord/master/.env.example)

Ensure these are saved in the same folder and are saved as `docker-compose.yml` amd `.env` respectively.

I would also recommend ensuring the full file path to these files contains no spaces.

Open `.env` in a text editor of your choice and adjust the values based on the meaning of these values described on [this page](https://github.com/Sn0wCrack/saucybot-discord/wiki/Environment-Variable-Values).

Please ensure that if you do not intend to use a site in your instance of the bot that you add that site name to the `DISABLED_SITES` environment value.

Once the `.env` file has been adjusted open a terminal in the location you have saved your `docker-compose.yml` and `.env` and run the following:

```shell
docker-compose up -d
```

You should see output to your terminal window indicating that is downloading the required docker images and starting them.

If you are on Windows or macOS, you can check how your instance is running inside the application called `Docker Desktop` under the `Containers` link in the left-hand sidebar.

### Development Docker

Prerequisites:
 - git (https://git-scm.com/)
   - If you're on Windows use https://desktop.github.com/ or https://www.gitkraken.com/
 - Docker (https://docs.docker.com/get-docker/)

<!-- TODO: Improve this section -->

Clone Repository

Run the following command:
```shell
docker-compose -f docker-compose.dev.yml up -d
```

### Standalone

**NOTE**: Information here is outdated and is intended for v1

Prerequisites:
 - git (https://git-scm.com/)
    - If you're on Windows use https://desktop.github.com/ or https://www.gitkraken.com/
 - nodejs (https://nodejs.org/en/)
 - yarn (https://yarnpkg.com/)

<!-- TODO: Improve this section -->

Clone Repository

Run the following commands:
```shell
yarn install
yarn start
```


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
