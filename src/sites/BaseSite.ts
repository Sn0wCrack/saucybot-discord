import ProcessResponse from './ProcessResponse';

class BaseSite {
    name = 'Base';

    pattern = /base/i;

    color = 0x000000;

    match(message: string): RegExpMatchArray {
        return message.match(this.pattern);
    }

    /* eslint-disable  @typescript-eslint/no-unused-vars */
    async process(match: RegExpMatchArray): Promise<ProcessResponse | false> {
        throw new Error('Not yet implemented');
    }
}

export default BaseSite;
