import { FileOptions } from "discord.js";
import BaseSite from "./BaseSite";
import { ProcessResponse } from "./ProcessResponse";
import got from "got";
import path from 'path';
import PixivAppApi from 'pixiv-app-api';
import Environment from "../Environment";
import { PixivIllustDetail } from "pixiv-app-api/dist/PixivTypes";
import os from 'os';
import AdmZip from "adm-zip";
import fs from 'fs/promises';
import rimraf from "rimraf";
import ffmpeg from 'fluent-ffmpeg';

class Pixiv extends BaseSite
{
    name = 'Pixiv';

    pattern = /pixiv.net\/.*artworks\/(?<id>\d+)/;

    api: PixivAppApi;

    constructor () {
        super();

        this.api = new PixivAppApi(
            Environment.get('PIXIV_LOGIN'),
            Environment.get('PIXIV_PASSWORD'),
            {
                camelcaseKeys: true,
            }
        );
    }

    async process (match: RegExpMatchArray): Promise<ProcessResponse|false> {

        await this.api.login();

        const id = parseInt(match.groups.id);

        const response = await this.api.illustDetail(id);

        if (response.illust.type == 'ugoira') {
            return this.processUgoira(response);
        }

        return this.processImage(response);
    }

    async processImage (details: PixivIllustDetail): Promise<ProcessResponse|false> {
        const message: ProcessResponse = {
            embeds: [],
            files: [],
        };

        const pageCount = details.illust.metaPages.length;

        if (pageCount == 0) {
            const file = await this.getFile(details.illust.imageUrls.large);

            message.files.push(file);

            return Promise.resolve(message);
        }

        const postLimit = Environment.get('PIXIV_POST_LIMIT', 5);

        const metaPages = details.illust.metaPages.slice(0, postLimit);

        for (const page of metaPages) {
            const file = await this.getFile(page.imageUrls.large);

            message.files.push(file);
        }

        if (pageCount > postLimit) {
            message.text = `This is part of a ${pageCount} image set.`;
        }

        return Promise.resolve(message);
    }

    async processUgoira (details: PixivIllustDetail): Promise<ProcessResponse|false> {
        const message: ProcessResponse = {
            embeds: [],
            files: [],
        };

        const metadata = await this.api.ugoiraMetaData(details.illust.id);

        const file = await this.getFile(metadata.ugoiraMetadata.zipUrls.medium);

        // Because the attachment can be a stirng or buffer, we have to type cast to any, as string can't go to buffer automatically
        const buffer: any = file.attachment;

        const zip = new AdmZip(buffer);

        const basePath = path.join(os.tmpdir(), details.illust.id.toString());

        const concatFilePath = path.join(basePath, 'ffconcat');

        const format = Environment.get('PIXIV_UGOIRA_FORMAT', 'mp4');

        const videoFilePath = path.join(basePath, `ugoira.${format}`)

        zip.extractAllTo(basePath, true);

        const ffconcat = this.buildConcatFile(metadata.ugoiraMetadata.frames);

        await fs.writeFile(concatFilePath, ffconcat);

        await this.ffmpeg(concatFilePath, videoFilePath);

        const video = await fs.readFile(videoFilePath);

        // Remove all files in the temporary directory
        rimraf(basePath, (err) => {
            if (err) {
                console.error(err);
            }
        });

        // Snake case and remove hyphens from title
        const fileName = `${details.illust.title.toLowerCase().replace('-', '').replace(/\s+/g, '_')}_ugoira.${format}`;

        message.files.push({
            attachment: video,
            name: fileName,
        });

        return Promise.resolve(message);
    }

    buildConcatFile (frames: Array<Record<string, any>>): string {
        let concat = '';

        for (const frame of frames) {
            const delay: number = frame.delay / 1000;

            concat += `file ${frame.file}\n`;
            concat += `duration ${delay}\n`;
        }

        const lastFrame = frames[frames.length - 1];

        concat += `file ${lastFrame.file}\n`

        return concat;
    }

    async ffmpeg (input: string, output: string): Promise<boolean> {
        // This is required as fluent-ffmpeg doesn't support promises unfortunately
        return new Promise<boolean>((resolve, reject) => {
            ffmpeg({ cwd: path.dirname(input) })
                .input(input)
                .inputFormat('concat')
                .videoBitrate(2000)
                .on('error', (err) => reject(err))
                .on('end', () => resolve(true))
                .save(output)
        });
    }

    async getFile (url: string): Promise<FileOptions> {

        const response = await got.get(url, {
            responseType: 'buffer',
            headers: {
                'Referer': 'https://app-api.pixiv.net/'
            },
        })
        .buffer()

        const file: FileOptions = {
            attachment: response,
            name: path.basename(new URL(url).pathname),
        }

        return Promise.resolve(file);
    }
}

export default Pixiv;
