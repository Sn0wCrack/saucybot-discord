class Environment {
    static get(key: string | number, fallback: unknown = null): unknown {
        return process.env[key] ?? fallback;
    }
}

export default Environment;
