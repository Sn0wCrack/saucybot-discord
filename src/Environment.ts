class Environment {
    /* eslint-disable  @typescript-eslint/no-explicit-any */
    static get(key: string | number, fallback: any = null): any {
        return process.env[key] ?? fallback;
    }
}

export default Environment;
