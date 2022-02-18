import { FileOptions, Message } from 'discord.js';
import BaseSite from './BaseSite';
import ProcessResponse from './ProcessResponse';
import path from 'path';
import Environment from '../Environment';
import os from 'os';
import AdmZip from 'adm-zip';
import fs from 'fs/promises';
import rimraf from 'rimraf';
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

class Pixiv extends BaseSite {
    identifier = 'Pixiv';

    pattern = /https?:\/\/(www\.)?pixiv.net\/.*artworks\/(?<id>\d+)/gim;

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
        source: Message | null
    ): Promise<ProcessResponse | false> {
        await this.api.login();

        const id = parseInt(match.groups.id);

        const response = await this.api.illustDetails(id);

        if (response.body.illustType == IllustType.Ugoira) {
            return this.processUgoira(response);
        }

        return this.processImage(response);
    }

    async processImage(
        details: IllustDetailsResponse
    ): Promise<ProcessResponse | false> {
        const message: ProcessResponse = {
            embeds: [],
            files: [],
        };

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

        const metadata = await this.api.ugoiraMetaData(details.body.id);

        const file = await this.getFile(metadata.body.originalSrc);

        // Because the attachment can be a stirng or buffer, we have to type cast to any, as string can't go to buffer automatically
        const zip = new AdmZip(file.attachment as Buffer);

        const basePath = path.join(os.tmpdir(), details.body.id.toString());

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
        rimraf(basePath, (err) => {
            if (err) {
                Logger.error(err);
            }
        });

        // Snake case and remove hyphens from title
        const fileName = `${details.body.title
            .toLowerCase()
            .replace('-', '')
            .replace(/\s+/g, '_')}_ugoira.${format}`;

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

    async ffmpeg(input: string, output: string): Promise<boolean> {
        // This is required as fluent-ffmpeg doesn't support promises unfortunately
        return new Promise<boolean>((resolve, reject) => {
            ffmpeg({ cwd: path.dirname(input) })
                .input(input)
                .inputFormat('concat')
                .videoBitrate(
                    Environment.get('PIXIV_UGOIRA_BITRATE', 2000) as number
                )
                // Pad the video size to be divisble by two
                // This ensures h264 can actually encode the output
                .videoFilter('pad=ceil(iw/2)*2:ceil(ih/2)*2')
                .on('error', (err) => reject(err))
                .on('end', () => resolve(true))
                .save(output);
        });
    }

    /**
     * Determines the highest quality of an image that can be posted to Discord inside of its size limit
     *
     * @param urls a list of urls from highest quality to lowest quality
     */
    async determineHighestQuality(urls: string[]): Promise<string | false> {
        for (const url of urls) {
            const response = await this.api.pokeFile(url);

            if (parseInt(response.headers['content-length']) < MAX_FILESIZE) {
                return Promise.resolve(url);
            }
        }

        return Promise.resolve(false);
    }

    async getFile(url: string): Promise<FileOptions> {
        const response = await this.api.getFile(url);

        const parsed = new URL(url);

        const file: FileOptions = {
            attachment: response,
            name: path.basename(parsed.pathname),
        };

        return Promise.resolve(file);
    }
}

export default Pixiv;
