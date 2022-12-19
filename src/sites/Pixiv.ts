import { AttachmentPayload, Message } from 'discord.js';
import BaseSite from './BaseSite';
import ProcessResponse from './ProcessResponse';
import path from 'path';
import Environment from '../Environment';
import os from 'os';
import AdmZip from 'adm-zip';
import fs from 'fs/promises';
import ffmpeg from 'fluent-ffmpeg';
import { MAX_FILESIZE } from '../Constants';
import PixivWeb from 'pixiv-web-api';
import {
    IllustDetailsResponse,
    UgoiraFrame,
} from 'pixiv-web-api/dist/ResponseTypes';
import { IllustType } from 'pixiv-web-api/dist/IllustType';
import Logger from '../Logger';
import { URL } from 'url';
import CacheManager from '../CacheManager';
import { randomString } from '../Helpers';

class Pixiv extends BaseSite {
    identifier = 'Pixiv';

    pattern =
        /https?:\/\/(www\.)?pixiv\.net\/(.*artworks\/(?<new_id>\d+)|member_illust.php\?(.*)?illust_id=(?<old_id>\d+))\/?/gim;

    private api: PixivWeb;

    constructor() {
        super();

        this.api = new PixivWeb({
            username: Environment.get('PIXIV_LOGIN') as string,
            password: Environment.get('PIXIV_PASSWORD') as string,
            cookie: Environment.get('PIXIV_COOKIE') as string,
        });
    }

    async process(
        match: RegExpMatchArray,
        /* eslint-disable @typescript-eslint/no-unused-vars */
        source: Message | null
    ): Promise<ProcessResponse | false> {
        // NOTE: Cloudflare is currently breaking getting user information
        // however fetching images still appears to function just fine
        // so this will just be ignored for now.
        // await this.api.login();

        if (!match.groups?.new_id && !match.groups?.old_id) {
            return Promise.resolve(false);
        }

        const id = parseInt(match.groups.new_id ?? match.groups.old_id);

        const response = await this.getIllustrationDetails(id);

        if (!response) {
            return Promise.resolve(false);
        }

        if (response.body?.illustType == IllustType.Ugoira) {
            return this.processUgoira(response);
        }

        return this.processImage(response);
    }

    async getIllustrationDetails(
        id: number
    ): Promise<IllustDetailsResponse | null> {
        const cacheKey = `pixiv.illustration_${id}`;
        const cacheManager = await CacheManager.getInstance();

        const cachedValue = await cacheManager.remember(cacheKey, async () => {
            const results = await this.api.illustDetails(id);
            return Promise.resolve(JSON.stringify(results));
        });

        if (!cachedValue) {
            return Promise.resolve(null);
        }

        const results = JSON.parse(cachedValue) as IllustDetailsResponse;

        return Promise.resolve(results);
    }

    async processImage(
        details: IllustDetailsResponse
    ): Promise<ProcessResponse | false> {
        const message: ProcessResponse = {
            embeds: [],
            files: [],
        };

        if (!details.body) {
            return Promise.resolve(false);
        }

        const pageCount = details.body.pageCount;

        if (pageCount == 1) {
            const urls = [
                details.body.urls.original,
                details.body.urls.regular,
                details.body.urls.small,
            ];

            const url = await this.determineHighestQuality(urls);

            if (!url) {
                return Promise.resolve(message);
            }

            const file = await this.getFile(url);

            message.files.push(file);

            return Promise.resolve(message);
        }

        const pagesDetails = await this.api.illustPages(details.body.id);

        if (!pagesDetails.body) {
            return Promise.resolve(false);
        }

        const postLimit = Environment.get('PIXIV_POST_LIMIT', 5) as number;

        const pages = pagesDetails.body.slice(0, postLimit);

        for (const page of pages) {
            const urls = [
                page.urls.original,
                page.urls.regular,
                page.urls.small,
            ];

            const url = await this.determineHighestQuality(urls);

            if (!url) {
                continue;
            }

            const file = await this.getFile(url);

            message.files.push(file);
        }

        if (pageCount > postLimit) {
            message.text = `This is part of a ${pageCount} image set.`;
        }

        return Promise.resolve(message);
    }

