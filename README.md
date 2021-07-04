Saucybot
========

> A discord bot that fills in the gaps for art sites without proper inline embeds.

![GitHub Workflow Status](https://img.shields.io/github/workflow/status/Sn0wCrack/saucybot-discord/CI)
![Version](https://img.shields.io/github/package-json/v/sn0wcrack/saucybot-discord)
![License](https://img.shields.io/github/license/sn0wcrack/saucybot-discord)

**NOTE**: Currently the bot is pending verification so it cannot be added to anymore servers, please see more details here: <https://github.com/Sn0wCrack/saucybot-discord/issues/9>

If you would like to add this bot to your server [click here](https://discordapp.com/api/oauth2/authorize?client_id=647368715742216193&permissions=67497024&scope=bot) and authorize it through your discord account.

* Currently Supports:
  * ArtStation - Embeds up to X extra images (X is configurable, default 5)
  * Twitter Videos - Embeds a Twitter videoes and GIFs
    * NOTE: On the live version this is temporary until discord fixes video embeds from Twitter, may not always work as expected due to API rate limits
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

Installion
----------

TODO

FAQ
---

TODO

Credits
-------

**JeremyRuhland**:

* Based on their original ['SauceBot'](https://github.com/JeremyRuhland/saucebot-discord)
