using Discord;

namespace SaucyBot.Library;

public static class Constants
{
    #region Discord
    
    /// <summary>
    /// The maximum file size that Discord allows to be uploaded.
    /// </summary>
    public const long MaximumFileSize = 8_388_119;

    /// <summary>
    /// The maximum number of embeds that Discord allows per message.
    /// </summary>
    public const int MaximumEmbedsPerMessage = 4;

    /// <summary>
    /// The URL to the Twitter favicon that Discord uses for their embeds.
    /// </summary>
    public const string TwitterIconUrl =
        "https://images-ext-1.discordapp.net/external/bXJWV2Y_F3XSra_kEqIYXAAsI3m1meckfLhYuWzxIfI/https/abs.twimg.com/icons/apple-touch-icon-192x192.png";

    public const string ArtStationIconUrl = "https://i.imgur.com/Wv9JJlS.png";

    public const string EHentaiIconUrl = "https://i.imgur.com/PSEmcWO.png";

    public const string HentaiFoundryIconUrl = "https://i.imgur.com/dOECfxG.png";

    public const string DeviantArtIconUrl = "https://st.deviantart.net/eclipse/icons/ios-180.png";
    
    #endregion
    
    #region Bot

    public const GatewayIntents RequiredGatewayIntents =
        GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent;

    public const ChannelPermission RequiredChannelPermissions = ChannelPermission.ReadMessageHistory |
                                                                 ChannelPermission.SendMessages |
                                                                 ChannelPermission.EmbedLinks |
                                                                 ChannelPermission.AttachFiles;

    public const ChannelPermission RequiredThreadPermissions =
        RequiredChannelPermissions | ChannelPermission.SendMessagesInThreads; 
    
    /// <summary>
    /// The default maximum number of embeds to attempt to send in a single processing run.
    ///
    /// This is primarily used as a fallback value.
    /// </summary>
    public const int DefaultMaximumEmbeds = 8;
    
    /// <summary>
    /// The default maximum number of Pixiv images to return for a Pixiv illustration.
    ///
    /// This is primarily used as a fallback value.
    /// </summary>
    public const int DefaultMaximumPixivImages = 5;
    
    /// <summary>
    /// The default maximum number of ArtStation images to return for an ArtStation project.
    ///
    /// This is primarily used as a fallback value.
    /// </summary>
    public const int DefaultMaximumArtStationImages = 8;
    
    
    /// <summary>
    /// The default setting for sending the "Matched to site X, please wait..." message for guilds.
    /// </summary>
    public const bool DefaultSendMatchedMessage = true;

    #endregion
}
