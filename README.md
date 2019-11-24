Saucybot
========

Forked from: [JeremyRuhland's original 'SauceBot'](https://github.com/JeremyRuhland/saucebot-discord)

A discord bot for interacting with multiple art hosting website URLs.

Currently supports:

* ArtStation
  * e621
  * Hentai Foundry
  * Pixiv

Installing
----------

* Install Python >= 3.6
  * Install Poetry ([here](https://poetry.eustace.io/docs/]))
  * Run ```poetry install``` in base directory
  * If you recieve a ModuleNotFoundError and you're running Python 3.8 run this command: ```cp -r $HOME/.poetry/lib/poetry/_vendor/py3.7 $HOME/.poetry/lib/poetry/_vendor/py3.8```
  * Run ```cp .env.example .env```
  * Open .env in your editor of choice and fill in the required environment variables
  * Run ```python saucybot/__init__.py```