    async processUgoira(
        details: IllustDetailsResponse
    ): Promise<ProcessResponse | false> {
        const message: ProcessResponse = {
            embeds: [],
            files: [],
        };

        if (!details.body) {
            return Promise.resolve(false);
        }

        const metadata = await this.api.ugoiraMetaData(details.body.id);

        if (!metadata.body) {
            return Promise.resolve(false);
        }

        const file = await this.getFile(metadata.body.originalSrc);

        const zip = new AdmZip(file.attachment as Buffer);

        const basePath = path.join(
            os.tmpdir(),
            'pixiv',
            `${details.body.id.toString()}_${randomString()}`
        );

        const concatFilePath = path.join(basePath, 'ffconcat');

        const format = Environment.get('PIXIV_UGOIRA_FORMAT', 'mp4') as string;

        const videoFilePath = path.join(basePath, `ugoira.${format}`);

        zip.extractAllTo(basePath, true);

        await fs.writeFile(
            concatFilePath,
            this.buildConcatFile(metadata.body.frames)
        );

        try {
            await this.ffmpeg(concatFilePath, videoFilePath);
        } catch (ex) {
            Logger.error(ex);
            return Promise.resolve(false);
        }

        const video = await fs.readFile(videoFilePath);

        // Remove all files in the temporary directory
        try {
            await fs.rm(basePath, { recursive: true });
        } catch (ex) {
            Logger.error(
                `Failed to cleanup Pixiv temporary directory with error: ${ex?.message}`
            );
        }

        const title = details.body.title
            .toLowerCase()
            .replace('-', '')
            .replace(/\s+/g, '_');

        // Snake case and remove hyphens from title
        const fileName = `${title}_ugoira.${format}`;

        message.files.push({
            attachment: video,
            name: fileName,
        });

        return Promise.resolve(message);
    }

    buildConcatFile(frames: Array<UgoiraFrame>): string {
        // Adding this header ensures the file path resolution is in safe mode
        let concat = 'ffconcat version 1.0\n';

        for (const frame of frames) {
            const delay = frame.delay / 1000;

            concat += `file ${frame.file}\n`;
            concat += `duration ${delay}\n`;
        }

        // We add the last frame in again as it creates a more natural look to the final frame
        const lastFrame = frames[frames.length - 1];

        concat += `file ${lastFrame.file}\n`;

        return concat;
    }

    async ffmpeg(input: string, output: string): Promise<void> {
        // This is required as fluent-ffmpeg doesn't support promises unfortunately
        return new Promise<void>((resolve, reject) => {
            const command = ffmpeg({ cwd: path.dirname(input) })
                .input(input)
                .inputFormat('concat')
                .videoBitrate(
                    Environment.get('PIXIV_UGOIRA_BITRATE', 2000) as number
                )
                // Discord and various Browsers do not properly support yuv444 (4:4:4 chroma subsampling)
                // so we have to always specify yuv420p as its the widely compatible pixel format.
                .addOption('-pix_fmt yuv420p')
                // Pad the video size to be divisble by two
                // This ensures h264 can actually encode the output
                .videoFilter('pad=ceil(iw/2)*2:ceil(ih/2)*2')
                .on('error', (err) => reject(err))
                .on('end', () => resolve());

            command.save(output);
        });
    }

    /**
     * Determines the highest quality of an image that can be posted to Discord inside its size limit
     *
     * @param urls a list of urls from the highest quality to the lowest quality
     */
    async determineHighestQuality(urls: string[]): Promise<string | false> {
        for (const url of urls) {
            const response = await this.api.pokeFile(url);

            if (!response.headers?.['content-length']) {
                continue;
            }

            if (parseInt(response.headers['content-length']) < MAX_FILESIZE) {
                return Promise.resolve(url);
            }
        }

        return Promise.resolve(false);
    }

    async getFile(url: string): Promise<AttachmentPayload> {
        const response = await this.api.getFile(url);

        const parsed = new URL(url);

        const file: AttachmentPayload = {
            attachment: response,
            name: path.basename(parsed.pathname),
        };

        return Promise.resolve(file);
    }
}

export default Pixiv;
