import BaseSite from './sites/BaseSite';

export default interface RunnerResponse {
    site: BaseSite;
    matches: Array<RegExpMatchArray>;
}
