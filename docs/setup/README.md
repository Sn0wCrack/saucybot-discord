Setup Guides
============

> SaucyBot is a Discord bot that provides inline embeds for art sites without proper embed support.

Choose a setup method that fits your needs:

- [Production](production.md) — Recommended for self-hosting SaucyBot in a production environment using a container runtime (Docker or Podman).
- [Development](development.md) — For contributors who want to develop and test SaucyBot locally using a container runtime (Docker or Podman).
- [Standalone](standalone.md) — For running SaucyBot directly on your machine without Docker.

- [Configuration Reference](configuration.md) — Reference for all `.env` and `appsettings.json` settings.

Supported Sites
------------

| Site | Description |
|---|---|
| **ArtStation** | Embeds up to 8 extra images (configurable). |
| **Twitter** | Embeds posts when native embeds fail. Uses [fxtwitter](https://github.com/FixTweet/FixTweet)'s API. Creates an embed when Twitter fails to embed a link itself. Embeds video if it cannot be played natively in Discord. Falls back to an fxtwitter link when files exceed Discord's maximum size. |
| **DeviantArt** | Embeds main image or thumbnail with more information than the built-in embed. |
| **Hentai Foundry** | Creates an embed as none exists for the site. |
| **Pixiv** | Posts up to 5 images of the set (configurable). Supports Ugoira video conversion with correct framerate and frame-timing (format is configurable, requires ffmpeg). |
| **FurAffinity** | Creates an image embed as none exists for the site. |
| **Newgrounds** | Creates an embed with full image and rating information. |
| **e621** | Creates an embed with higher quality images and more information than Discord's default. |
| **E(x-)Hentai** | Creates an embed to preview cover art, title, current score, etc. |
| **Misskey** | Creates an embed for multi-image posts and NSFW posts. Only supports misskey.io. |
| **Instagram** | Rewrites URLs to kkinstagram.com for improved embeds. Supports Posts and Reels. No external API calls — simple URL domain rewrite. |
