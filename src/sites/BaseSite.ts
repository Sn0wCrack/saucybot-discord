import { ProcessResponse } from "./ProcessResponse";

class BaseSite
{
    name = 'Base';

    pattern = /base/;

    color = 0x000000;

    match (message: string): RegExpMatchArray {
        return message.match(this.pattern)
    }

    async process (match: RegExpMatchArray): Promise<ProcessResponse|false> {
        throw new Error('Not yet implemented');
    }
}

export default BaseSite