Moderation Guide
================

> Guidance for server owners, administrators, and moderators running or using SaucyBot. This guide explains how SaucyBot handles NSFW content, how to restrict which users can trigger embeds, and how to control where the bot is active using Discord's built-in permissions.

SaucyBot's moderation-related features fall into three areas:

1. [NSFW content handling](#nsfw-content-handling) — how adult content is embedded only in appropriate channels.
2. [The Settings modal](#the-settings-modal) — restricting embeds to specific roles.
3. [Channel-level control with permissions](#channel-level-control-with-permissions) — using Discord's permission system to disable the bot in specific channels.

---

## NSFW content handling

Naively embedding art links into any channel can unintentionally expose adult content. To prevent this, SaucyBot can be run in an **NSFW-restricted mode** that only embeds adult content in channels marked as age-restricted.

### How it works

When the instance operator enables `Bot:RestrictNSFW` (see the [Configuration Reference](setup/configuration.md)), SaucyBot behaves as follows:

- **Message embeds.** When a message contains a link whose content is flagged as NSFW, the resulting embed is only sent if the message's channel is an NSFW channel. When checking an **NSFW thread**, the bot checks the thread's parent channel instead.
- **Slash command.** A `/sauce` command is exposed that is marked as an NSFW-only interaction and replaces the standard command. On the public instance it is only usable in NSFW channels, and NSFW content is never embedded in a non-NSFW context.

> [!NOTE]
> Whether a given link is treated as NSFW is determined per site. ArtStation, Twitter, Pixiv, Bluesky, Misskey, FurAffinity, e621, E(x-)Hentai, and Reddit all report NSFW status when the source provides it; some sites are always treated as NSFW because the source does not expose a rating.

### As a server owner

- On the public instance, SaucyBot's `RestrictNSFW` setting is enabled. This means you can rely on the bot **not** to embed adult content in your SFW channels: an NSFW result sent in a normal channel will simply not be embedded.
- To allow NSFW embeds in a specific location, mark that channel as **age-restricted (NSFW)** in Discord's channel settings. NSFW embeds posted there will be shown normally.
- Adult content will never be shown in DM/private contexts when restriction is active.

### Self-hosting note

If you self-host, you control this with the `Bot:RestrictNSFW` configuration value:

- `true` — enable the restricted behavior described above.
- `false` — the bot embeds all content in all channels and the DM slash command is enabled (not recommended for public instances).

---

## The Settings modal

The `/settings` slash command opens a modal that lets the **server owner** control whether embeddings should be restricted to specific roles.

> [!IMPORTANT]
> The `/settings` command is restricted to the **server owner** only. So a non-owner cannot open the modal and submit changes on the owner's behalf.

The command is only available when the instance has database features enabled (it cannot be used in DM or with the database disabled).

### The "Restrict to Roles" option

The modal contains a **Restrict Roles** toggle and a **Whitelisted Roles** role picker (up to 25 roles):

- **Restrict Roles (off, default).** Any user in the server may post a link and have SaucyBot embed it.
- **Restrict Roles (on).** Only the following users can trigger embeds:
  - The **server owner** (always exempt), and
  - Users who hold **at least one of the whitelisted roles** listed under **Whitelisted Roles**.

When restriction is enabled, a link posted by an un-whitelisted user is simply not embedded by the bot.

### As a server owner / admin

1. Run `/settings` in your server (guild) channel.
2. Toggle **Restrict Roles** on if you want to limit who can generate embeds.
3. Use the **Whitelisted Roles** picker to select the role(s) that are allowed to embed content.
4. Press submit.

> [!NOTE]
> The whitelist grants a **role-based** permission to *generate embeds*. To remove the ability for a setting change to take effect on a per-role or per-user basis, Discord's own role/permission hierarchy still applies for *who may post in a channel at all* — the whitelist only controls who the bot will respond to.

---

## Channel-level control with permissions

SaucyBot does not use a separate "disable this channel" config setting. Instead, it relies on **Discord's native permission system**: if the bot cannot read messages in a channel, it cannot (and will not) respond there.

### How to disable the bot in a specific channel

1. Open your server's **Server Settings → Roles → SaucyBot** (or the bot's role).
2. In the desired text channel: **Channel Settings → Permissions → SaucyBot (role)**.
3. Deny the bot the **Read Messages** (**View Channel**) permission for that channel.

Once the bot can no longer see the channel, it will not see the messages posted there and therefore will not embed anything in it.

### What the bot actually needs to work

For SaucyBot to post embeds, it needs the following permissions on the channel (these should normally be left granted so the bot functions):

| Permission | Purpose |
|---|---|
| **Read Messages / View Channel** | The bot must see messages to detect links. **Removing this disables the bot in that channel.** |
| Read Message History | Lets the bot read the message containing the link. |
| Send Messages | So the bot can post the generated embed. |
| Embed Links | Required to render the embed instead of just the raw URL. |
| Attach Files | Allows the bot to attach images/files related to the embed. |
| Send Messages in Threads | Required for the bot to respond in thread channels. |

> [!TIP]
> To enable/disable SaucyBot across many channels at once, grant or deny the bot's **Read Messages/View Channel** permission on the bot's role in each channel (or use **@everyone** denials overridden by the bot role). For a whole category, apply the permission at the category level.

### Managing NSFW restrictions together with permissions

- For full protection, combine both systems: keep `RestrictNSFW` active (on the public instance it always is) **and** remove the bot's **Read Messages** permission in channels where you want no embeddings at all, even of SFW content.
- Age-restricting a channel both permits NSFW embeds *and* is required by Discord before adult content can be shown there at all.

---

## Frequently asked questions

**Someone posted a link and the bot didn't embed it. Why?**

Possible reasons: restricting-to-roles is on and the poster isn't whitelisted; the message was in a channel where the bot lacks **Read Messages**; the content was NSFW and the channel isn't age-restricted; or the site was disabled by the operator. Check each of these in order.

**Can moderators use `/settings` to change the role whitelist?**

No. The `/settings` command, including submitting the modal, is limited to the **server owner** for security purposes.

**Does the role whitelist stop users from posting links?**

No. It only stops SaucyBot from generating an embed for those users. Whether a user can send a message at all is governed by Discord's normal channel/role permissions.
