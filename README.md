Saucybot
========

Forked from: [JeremyRuhland's original 'SauceBot'](https://github.com/JeremyRuhland/saucebot-discord)

If you would like to add this bot to your server [click here](https://discordapp.com/api/oauth2/authorize?client_id=647368715742216193&permissions=388096&scope=bot) and authorize it through your discord account.

A discord bot for interacting with multiple art hosting website URLs.

* Currently Supports:
  * ArtStation (Embeds all extra images)
  * e621 (Embeds image in full quality)
  * Hentai Foundry (Creates embed as none exists for site)
  * Pixiv (Posts up to 5 images of the set)
  * FurAffinity (Creates image embed as none exists for site)

Installing
----------

* Linux/macOS:
  * Install Python >= 3.6
  * Install Poetry ([here](https://poetry.eustace.io/docs/]))
  * Run ```poetry install``` in base directory
  * If you recieve a ModuleNotFoundError and you're running Python 3.8 run this command: ```cp -r $HOME/.poetry/lib/poetry/_vendor/py3.7 $HOME/.poetry/lib/poetry/_vendor/py3.8```
  * Run ```cp .env.example .env```
  * Open .env in your editor of choice and fill in the required environment variables
  * Run ```python saucybot/__init__.py```
