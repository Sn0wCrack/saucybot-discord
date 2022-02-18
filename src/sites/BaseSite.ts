import { Message } from 'discord.js';
import ProcessResponse from './ProcessResponse';

abstract class BaseSite {
    identifier = 'Base';

    pattern = /base/i;

    color = 0x000000;

    match(message: string): IterableIterator<RegExpMatchArray> {
        return message.matchAll(this.pattern);
    }

    abstract process(
        match: RegExpMatchArray,
        source: Message | null
    ): Promise<ProcessResponse | false>;
}

export default BaseSite;
