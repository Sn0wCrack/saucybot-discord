import ProcessResponse from './ProcessResponse';

abstract class BaseSite {
    identifier = 'Base';

    pattern = /base/i;

    color = 0x000000;

    match(message: string): IterableIterator<RegExpMatchArray> {
        return message.matchAll(this.pattern);
    }

    abstract process(match: RegExpMatchArray): Promise<ProcessResponse | false>;
}

export default BaseSite;
